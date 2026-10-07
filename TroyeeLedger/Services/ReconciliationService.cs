using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Services;

public interface IReconciliationService
{
    Task<ReconcileVm> BuildAsync(int bankAccountId, DateTime asOf);
    Task<(int Imported, List<string> Errors)> ImportAsync(int bankAccountId, string csv);
    Task<int> AutoMatchAsync(int bankAccountId, int dayWindow = 3);
    Task<OpResult> MatchAsync(int statementLineId, long ledgerEntryId);
    Task<OpResult> UnmatchAsync(int statementLineId);
}

public class ReconciliationService : IReconciliationService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public ReconciliationService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ReconcileVm> BuildAsync(int bankAccountId, DateTime asOf)
    {
        var t = asOf.Date;
        var account = await _db.ChartOfAccounts.FirstAsync(a => a.Id == bankAccountId);
        return new ReconcileVm
        {
            Account = account,
            AsOf = t,
            BankAccounts = await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup).OrderBy(a => a.Code).ToListAsync(),
            StatementLines = await _db.BankStatementLines.Where(s => s.BankAccountId == bankAccountId && s.TxnDate <= t)
                .OrderBy(s => s.IsMatched).ThenBy(s => s.TxnDate).ToListAsync(),
            BookEntries = await _db.LedgerEntries.Include(l => l.Voucher)
                .Where(l => l.AccountId == bankAccountId && l.EntryDate <= t)
                .OrderBy(l => l.IsReconciled).ThenByDescending(l => l.EntryDate).Take(400).ToListAsync(),
            BookBalance = await _db.LedgerEntries.Where(l => l.AccountId == bankAccountId && l.EntryDate <= t).SumAsync(l => l.Debit - l.Credit)
        };
    }

    /// <summary>CSV lines: date,description,reference,amount (amount positive for deposits).</summary>
    public async Task<(int Imported, List<string> Errors)> ImportAsync(int bankAccountId, string csv)
    {
        var errors = new List<string>();
        int imported = 0, lineNo = 0;
        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "MM/dd/yyyy" };
        foreach (var raw in csv.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("date", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(',');
            if (parts.Length < 4) { errors.Add($"Line {lineNo}: expected 4 values (date, description, reference, amount)."); continue; }
            if (!DateTime.TryParseExact(parts[0].Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { errors.Add($"Line {lineNo}: '{parts[0]}' is not a date. Use yyyy-MM-dd."); continue; }
            var amountText = parts[^1].Trim().Replace("\"", "");
            if (!decimal.TryParse(amountText, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
            { errors.Add($"Line {lineNo}: '{amountText}' is not an amount."); continue; }
            var description = string.Join(",", parts.Skip(1).Take(parts.Length - 3)).Trim();
            _db.BankStatementLines.Add(new BankStatementLine
            {
                BankAccountId = bankAccountId,
                TxnDate = date,
                Description = string.IsNullOrWhiteSpace(description) ? "(no description)" : description,
                Reference = parts[^2].Trim(),
                Amount = amount
            });
            imported++;
        }
        await _db.SaveChangesAsync();
        if (imported > 0) await _audit.LogAsync("Import", "BankStatement", bankAccountId.ToString(), $"{imported} line(s)");
        return (imported, errors);
    }

    public async Task<int> AutoMatchAsync(int bankAccountId, int dayWindow = 3)
    {
        var lines = await _db.BankStatementLines.Where(s => s.BankAccountId == bankAccountId && !s.IsMatched).ToListAsync();
        var entries = await _db.LedgerEntries.Where(l => l.AccountId == bankAccountId && !l.IsReconciled).ToListAsync();
        int matched = 0;
        foreach (var s in lines.OrderBy(s => s.TxnDate))
        {
            var candidate = entries
                .Where(e => !e.IsReconciled && e.Debit - e.Credit == s.Amount && Math.Abs((e.EntryDate - s.TxnDate).TotalDays) <= dayWindow)
                .OrderBy(e => Math.Abs((e.EntryDate - s.TxnDate).TotalDays))
                .FirstOrDefault();
            if (candidate == null) continue;
            candidate.IsReconciled = true;
            candidate.ReconciledAt = DateTime.Now;
            s.IsMatched = true;
            s.MatchedLedgerEntryId = candidate.Id;
            matched++;
        }
        await _db.SaveChangesAsync();
        if (matched > 0) await _audit.LogAsync("AutoMatch", "BankStatement", bankAccountId.ToString(), $"{matched} match(es)");
        return matched;
    }

    public async Task<OpResult> MatchAsync(int statementLineId, long ledgerEntryId)
    {
        var s = await _db.BankStatementLines.FindAsync(statementLineId);
        var e = await _db.LedgerEntries.FindAsync(ledgerEntryId);
        if (s == null || e == null) return OpResult.Fail("Statement line or ledger entry not found.");
        if (s.IsMatched || e.IsReconciled) return OpResult.Fail("One of these items is already matched.");
        if (e.AccountId != s.BankAccountId) return OpResult.Fail("The ledger entry belongs to a different account.");
        if (e.Debit - e.Credit != s.Amount) return OpResult.Fail("Amounts differ. Only identical amounts can be matched.");
        s.IsMatched = true; s.MatchedLedgerEntryId = e.Id;
        e.IsReconciled = true; e.ReconciledAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return OpResult.Success();
    }

    public async Task<OpResult> UnmatchAsync(int statementLineId)
    {
        var s = await _db.BankStatementLines.FindAsync(statementLineId);
        if (s == null) return OpResult.Fail("Statement line not found.");
        if (s.MatchedLedgerEntryId.HasValue)
        {
            var e = await _db.LedgerEntries.FindAsync(s.MatchedLedgerEntryId.Value);
            if (e != null) { e.IsReconciled = false; e.ReconciledAt = null; }
        }
        s.IsMatched = false; s.MatchedLedgerEntryId = null;
        await _db.SaveChangesAsync();
        return OpResult.Success();
    }
}
