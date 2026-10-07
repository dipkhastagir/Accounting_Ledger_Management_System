using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Services;

public interface IFiscalYearService
{
    Task<OpResult> CloseAsync(int fiscalYearId);
    Task<OpResult> ReopenAsync(int fiscalYearId);
    Task<(decimal Income, decimal Expense, int Pending)> PreviewCloseAsync(int fiscalYearId);
}

/// <summary>
/// Year-end close: zeroes every income and expense account into Retained
/// Earnings with a sealed system journal, then locks the period.
/// </summary>
public class FiscalYearService : IFiscalYearService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public FiscalYearService(AppDbContext db, IAccountingService accounting, ICurrentUser user, IAuditService audit)
    {
        _db = db;
        _accounting = accounting;
        _user = user;
        _audit = audit;
    }

    private async Task<List<(ChartOfAccount Account, decimal D, decimal C)>> PnlBalancesAsync(FiscalYear fy)
    {
        var accounts = await _db.ChartOfAccounts.ToDictionaryAsync(a => a.Id);
        var pnlIds = accounts.Values.Where(a => a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense).Select(a => a.Id).ToList();
        var rows = await _db.LedgerEntries
            .Where(l => l.EntryDate >= fy.StartDate && l.EntryDate <= fy.EndDate && pnlIds.Contains(l.AccountId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();
        return rows.Select(r => (accounts[r.AccountId], r.D, r.C)).ToList();
    }

    public async Task<(decimal Income, decimal Expense, int Pending)> PreviewCloseAsync(int fiscalYearId)
    {
        var fy = await _db.FiscalYears.FindAsync(fiscalYearId);
        if (fy == null) return (0, 0, 0);
        var bal = await PnlBalancesAsync(fy);
        var income = bal.Where(b => b.Account.AccountType == AccountType.Income).Sum(b => b.C - b.D);
        var expense = bal.Where(b => b.Account.AccountType == AccountType.Expense).Sum(b => b.D - b.C);
        var pending = await _db.Vouchers.CountAsync(v => v.VoucherDate >= fy.StartDate && v.VoucherDate <= fy.EndDate
            && (v.Status == VoucherStatus.Draft || v.Status == VoucherStatus.PendingApproval || v.Status == VoucherStatus.Rejected));
        return (income, expense, pending);
    }

    public async Task<OpResult> CloseAsync(int fiscalYearId)
    {
        var fy = await _db.FiscalYears.FindAsync(fiscalYearId);
        if (fy == null) return OpResult.Fail("Fiscal year not found.");
        if (fy.IsClosed) return OpResult.Fail("This fiscal year is already closed.");

        var pending = await _db.Vouchers.CountAsync(v => v.VoucherDate >= fy.StartDate && v.VoucherDate <= fy.EndDate
            && v.Status == VoucherStatus.PendingApproval);
        if (pending > 0)
            return OpResult.Fail($"{pending} voucher(s) in this year are still waiting for approval. Approve or reject them first.");

        var retained = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Code == "3002")
                       ?? await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountType == AccountType.Equity && !a.IsGroup && a.Name.Contains("Retained"));
        if (retained == null) return OpResult.Fail("Create a Retained Earnings equity account (code 3002) before closing the year.");

        var balances = await PnlBalancesAsync(fy);
        var lines = new List<VoucherDetail>();
        decimal net = 0;
        foreach (var (acc, d, c) in balances)
        {
            var bal = d - c;
            if (bal == 0) continue;
            // Reverse the balance: debit-balanced expenses are credited, credit-balanced income is debited.
            lines.Add(new VoucherDetail { AccountId = acc.Id, Debit = bal < 0 ? -bal : 0, Credit = bal > 0 ? bal : 0, LineNarration = "Year-end closing" });
            net += bal; // positive net = loss (expenses exceed income)
        }

        var actor = _user.Id != 0 ? _user.Id
            : await _db.Users.Where(u => u.Role == Roles.Admin).Select(u => u.Id).FirstAsync();
        int? closingId = null;
        if (lines.Count > 0)
        {
            if (net != 0)
                lines.Add(new VoucherDetail
                {
                    AccountId = retained.Id,
                    Debit = net > 0 ? net : 0,
                    Credit = net < 0 ? -net : 0,
                    LineNarration = net < 0 ? "Profit transferred to retained earnings" : "Loss transferred to retained earnings"
                });

            var voucher = new Voucher
            {
                VoucherType = VoucherType.Journal,
                VoucherDate = fy.EndDate,
                Narration = $"Year-end closing entry for {fy.Name}",
                IsSystemGenerated = true,
                CreatedById = actor,
                CreatedAt = DateTime.Now,
                Details = lines
            };
            var result = await _accounting.CreateAndPostAsync(voucher, actor);
            if (!result.Ok) return result;
            closingId = voucher.Id;
        }

        fy.IsClosed = true;
        fy.ClosedAt = DateTime.Now;
        fy.ClosedBy = _user.UserName;
        fy.ClosingVoucherId = closingId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Close", "FiscalYear", fy.Name, $"Net result {Fmt.Signed(-net)} transferred to retained earnings");
        return OpResult.Success(closingId);
    }

    public async Task<OpResult> ReopenAsync(int fiscalYearId)
    {
        var fy = await _db.FiscalYears.FindAsync(fiscalYearId);
        if (fy == null) return OpResult.Fail("Fiscal year not found.");
        if (!fy.IsClosed) return OpResult.Fail("This fiscal year is already open.");

        var later = await _db.FiscalYears.AnyAsync(f => f.StartDate > fy.EndDate && f.IsClosed);
        if (later) return OpResult.Fail("Reopen the later closed fiscal year first.");

        fy.IsClosed = false;
        await _db.SaveChangesAsync();

        if (fy.ClosingVoucherId.HasValue)
        {
            var result = await _accounting.ReverseAsync(fy.ClosingVoucherId.Value, fy.EndDate, "Fiscal year reopened", systemGenerated: true);
            if (!result.Ok)
            {
                fy.IsClosed = true;
                await _db.SaveChangesAsync();
                return result;
            }
        }
        fy.ClosingVoucherId = null;
        fy.ClosedAt = null;
        fy.ClosedBy = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reopen", "FiscalYear", fy.Name, "Closing entry reversed");
        return OpResult.Success();
    }
}
