using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Services;

public class OpResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public List<string> Errors { get; init; } = new();
    public int? Id { get; init; }

    public static OpResult Success(int? id = null) => new() { Ok = true, Id = id };
    public static OpResult Fail(string error) => new() { Ok = false, Error = error, Errors = new() { error } };
    public static OpResult Fail(List<string> errors) => new() { Ok = false, Error = string.Join(" ", errors), Errors = errors };
}

public interface IAccountingService
{
    Task<string> NextVoucherNoAsync(VoucherType type, DateTime date);
    Task<List<string>> ValidateAsync(VoucherType type, DateTime date, IList<VoucherDetail> lines);
    Task<OpResult> CreateAsync(Voucher header, List<VoucherDetail> lines, bool submit);
    Task<OpResult> UpdateAsync(int id, Voucher header, List<VoucherDetail> lines, bool submit);
    Task<OpResult> SubmitAsync(int id);
    Task<OpResult> ApproveAsync(int id);
    Task<OpResult> RejectAsync(int id, string reason);
    Task<OpResult> DeleteDraftAsync(int id);
    Task<OpResult> ReverseAsync(int id, DateTime date, string reason, bool systemGenerated = false);
    Task<OpResult> CreateAndPostAsync(Voucher voucher, int approverId, bool validate = true);
    Task<int> RebuildLedgerAsync(int voucherId);
}

