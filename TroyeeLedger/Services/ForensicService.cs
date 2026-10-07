using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Services;

public interface IForensicService
{
    Task<BenfordVm> BenfordAsync(DateTime from, DateTime to);
    Task<AnomalyReportVm> AnomaliesAsync(DateTime from, DateTime to, string minLevel = "Low");
    Task<AnomalyItem?> ScoreVoucherAsync(int voucherId);
    Task<IntegrityReportVm> VerifyIntegrityAsync();
    Task<SodVm> SegregationAsync();
}

/// <summary>
/// Continuous-audit toolkit: Benford's-law conformity, rule-based and statistical
/// anomaly scoring with human-readable explanations, cryptographic chain
/// verification and segregation-of-duties analysis.
/// </summary>
public class ForensicService : IForensicService
{
    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;

    public ForensicService(AppDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ---------------------------------------------------------------- Benford
    private static int FirstDigit(decimal v)
    {
        v = Math.Abs(v);
        while (v >= 10) v /= 10;
        while (v > 0 && v < 1) v *= 10;
        return (int)Math.Floor(v);
    }

    public async Task<BenfordVm> BenfordAsync(DateTime from, DateTime to)
    {
        var f = from.Date; var t = to.Date;
        var amounts = await _db.LedgerEntries
            .Where(l => l.EntryDate >= f && l.EntryDate <= t && !l.Voucher!.IsSystemGenerated && l.Voucher.ReversalOfId == null)
            .Select(l => l.Debit > 0 ? l.Debit : l.Credit)
            .Where(a => a >= 10)
            .ToListAsync();

        var counts = new int[10];
        foreach (var a in amounts) counts[FirstDigit(a)]++;
        int n = amounts.Count;

        var vm = new BenfordVm { From = f, To = t, N = n };
        double chi = 0, madSum = 0;
        for (int d = 1; d <= 9; d++)
        {
            double expected = Math.Log10(1 + 1.0 / d);
            double observed = n == 0 ? 0 : counts[d] / (double)n;
            double z = 0;
            if (n > 0)
            {
                var se = Math.Sqrt(expected * (1 - expected) / n);
                z = (Math.Abs(observed - expected) - 1.0 / (2 * n)) / se;
                if (z < 0) z = 0;
                var expCount = expected * n;
                chi += Math.Pow(counts[d] - expCount, 2) / expCount;
            }
            madSum += Math.Abs(observed - expected);
            vm.Digits.Add(new BenfordDigit { Digit = d, Count = counts[d], Observed = observed, Expected = expected, Z = z });
        }
        vm.ChiSquare = chi;
        vm.Mad = madSum / 9.0;
        // Nigrini (2012) first-digit MAD conformity bands
        vm.Conformity = n == 0 ? "No data" :
            vm.Mad <= 0.006 ? "Close conformity" :
            vm.Mad <= 0.012 ? "Acceptable conformity" :
            vm.Mad <= 0.015 ? "Marginally acceptable" : "Nonconformity";
        return vm;
    }

    // -------------------------------------------------------------- Anomalies
    private record AccountStat(double Mean, double Std, int N);

    private async Task<Dictionary<int, AccountStat>> AccountStatsAsync()
    {
        var rows = await _db.LedgerEntries
            .Where(l => !l.Account!.IsCashOrBank && !l.Voucher!.IsSystemGenerated && l.Voucher.ReversalOfId == null)
            .Select(l => new { l.AccountId, Amount = l.Debit > 0 ? l.Debit : l.Credit })
            .ToListAsync();
        return rows.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g =>
        {
            var xs = g.Select(x => (double)x.Amount).ToList();
            var mean = xs.Average();
            var std = xs.Count > 1 ? Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1)) : 0;
            return new AccountStat(mean, std, xs.Count);
        });
    }

    private static string AccountKey(Voucher v) =>
        string.Join("-", v.Details.Select(d => $"{d.AccountId}{(d.Debit > 0 ? "D" : "C")}").OrderBy(s => s));

    private List<AnomalyItem> Score(List<Voucher> vouchers, List<Voucher> neighbourhood,
        Dictionary<int, AccountStat> stats, CompanySetting s)
    {
        var weekend = s.WeekendDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.TryParse<DayOfWeek>(d, true, out var day) ? day : (DayOfWeek?)null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToHashSet();
        var dupIndex = neighbourhood.GroupBy(v => (v.TotalAmount, AccountKey(v))).ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<AnomalyItem>();
        foreach (var v in vouchers)
        {
            var item = new AnomalyItem
            {
                VoucherId = v.Id, VoucherNo = v.VoucherNo, Date = v.VoucherDate, CreatedAt = v.CreatedAt,
                Type = v.VoucherType, Amount = v.TotalAmount, Narration = v.Narration,
                CreatedBy = v.CreatedBy?.FullName ?? "—"
            };

            // 1. Statistical outlier (z-score against the account's own history)
            double bestZ = 0; string? zText = null;
            foreach (var d in v.Details.Where(d => d.Account != null && !d.Account.IsCashOrBank))
            {
                if (!stats.TryGetValue(d.AccountId, out var st) || st.N < 8 || st.Std <= 0) continue;
                var amt = (double)(d.Debit > 0 ? d.Debit : d.Credit);
                var z = (amt - st.Mean) / st.Std;
                if (z > bestZ)
                {
                    bestZ = z;
                    zText = $"{d.Account!.Name}: {Fmt.Money((decimal)amt)} is {z:F1}σ above its mean of {Fmt.Money((decimal)st.Mean)} (n={st.N}).";
                }
            }
            if (bestZ >= s.AnomalyZThreshold)
                item.Reasons.Add(new AnomalyReason { Rule = "Statistical outlier", Detail = zText!, Points = 35 });

            // 2. Threshold avoidance (just under the large-amount limit)
            if (s.LargeAmountThreshold > 0 && v.TotalAmount >= s.LargeAmountThreshold * 0.95m && v.TotalAmount < s.LargeAmountThreshold)
                item.Reasons.Add(new AnomalyReason { Rule = "Just below threshold", Detail = $"Amount sits within 5% under the {Fmt.Money(s.LargeAmountThreshold)} review limit.", Points = 20 });

            // 3. Large transaction
            if (v.TotalAmount >= s.LargeAmountThreshold)
                item.Reasons.Add(new AnomalyReason { Rule = "Large transaction", Detail = $"Total {Fmt.Money(v.TotalAmount)} meets the review threshold of {Fmt.Money(s.LargeAmountThreshold)}.", Points = 15 });

            // 4. Possible duplicate
            if (dupIndex.TryGetValue((v.TotalAmount, AccountKey(v)), out var twins))
            {
                var twin = twins.FirstOrDefault(o => o.Id != v.Id
                    && Math.Abs((o.VoucherDate - v.VoucherDate).TotalDays) <= 7
                    && o.ReversalOfId != v.Id && v.ReversalOfId != o.Id);
                if (twin != null)
                    item.Reasons.Add(new AnomalyReason { Rule = "Possible duplicate", Detail = $"Same amount and accounts as {twin.VoucherNo} ({Fmt.Date(twin.VoucherDate)}).", Points = 25 });
            }

            // 5. Segregation of duties
            if (v.ApprovedById != null && v.ApprovedById == v.CreatedById && v.ReversalOfId == null)
                item.Reasons.Add(new AnomalyReason { Rule = "Self-approved", Detail = "The same user created and approved this voucher.", Points = 15 });

            // 6. Backdated entry
            var lag = (v.CreatedAt.Date - v.VoucherDate.Date).Days;
            if (lag > s.BackdateToleranceDays)
                item.Reasons.Add(new AnomalyReason { Rule = "Backdated", Detail = $"Recorded {lag} days after its voucher date.", Points = 15 });

            // 7. Round amount
            if (v.TotalAmount >= 10000 && v.TotalAmount % 1000 == 0)
                item.Reasons.Add(new AnomalyReason { Rule = "Round amount", Detail = $"{Fmt.Money(v.TotalAmount)} is an exact multiple of 1,000.", Points = 10 });

            // 8. Weekend entry
            if (weekend.Contains(v.CreatedAt.DayOfWeek))
                item.Reasons.Add(new AnomalyReason { Rule = "Weekend entry", Detail = $"Entered on a {v.CreatedAt.DayOfWeek}.", Points = 10 });

            // 9. Out-of-hours entry
            if (v.CreatedAt.Hour < 8 || v.CreatedAt.Hour >= 21)
                item.Reasons.Add(new AnomalyReason { Rule = "Out of hours", Detail = $"Entered at {v.CreatedAt:HH:mm}.", Points = 10 });

            // 10. Missing narration
            if (string.IsNullOrWhiteSpace(v.Narration))
                item.Reasons.Add(new AnomalyReason { Rule = "No narration", Detail = "The voucher has no description of its purpose.", Points = 5 });

            items.Add(item);
        }
        return items;
    }

    private IQueryable<Voucher> PostedWithDetails() => _db.Vouchers.AsNoTracking()
        .Include(v => v.Details).ThenInclude(d => d.Account)
        .Include(v => v.CreatedBy)
        .Where(v => (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed) && !v.IsSystemGenerated);

    public async Task<AnomalyReportVm> AnomaliesAsync(DateTime from, DateTime to, string minLevel = "Low")
    {
        var s = await _settings.GetAsync();
        var f = from.Date; var t = to.Date;
        var vouchers = await PostedWithDetails().Where(v => v.VoucherDate >= f && v.VoucherDate <= t).ToListAsync();
        var neighbourhood = await PostedWithDetails().Where(v => v.VoucherDate >= f.AddDays(-7) && v.VoucherDate <= t.AddDays(7)).ToListAsync();
        var stats = await AccountStatsAsync();

        int minScore = minLevel switch { "High" => 50, "Medium" => 25, _ => 1 };
        var items = Score(vouchers, neighbourhood, stats, s)
            .Where(i => i.Score >= minScore)
            .OrderByDescending(i => i.Score).ThenByDescending(i => i.Amount)
            .ToList();
        return new AnomalyReportVm { From = f, To = t, MinLevel = minLevel, Scanned = vouchers.Count, Items = items };
    }

    public async Task<AnomalyItem?> ScoreVoucherAsync(int voucherId)
    {
        // Works for pending vouchers too, so approvers see the risk before they post.
        var v = await _db.Vouchers.AsNoTracking()
            .Include(x => x.Details).ThenInclude(d => d.Account)
            .Include(x => x.CreatedBy)
            .FirstOrDefaultAsync(x => x.Id == voucherId);
        if (v == null || v.IsSystemGenerated) return null;
        var s = await _settings.GetAsync();
        var neighbourhood = await PostedWithDetails()
            .Where(x => x.VoucherDate >= v.VoucherDate.AddDays(-7) && x.VoucherDate <= v.VoucherDate.AddDays(7)).ToListAsync();
        neighbourhood.RemoveAll(x => x.Id == v.Id);
        neighbourhood.Add(v);
        return Score(new List<Voucher> { v }, neighbourhood, await AccountStatsAsync(), s).First();
    }

    // -------------------------------------------------------------- Integrity
    public async Task<IntegrityReportVm> VerifyIntegrityAsync()
    {
        var vm = new IntegrityReportVm();

        // A. Voucher hash chain
        var chained = await _db.Vouchers.AsNoTracking().Include(v => v.Details)
            .Where(v => v.ChainIndex != null).OrderBy(v => v.ChainIndex).ToListAsync();
        var expectedPrev = HashUtil.Genesis;
        long expectedIndex = 1;
        vm.VoucherChainOk = true;
        foreach (var v in chained)
        {
            if (v.ChainIndex != expectedIndex)
            {
                vm.Issues.Add(new IntegrityIssue { Area = "Voucher chain", Reference = v.VoucherNo, VoucherId = v.Id, Message = $"Chain position {v.ChainIndex} found where {expectedIndex} was expected — a posted voucher may have been deleted." });
                vm.VoucherChainOk = false;
            }
            if (v.PreviousHash != expectedPrev)
            {
                vm.Issues.Add(new IntegrityIssue { Area = "Voucher chain", Reference = v.VoucherNo, VoucherId = v.Id, Message = "The link to the previous voucher is broken." });
                vm.VoucherChainOk = false;
            }
            var recomputed = HashUtil.VoucherHash(v, v.Details, v.PreviousHash ?? "");
            if (recomputed != v.Hash)
            {
                vm.Issues.Add(new IntegrityIssue { Area = "Voucher content", Reference = v.VoucherNo, VoucherId = v.Id, Message = "Date, type, accounts or amounts were changed after posting (fingerprint mismatch)." });
                vm.VoucherChainOk = false;
            }
            expectedPrev = v.Hash ?? "";
            expectedIndex = (v.ChainIndex ?? expectedIndex) + 1;
        }
        vm.VouchersChecked = chained.Count;
        vm.ChainHead = chained.LastOrDefault()?.Hash;

        var unchained = await _db.Vouchers.AsNoTracking()
            .Where(v => (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed) && v.ChainIndex == null)
            .Select(v => new { v.Id, v.VoucherNo }).ToListAsync();
        foreach (var u in unchained)
        {
            vm.Issues.Add(new IntegrityIssue { Area = "Voucher chain", Reference = u.VoucherNo, VoucherId = u.Id, Message = "Marked as posted but never sealed into the chain." });
            vm.VoucherChainOk = false;
        }

        // B. Ledger agrees with sealed vouchers
        var ledger = await _db.LedgerEntries.AsNoTracking()
            .GroupBy(l => l.VoucherId)
            .Select(g => new { VoucherId = g.Key, N = g.Count(), D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();
        var ledgerMap = ledger.ToDictionary(x => x.VoucherId);
        vm.LedgerMatchesVouchers = true;
        foreach (var v in chained)
        {
            var dD = v.Details.Sum(d => d.Debit);
            var dC = v.Details.Sum(d => d.Credit);
            if (!ledgerMap.TryGetValue(v.Id, out var l) || l.N != v.Details.Count || l.D != dD || l.C != dC)
            {
                vm.Issues.Add(new IntegrityIssue { Area = "Ledger", Reference = v.VoucherNo, VoucherId = v.Id, Message = "Ledger postings no longer match the sealed voucher lines." });
                vm.LedgerMatchesVouchers = false;
            }
        }
        var chainedIds = chained.Select(v => v.Id).ToHashSet();
        foreach (var orphan in ledger.Where(x => !chainedIds.Contains(x.VoucherId)))
        {
            vm.Issues.Add(new IntegrityIssue { Area = "Ledger", Reference = $"Voucher #{orphan.VoucherId}", VoucherId = orphan.VoucherId, Message = "Ledger rows exist for a voucher that was never posted." });
            vm.LedgerMatchesVouchers = false;
        }
        vm.LedgerRowsChecked = ledger.Sum(x => x.N);
        vm.LedgerDebit = ledger.Sum(x => x.D);
        vm.LedgerCredit = ledger.Sum(x => x.C);
        vm.LedgerBalanced = vm.LedgerDebit == vm.LedgerCredit;
        if (!vm.LedgerBalanced)
            vm.Issues.Add(new IntegrityIssue { Area = "Ledger", Reference = "Whole ledger", Message = $"Total debits ({Fmt.Money(vm.LedgerDebit)}) differ from total credits ({Fmt.Money(vm.LedgerCredit)})." });

        // C. Audit trail chain
        var audits = await _db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        var prev = HashUtil.Genesis;
        vm.AuditChainOk = true;
        foreach (var a in audits)
        {
            if (a.PreviousHash != prev || HashUtil.AuditHash(a, a.PreviousHash) != a.Hash)
            {
                vm.Issues.Add(new IntegrityIssue { Area = "Audit trail", Reference = $"Record #{a.Id}", Message = $"Audit record ({a.Action} {a.EntityName}) was altered or a record before it was removed." });
                vm.AuditChainOk = false;
            }
            prev = a.Hash;
        }
        vm.AuditRecordsChecked = audits.Count;
        vm.AuditHead = audits.LastOrDefault()?.Hash;
        return vm;
    }

    // ---------------------------------------------------- Segregation of duty
    public async Task<SodVm> SegregationAsync()
    {
        var users = await _db.Users.AsNoTracking().ToListAsync();
        var vouchers = await _db.Vouchers.AsNoTracking()
            .Where(v => (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed) && !v.IsSystemGenerated && v.ReversalOfId == null)
            .Select(v => new { v.Id, v.CreatedById, v.ApprovedById })
            .ToListAsync();

        var violations = await _db.Vouchers.AsNoTracking().Include(v => v.CreatedBy)
            .Where(v => (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed) && !v.IsSystemGenerated
                        && v.ReversalOfId == null && v.ApprovedById == v.CreatedById)
            .OrderByDescending(v => v.VoucherDate).ToListAsync();

        return new SodVm
        {
            Violations = violations,
            Users = users.Select(u => new SodUserStat
            {
                UserName = u.Username, FullName = u.FullName, Role = u.Role,
                Created = vouchers.Count(v => v.CreatedById == u.Id),
                Approved = vouchers.Count(v => v.ApprovedById == u.Id),
                SelfApproved = vouchers.Count(v => v.CreatedById == u.Id && v.ApprovedById == u.Id)
            }).OrderByDescending(x => x.Created + x.Approved).ToList()
        };
    }
}