/// <summary>
/// The posting engine: validates double-entry rules, runs the maker–checker
/// workflow, writes immutable ledger rows and extends the voucher hash chain.
/// </summary>
public class AccountingService : IAccountingService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;

    public AccountingService(AppDbContext db, ICurrentUser user, ISettingsService settings, IAuditService audit)
    {
        _db = db;
        _user = user;
        _settings = settings;
        _audit = audit;
    }

    public async Task<string> NextVoucherNoAsync(VoucherType type, DateTime date)
    {
        var prefix = $"{Fmt.TypeCode(type)}-{date:yyyy}-";
        var numbers = await _db.Vouchers.Where(v => v.VoucherNo.StartsWith(prefix)).Select(v => v.VoucherNo).ToListAsync();
        var max = 0;
        foreach (var n in numbers)
            if (int.TryParse(n.Substring(prefix.Length), out var seq) && seq > max) max = seq;
        return prefix + (max + 1).ToString("D4");
    }

    public async Task<List<string>> ValidateAsync(VoucherType type, DateTime date, IList<VoucherDetail> lines)
    {
        var errors = new List<string>();
        if (lines.Count < 2)
            errors.Add("A voucher needs at least two lines: one debit and one credit.");

        var ids = lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);

        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var n = i + 1;
            if (!accounts.TryGetValue(l.AccountId, out var acc))
            {
                errors.Add($"Line {n}: choose an account.");
                continue;
            }
            if (acc.IsGroup) errors.Add($"Line {n}: {acc.Code} {acc.Name} is a group account. Post to one of its sub-accounts.");
            if (!acc.IsActive) errors.Add($"Line {n}: {acc.Code} {acc.Name} is inactive.");
            if (l.Debit < 0 || l.Credit < 0) errors.Add($"Line {n}: amounts cannot be negative.");
            if (l.Debit > 0 && l.Credit > 0) errors.Add($"Line {n}: enter either a debit or a credit, not both.");
            if (l.Debit == 0 && l.Credit == 0) errors.Add($"Line {n}: enter a debit or credit amount.");
        }

        var td = lines.Sum(l => l.Debit);
        var tc = lines.Sum(l => l.Credit);
        if (td != tc)
            errors.Add($"Total debit ({Fmt.Money(td)}) must equal total credit ({Fmt.Money(tc)}). Difference: {Fmt.Money(Math.Abs(td - tc))}.");
        if (td == 0 && lines.Count >= 2)
            errors.Add("The voucher total cannot be zero.");

        // Voucher-type business rules
        bool IsCash(VoucherDetail l) => accounts.TryGetValue(l.AccountId, out var a) && a.IsCashOrBank;
        switch (type)
        {
            case VoucherType.Payment when !lines.Any(l => IsCash(l) && l.Credit > 0):
                errors.Add("A payment voucher must credit a cash or bank account (money going out).");
                break;
            case VoucherType.Receipt when !lines.Any(l => IsCash(l) && l.Debit > 0):
                errors.Add("A receipt voucher must debit a cash or bank account (money coming in).");
                break;
            case VoucherType.Contra when lines.Any(l => accounts.ContainsKey(l.AccountId) && !IsCash(l)):
                errors.Add("A contra voucher moves money between cash and bank accounts only.");
                break;
            case VoucherType.Journal when lines.Any(IsCash):
                errors.Add("A journal voucher cannot touch cash or bank accounts. Use a payment, receipt or contra voucher.");
                break;
        }

        var fy = await _db.FiscalYears.FirstOrDefaultAsync(f => f.StartDate <= date.Date && f.EndDate >= date.Date);
        if (fy == null)
            errors.Add($"No fiscal year covers {Fmt.Date(date)}. Create one under Setup › Fiscal years.");
        else if (fy.IsClosed)
            errors.Add($"Fiscal year {fy.Name} is closed. Vouchers dated {Fmt.Date(date)} cannot be posted.");

        return errors;
    }

    private static void Normalise(Voucher v, List<VoucherDetail> lines)
    {
        int n = 1;
        foreach (var l in lines)
        {
            l.LineNo = n++;
            l.Debit = Math.Round(l.Debit, 2);
            l.Credit = Math.Round(l.Credit, 2);
        }
        v.TotalAmount = lines.Sum(l => l.Debit);
        v.VoucherDate = v.VoucherDate.Date;
    }

    public async Task<OpResult> CreateAsync(Voucher header, List<VoucherDetail> lines, bool submit)
    {
        Normalise(header, lines);
        var errors = await ValidateAsync(header.VoucherType, header.VoucherDate, lines);
        if (errors.Any()) return OpResult.Fail(errors);

        header.VoucherNo = await NextVoucherNoAsync(header.VoucherType, header.VoucherDate);
        header.CreatedById = _user.Id;
        header.CreatedAt = DateTime.Now;
        header.Status = VoucherStatus.Draft;
        header.Details = lines;
        _db.Vouchers.Add(header);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Voucher", header.VoucherNo, $"{header.VoucherType} {Fmt.Money(header.TotalAmount)}");

        if (submit) return await SubmitAsync(header.Id);
        return OpResult.Success(header.Id);
    }

    public async Task<OpResult> UpdateAsync(int id, Voucher header, List<VoucherDetail> lines, bool submit)
    {
        var v = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return OpResult.Fail("Voucher not found.");
        if (v.Status is not (VoucherStatus.Draft or VoucherStatus.Rejected))
            return OpResult.Fail("Only draft or rejected vouchers can be edited. Reverse a posted voucher instead.");

        Normalise(header, lines);
        var errors = await ValidateAsync(header.VoucherType, header.VoucherDate, lines);
        if (errors.Any()) return OpResult.Fail(errors);

        if (v.VoucherType != header.VoucherType || v.VoucherDate.Year != header.VoucherDate.Year)
            v.VoucherNo = await NextVoucherNoAsync(header.VoucherType, header.VoucherDate);

        v.VoucherType = header.VoucherType;
        v.VoucherDate = header.VoucherDate;
        v.Narration = header.Narration;
        v.Reference = header.Reference;
        v.TotalAmount = header.TotalAmount;
        v.Status = VoucherStatus.Draft;
        v.RejectionReason = null;
        _db.VoucherDetails.RemoveRange(v.Details);
        v.Details = lines;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Voucher", v.VoucherNo, $"Total {Fmt.Money(v.TotalAmount)}, {lines.Count} lines");

        if (submit) return await SubmitAsync(v.Id);
        return OpResult.Success(v.Id);
    }

    public async Task<OpResult> SubmitAsync(int id)
    {
        var v = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return OpResult.Fail("Voucher not found.");
        if (v.Status is not (VoucherStatus.Draft or VoucherStatus.Rejected))
            return OpResult.Fail("This voucher has already been submitted.");

        var settings = await _settings.GetAsync();
        if (!settings.RequireApproval)
        {
            var errors = await ValidateAsync(v.VoucherType, v.VoucherDate, v.Details);
            if (errors.Any()) return OpResult.Fail(errors);
            await using var tx = await _db.Database.BeginTransactionAsync();
            await PostCoreAsync(v, _user.Id);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            await _audit.LogAsync("Post", "Voucher", v.VoucherNo, $"Direct post (approval not required). Hash {Fmt.ShortHash(v.Hash)}");
            return OpResult.Success(v.Id);
        }

        v.Status = VoucherStatus.PendingApproval;
        v.SubmittedAt = DateTime.Now;
        v.RejectionReason = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Submit", "Voucher", v.VoucherNo, "Sent for approval");
        return OpResult.Success(v.Id);
    }

    public async Task<OpResult> ApproveAsync(int id)
    {
        var v = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return OpResult.Fail("Voucher not found.");
        if (v.Status != VoucherStatus.PendingApproval)
            return OpResult.Fail("Only vouchers awaiting approval can be approved.");

        var settings = await _settings.GetAsync();
        if (settings.EnforceSegregationOfDuties && v.CreatedById == _user.Id)
            return OpResult.Fail("You created this voucher, so another user must approve it (segregation of duties).");

        var errors = await ValidateAsync(v.VoucherType, v.VoucherDate, v.Details);
        if (errors.Any()) return OpResult.Fail(errors);

        await using var tx = await _db.Database.BeginTransactionAsync();
        await PostCoreAsync(v, _user.Id);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("Approve", "Voucher", v.VoucherNo, $"Posted to ledger. Chain #{v.ChainIndex}, hash {Fmt.ShortHash(v.Hash)}");
        return OpResult.Success(v.Id);
    }

    public async Task<OpResult> RejectAsync(int id, string reason)
    {
        var v = await _db.Vouchers.FindAsync(id);
        if (v == null) return OpResult.Fail("Voucher not found.");
        if (v.Status != VoucherStatus.PendingApproval) return OpResult.Fail("Only vouchers awaiting approval can be rejected.");
        v.Status = VoucherStatus.Rejected;
        v.RejectionReason = string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reject", "Voucher", v.VoucherNo, v.RejectionReason);
        return OpResult.Success(v.Id);
    }

    public async Task<OpResult> DeleteDraftAsync(int id)
    {
        var v = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return OpResult.Fail("Voucher not found.");
        if (v.Status is not (VoucherStatus.Draft or VoucherStatus.Rejected))
            return OpResult.Fail("Posted vouchers are permanent. Reverse it instead.");
        _db.Vouchers.Remove(v);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Voucher", v.VoucherNo, "Draft deleted");
        return OpResult.Success();
    }

    public async Task<OpResult> ReverseAsync(int id, DateTime date, string reason, bool systemGenerated = false)
    {
        var orig = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (orig == null) return OpResult.Fail("Voucher not found.");
        if (orig.Status != VoucherStatus.Posted) return OpResult.Fail("Only posted vouchers can be reversed.");
        if (orig.ReversedByVoucherId != null) return OpResult.Fail("This voucher has already been reversed.");

        var type = orig.VoucherType switch
        {
            VoucherType.Payment => VoucherType.Receipt,
            VoucherType.Receipt => VoucherType.Payment,
            _ => orig.VoucherType
        };
        var rev = new Voucher
        {
            VoucherType = type,
            VoucherDate = date.Date,
            Narration = $"Reversal of {orig.VoucherNo}: {reason}",
            Reference = orig.VoucherNo,
            ReversalOfId = orig.Id,
            IsSystemGenerated = systemGenerated || orig.IsSystemGenerated,
            CreatedById = _user.Id == 0 ? orig.CreatedById : _user.Id,
            CreatedAt = DateTime.Now,
            Details = orig.Details.OrderBy(d => d.LineNo).Select(d => new VoucherDetail
            {
                AccountId = d.AccountId,
                Debit = d.Credit,
                Credit = d.Debit,
                LineNarration = d.LineNarration,
                CostCenterId = d.CostCenterId,
                PartyId = d.PartyId
            }).ToList()
        };

        var result = await CreateAndPostAsync(rev, rev.CreatedById);
        if (!result.Ok) return result;

        orig.Status = VoucherStatus.Reversed;
        orig.ReversedByVoucherId = rev.Id;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reverse", "Voucher", orig.VoucherNo, $"Reversed by {rev.VoucherNo}: {reason}");
        return OpResult.Success(rev.Id);
    }

    public async Task<OpResult> CreateAndPostAsync(Voucher voucher, int approverId, bool validate = true)
    {
        var lines = voucher.Details;
        Normalise(voucher, lines);
        if (validate)
        {
            var errors = await ValidateAsync(voucher.VoucherType, voucher.VoucherDate, lines);
            if (errors.Any()) return OpResult.Fail(errors);
        }
        if (string.IsNullOrEmpty(voucher.VoucherNo))
            voucher.VoucherNo = await NextVoucherNoAsync(voucher.VoucherType, voucher.VoucherDate);
        if (voucher.CreatedById == 0) voucher.CreatedById = approverId;

        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync();
        await PostCoreAsync(voucher, approverId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return OpResult.Success(voucher.Id);
    }

    /// <summary>
    /// Writes ledger rows and links the voucher into the SHA-256 chain:
    /// hash(n) = SHA256(canonical content(n) | hash(n-1)).
    /// </summary>
    private async Task PostCoreAsync(Voucher v, int approverId)
    {
        var last = await _db.Vouchers
            .Where(x => x.ChainIndex != null)
            .OrderByDescending(x => x.ChainIndex)
            .Select(x => new { x.ChainIndex, x.Hash })
            .FirstOrDefaultAsync();

        var now = DateTime.Now;
        v.TotalAmount = v.Details.Sum(d => d.Debit);
        v.ChainIndex = (last?.ChainIndex ?? 0) + 1;
        v.PreviousHash = last?.Hash ?? HashUtil.Genesis;
        v.Hash = HashUtil.VoucherHash(v, v.Details, v.PreviousHash);
        v.Status = VoucherStatus.Posted;
        v.ApprovedById = approverId == 0 ? v.CreatedById : approverId;
        v.ApprovedAt ??= now;
        v.PostedAt = now;
        v.SubmittedAt ??= now;

        foreach (var d in v.Details)
        {
            _db.LedgerEntries.Add(new LedgerEntry
            {
                Voucher = v,
                VoucherDetail = d,
                AccountId = d.AccountId,
                EntryDate = v.VoucherDate,
                Debit = d.Debit,
                Credit = d.Credit,
                Narration = d.LineNarration ?? v.Narration,
                CostCenterId = d.CostCenterId,
                PartyId = d.PartyId,
                PostedAt = now
            });
        }
    }

    public async Task<int> RebuildLedgerAsync(int voucherId)
    {
        var v = await _db.Vouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == voucherId);
        if (v == null || v.ChainIndex == null) return 0;
        var existing = await _db.LedgerEntries.Where(l => l.VoucherId == voucherId).ToListAsync();
        var reconciled = existing.Where(e => e.IsReconciled).Select(e => e.VoucherDetailId).ToHashSet();
        _db.LedgerEntries.RemoveRange(existing);
        foreach (var d in v.Details)
        {
            _db.LedgerEntries.Add(new LedgerEntry
            {
                VoucherId = v.Id,
                VoucherDetailId = d.Id,
                AccountId = d.AccountId,
                EntryDate = v.VoucherDate,
                Debit = d.Debit,
                Credit = d.Credit,
                Narration = d.LineNarration ?? v.Narration,
                CostCenterId = d.CostCenterId,
                PartyId = d.PartyId,
                PostedAt = v.PostedAt ?? DateTime.Now,
                IsReconciled = reconciled.Contains(d.Id)
            });
        }
        await _db.SaveChangesAsync();
        return v.Details.Count;
    }
}
