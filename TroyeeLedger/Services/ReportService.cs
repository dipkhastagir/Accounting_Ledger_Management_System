using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Services;

public interface IReportService
{
    Task<Dictionary<int, (decimal D, decimal C)>> SumsAsync(DateTime? from, DateTime? to, bool excludeClosing = false);
    Task<List<AccountBalanceRow>> BalanceTreeAsync(DateTime? from, DateTime? to, Func<ChartOfAccount, bool> rootFilter, bool hideZero, bool excludeClosing = false);
    Task<TrialBalanceVm> TrialBalanceAsync(DateTime asOf, bool hideZero, bool showGroups);
    Task<ProfitLossVm> ProfitLossAsync(DateTime from, DateTime to, bool hideZero = true);
    Task<BalanceSheetVm> BalanceSheetAsync(DateTime asOf, bool hideZero = true);
    Task<CashFlowVm> CashFlowAsync(DateTime from, DateTime to);
    Task<AccountStatementVm> AccountStatementAsync(int accountId, DateTime from, DateTime to);
    Task<AccountStatementVm> CashBookAsync(DateTime from, DateTime to);
    Task<AccountStatementVm> PartyStatementAsync(int partyId, DateTime from, DateTime to);
    Task<DayBookVm> DayBookAsync(DateTime from, DateTime to, VoucherType? type);
    Task<VatReportVm> VatAsync(DateTime from, DateTime to);
    Task<CostCenterReportVm> CostCentersAsync(DateTime from, DateTime to);
    Task<PartyBalancesVm> PartyBalancesAsync(DateTime asOf);
    Task<List<MonthlyPoint>> MonthlyAsync(DateTime firstMonth, int months);
    Task<decimal> CashBalanceAsync(DateTime asOf);
    Task<FiscalYear?> FiscalYearForAsync(DateTime date);
}

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    public ReportService(AppDbContext db) => _db = db;

    public async Task<FiscalYear?> FiscalYearForAsync(DateTime date) =>
        await _db.FiscalYears.FirstOrDefaultAsync(f => f.StartDate <= date.Date && f.EndDate >= date.Date);

    private IQueryable<LedgerEntry> Range(DateTime? from, DateTime? to, bool excludeClosing)
    {
        var q = _db.LedgerEntries.AsQueryable();
        if (from.HasValue) { var f = from.Value.Date; q = q.Where(l => l.EntryDate >= f); }
        if (to.HasValue) { var t = to.Value.Date; q = q.Where(l => l.EntryDate <= t); }
        if (excludeClosing) q = q.Where(l => !l.Voucher!.IsSystemGenerated);
        return q;
    }

    public async Task<Dictionary<int, (decimal D, decimal C)>> SumsAsync(DateTime? from, DateTime? to, bool excludeClosing = false)
    {
        var list = await Range(from, to, excludeClosing)
            .GroupBy(l => l.AccountId)
            .Select(g => new { Id = g.Key, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();
        return list.ToDictionary(x => x.Id, x => (x.D, x.C));
    }

    private static List<AccountBalanceRow> BuildTree(List<ChartOfAccount> accounts,
        Dictionary<int, (decimal D, decimal C)> sums, Func<ChartOfAccount, bool> rootFilter, bool hideZero)
    {
        var byParent = accounts.ToLookup(a => a.ParentId);
        var output = new List<AccountBalanceRow>();

        (decimal D, decimal C) Visit(ChartOfAccount a, int level)
        {
            var row = new AccountBalanceRow
            {
                AccountId = a.Id, ParentId = a.ParentId, Code = a.Code, Name = a.Name, Type = a.AccountType,
                IsGroup = a.IsGroup, IsCashOrBank = a.IsCashOrBank, IsCurrent = a.IsCurrent, Level = level
            };
            output.Add(row);
            decimal d = 0, c = 0;
            if (sums.TryGetValue(a.Id, out var s)) { d += s.D; c += s.C; }
            foreach (var child in byParent[a.Id].OrderBy(x => x.Code))
            {
                var r = Visit(child, level + 1);
                d += r.D; c += r.C;
            }
            row.Debit = d;
            row.Credit = c;
            return (d, c);
        }

        foreach (var root in byParent[null].Where(rootFilter).OrderBy(a => a.Code))
            Visit(root, 0);

        if (hideZero)
            output = output.Where(r => r.Debit != 0 || r.Credit != 0).ToList();
        return output;
    }

    public async Task<List<AccountBalanceRow>> BalanceTreeAsync(DateTime? from, DateTime? to, Func<ChartOfAccount, bool> rootFilter, bool hideZero, bool excludeClosing = false)
    {
        var accounts = await _db.ChartOfAccounts.AsNoTracking().ToListAsync();
        var sums = await SumsAsync(from, to, excludeClosing);
        return BuildTree(accounts, sums, rootFilter, hideZero);
    }

    public async Task<TrialBalanceVm> TrialBalanceAsync(DateTime asOf, bool hideZero, bool showGroups)
    {
        var rows = await BalanceTreeAsync(null, asOf, _ => true, hideZero);
        var leaves = rows.Where(r => !r.IsGroup).ToList();
        return new TrialBalanceVm
        {
            AsOf = asOf,
            HideZero = hideZero,
            ShowGroups = showGroups,
            Rows = showGroups ? rows : leaves.Select(r => { r.Level = 0; return r; }).ToList(),
            TotalDebit = leaves.Sum(r => r.NetDebit),
            TotalCredit = leaves.Sum(r => r.NetCredit)
        };
    }

    public async Task<ProfitLossVm> ProfitLossAsync(DateTime from, DateTime to, bool hideZero = true)
    {
        var days = (to.Date - from.Date).Days + 1;
        var prevTo = from.Date.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(days - 1));

        var accounts = await _db.ChartOfAccounts.AsNoTracking().ToListAsync();
        var cur = await SumsAsync(from, to, excludeClosing: true);
        var prev = await SumsAsync(prevFrom, prevTo, excludeClosing: true);

        List<PlRow> Section(AccountType type)
        {
            var curRows = BuildTree(accounts, cur, a => a.AccountType == type, false);
            var prevRows = BuildTree(accounts, prev, a => a.AccountType == type, false).ToDictionary(r => r.AccountId);
            var rows = curRows.Select(r => new PlRow
            {
                AccountId = r.AccountId, Code = r.Code, Name = r.Name, Level = r.Level, IsGroup = r.IsGroup,
                Current = r.Natural,
                Previous = prevRows.TryGetValue(r.AccountId, out var p) ? p.Natural : 0
            }).ToList();
            return hideZero ? rows.Where(r => r.Current != 0 || r.Previous != 0).ToList() : rows;
        }

        var vm = new ProfitLossVm
        {
            From = from.Date, To = to.Date, PrevFrom = prevFrom, PrevTo = prevTo,
            Income = Section(AccountType.Income),
            Expense = Section(AccountType.Expense)
        };
        vm.TotalIncome = vm.Income.Where(r => r.Level == 0).Sum(r => r.Current);
        vm.TotalExpense = vm.Expense.Where(r => r.Level == 0).Sum(r => r.Current);
        vm.PrevTotalIncome = vm.Income.Where(r => r.Level == 0).Sum(r => r.Previous);
        vm.PrevTotalExpense = vm.Expense.Where(r => r.Level == 0).Sum(r => r.Previous);
        return vm;
    }

    public async Task<BalanceSheetVm> BalanceSheetAsync(DateTime asOf, bool hideZero = true)
    {
        var accounts = await _db.ChartOfAccounts.AsNoTracking().ToListAsync();
        var sums = await SumsAsync(null, asOf);
        var vm = new BalanceSheetVm
        {
            AsOf = asOf.Date,
            Assets = BuildTree(accounts, sums, a => a.AccountType == AccountType.Asset, hideZero),
            Liabilities = BuildTree(accounts, sums, a => a.AccountType == AccountType.Liability, hideZero),
            Equity = BuildTree(accounts, sums, a => a.AccountType == AccountType.Equity, hideZero)
        };
        vm.TotalAssets = vm.Assets.Where(r => r.Level == 0).Sum(r => r.Natural);
        vm.TotalLiabilities = vm.Liabilities.Where(r => r.Level == 0).Sum(r => r.Natural);
        vm.TotalEquity = vm.Equity.Where(r => r.Level == 0).Sum(r => r.Natural);

        decimal income = 0, expense = 0;
        foreach (var a in accounts.Where(a => !a.IsGroup))
        {
            if (!sums.TryGetValue(a.Id, out var s)) continue;
            if (a.AccountType == AccountType.Income) income += s.C - s.D;
            else if (a.AccountType == AccountType.Expense) expense += s.D - s.C;
        }
        vm.CurrentEarnings = income - expense;
        return vm;
    }

    private static CashFlowCategory Classify(ChartOfAccount a)
    {
        if (a.CashFlowCategory != CashFlowCategory.Auto) return a.CashFlowCategory;
        return a.AccountType switch
        {
            AccountType.Income or AccountType.Expense => CashFlowCategory.Operating,
            AccountType.Asset => a.IsCurrent ? CashFlowCategory.Operating : CashFlowCategory.Investing,
            AccountType.Liability => a.IsCurrent ? CashFlowCategory.Operating : CashFlowCategory.Financing,
            _ => CashFlowCategory.Financing
        };
    }

    public async Task<CashFlowVm> CashFlowAsync(DateTime from, DateTime to)
    {
        var f = from.Date; var t = to.Date;
        var accounts = await _db.ChartOfAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var cashIds = accounts.Values.Where(a => a.IsCashOrBank && !a.IsGroup).Select(a => a.Id).ToList();

        var voucherIds = await _db.LedgerEntries
            .Where(l => cashIds.Contains(l.AccountId) && l.EntryDate >= f && l.EntryDate <= t)
            .Select(l => l.VoucherId).Distinct().ToListAsync();

        var counter = await _db.LedgerEntries
            .Where(l => voucherIds.Contains(l.VoucherId) && !cashIds.Contains(l.AccountId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Amount = g.Sum(x => x.Credit - x.Debit) })
            .ToListAsync();

        var vm = new CashFlowVm { From = f, To = t };
        foreach (var c in counter.Where(c => c.Amount != 0))
        {
            var a = accounts[c.AccountId];
            vm.Lines.Add(new CashFlowLine { Category = Classify(a), AccountCode = a.Code, AccountName = a.Name, Amount = c.Amount });
        }
        vm.Lines = vm.Lines.OrderBy(l => l.Category).ThenByDescending(l => Math.Abs(l.Amount)).ToList();
        vm.OpeningCash = await _db.LedgerEntries.Where(l => cashIds.Contains(l.AccountId) && l.EntryDate < f).SumAsync(l => l.Debit - l.Credit);
        vm.ClosingCash = await _db.LedgerEntries.Where(l => cashIds.Contains(l.AccountId) && l.EntryDate <= t).SumAsync(l => l.Debit - l.Credit);
        return vm;
    }

    private async Task<List<StatementLine>> LinesAsync(Expression<Func<LedgerEntry, bool>> filter, DateTime from, DateTime to, decimal opening, bool withAccount)
    {
        var f = from.Date; var t = to.Date;
        var entries = await _db.LedgerEntries.AsNoTracking()
            .Include(l => l.Voucher).Include(l => l.Account)
            .Where(filter).Where(l => l.EntryDate >= f && l.EntryDate <= t)
            .OrderBy(l => l.EntryDate).ThenBy(l => l.VoucherId).ThenBy(l => l.Id)
            .ToListAsync();

        var vIds = entries.Select(e => e.VoucherId).Distinct().ToList();
        var ownDetailIds = entries.Select(e => e.VoucherDetailId).ToHashSet();
        var details = await _db.VoucherDetails.AsNoTracking().Include(d => d.Account)
            .Where(d => vIds.Contains(d.VoucherId)).ToListAsync();
        var counterMap = details.Where(d => !ownDetailIds.Contains(d.Id))
            .GroupBy(d => d.VoucherId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(d => d.Account!.Name).Distinct()));

        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var e in entries)
        {
            running += e.Debit - e.Credit;
            lines.Add(new StatementLine
            {
                Date = e.EntryDate,
                VoucherId = e.VoucherId,
                VoucherNo = e.Voucher!.VoucherNo,
                VoucherType = e.Voucher.VoucherType,
                Narration = e.Narration,
                Counterparts = counterMap.TryGetValue(e.VoucherId, out var cp) ? cp : "",
                AccountName = withAccount ? e.Account!.Name : null,
                Debit = e.Debit,
                Credit = e.Credit,
                Balance = running,
                IsReconciled = e.IsReconciled
            });
        }
        return lines;
    }

    public async Task<AccountStatementVm> AccountStatementAsync(int accountId, DateTime from, DateTime to)
    {
        var account = await _db.ChartOfAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        var f = from.Date;
        var opening = await _db.LedgerEntries.Where(l => l.AccountId == accountId && l.EntryDate < f).SumAsync(l => l.Debit - l.Credit);
        return new AccountStatementVm
        {
            Account = account,
            Title = $"{account.Code} · {account.Name}",
            From = f, To = to.Date, Opening = opening,
            Lines = await LinesAsync(l => l.AccountId == accountId, from, to, opening, false)
        };
    }

    public async Task<AccountStatementVm> CashBookAsync(DateTime from, DateTime to)
    {
        var cashIds = await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup).Select(a => a.Id).ToListAsync();
        var f = from.Date;
        var opening = await _db.LedgerEntries.Where(l => cashIds.Contains(l.AccountId) && l.EntryDate < f).SumAsync(l => l.Debit - l.Credit);
        return new AccountStatementVm
        {
            Title = "Cash book — all cash and bank accounts",
            From = f, To = to.Date, Opening = opening, ShowAccountColumn = true,
            Lines = await LinesAsync(l => cashIds.Contains(l.AccountId), from, to, opening, true)
        };
    }

    public async Task<AccountStatementVm> PartyStatementAsync(int partyId, DateTime from, DateTime to)
    {
        var party = await _db.Parties.AsNoTracking().FirstAsync(p => p.Id == partyId);
        var f = from.Date;
        var opening = await _db.LedgerEntries.Where(l => l.PartyId == partyId && l.EntryDate < f).SumAsync(l => l.Debit - l.Credit);
        return new AccountStatementVm
        {
            Party = party,
            Title = $"{party.Code} · {party.Name}",
            From = f, To = to.Date, Opening = opening, ShowAccountColumn = true,
            Lines = await LinesAsync(l => l.PartyId == partyId, from, to, opening, true)
        };
    }

    public async Task<DayBookVm> DayBookAsync(DateTime from, DateTime to, VoucherType? type)
    {
        var f = from.Date; var t = to.Date;
        var q = _db.Vouchers.AsNoTracking()
            .Include(v => v.Details).ThenInclude(d => d.Account)
            .Include(v => v.CreatedBy)
            .Where(v => (v.Status == VoucherStatus.Posted || v.Status == VoucherStatus.Reversed)
                        && v.VoucherDate >= f && v.VoucherDate <= t);
        if (type.HasValue) q = q.Where(v => v.VoucherType == type.Value);
        return new DayBookVm
        {
            From = f, To = t, Type = type,
            Vouchers = await q.OrderBy(v => v.VoucherDate).ThenBy(v => v.ChainIndex).ToListAsync()
        };
    }

    public async Task<VatReportVm> VatAsync(DateTime from, DateTime to)
    {
        var rates = await _db.TaxRates.AsNoTracking().Include(r => r.Account).OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync();
        var sums = await SumsAsync(from, to);
        var vm = new VatReportVm { From = from.Date, To = to.Date };
        foreach (var r in rates)
        {
            sums.TryGetValue(r.AccountId, out var s);
            vm.Rows.Add(new VatRow
            {
                TaxName = r.Name, RatePercent = r.RatePercent, Kind = r.Kind,
                AccountName = $"{r.Account!.Code} {r.Account.Name}", Debit = s.D, Credit = s.C
            });
        }
        return vm;
    }

    public async Task<CostCenterReportVm> CostCentersAsync(DateTime from, DateTime to)
    {
        var f = from.Date; var t = to.Date;
        var centers = await _db.CostCenters.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
        var accounts = await _db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense)
            .ToDictionaryAsync(a => a.Id);
        var pnlIds = accounts.Keys.ToList();
        var raw = await _db.LedgerEntries
            .Where(l => l.CostCenterId != null && l.EntryDate >= f && l.EntryDate <= t && !l.Voucher!.IsSystemGenerated
                        && pnlIds.Contains(l.AccountId))
            .GroupBy(l => new { l.CostCenterId, l.AccountId })
            .Select(g => new { g.Key.CostCenterId, g.Key.AccountId, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();

        var vm = new CostCenterReportVm { From = f, To = t };
        foreach (var c in centers)
        {
            var row = new CostCenterRow { CostCenterId = c.Id, Code = c.Code, Name = c.Name };
            foreach (var d in raw.Where(x => x.CostCenterId == c.Id))
            {
                var acc = accounts[d.AccountId];
                var amount = acc.AccountType == AccountType.Income ? d.C - d.D : d.D - d.C;
                row.Lines.Add(new CostCenterAccountLine { AccountCode = acc.Code, AccountName = acc.Name, Type = acc.AccountType, Amount = amount });
                if (acc.AccountType == AccountType.Income) row.Income += amount; else row.Expense += amount;
            }
            row.Lines = row.Lines.OrderBy(l => l.Type).ThenByDescending(l => l.Amount).ToList();
            vm.Rows.Add(row);
        }
        return vm;
    }

    public async Task<PartyBalancesVm> PartyBalancesAsync(DateTime asOf)
    {
        var t = asOf.Date;
        var parties = await _db.Parties.AsNoTracking().OrderBy(p => p.Code).ToListAsync();
        var sums = await _db.LedgerEntries.Where(l => l.PartyId != null && l.EntryDate <= t)
            .GroupBy(l => l.PartyId)
            .Select(g => new { PartyId = g.Key, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();
        var map = sums.ToDictionary(s => s.PartyId!.Value);
        return new PartyBalancesVm
        {
            AsOf = t,
            Rows = parties.Select(p => new PartyBalanceRow
            {
                PartyId = p.Id, Code = p.Code, Name = p.Name, Type = p.PartyType, CreditLimit = p.CreditLimit,
                Debit = map.TryGetValue(p.Id, out var s) ? s.D : 0,
                Credit = map.TryGetValue(p.Id, out var s2) ? s2.C : 0
            }).ToList()
        };
    }

    public async Task<List<MonthlyPoint>> MonthlyAsync(DateTime firstMonth, int months)
    {
        var start = new DateTime(firstMonth.Year, firstMonth.Month, 1);
        var end = start.AddMonths(months).AddDays(-1);
        var types = await _db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense)
            .ToDictionaryAsync(a => a.Id, a => a.AccountType);
        var ids = types.Keys.ToList();
        var raw = await _db.LedgerEntries
            .Where(l => l.EntryDate >= start && l.EntryDate <= end && !l.Voucher!.IsSystemGenerated && ids.Contains(l.AccountId))
            .GroupBy(l => new { l.EntryDate.Year, l.EntryDate.Month, l.AccountId })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.AccountId, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .ToListAsync();

        var points = new List<MonthlyPoint>();
        for (int i = 0; i < months; i++)
        {
            var m = start.AddMonths(i);
            var p = new MonthlyPoint { MonthStart = m, Label = m.ToString("MMM yy") };
            foreach (var d in raw.Where(x => x.Year == m.Year && x.Month == m.Month))
            {
                if (types[d.AccountId] == AccountType.Income) p.Income += d.C - d.D;
                else p.Expense += d.D - d.C;
            }
            points.Add(p);
        }
        return points;
    }

    public async Task<decimal> CashBalanceAsync(DateTime asOf)
    {
        var t = asOf.Date;
        return await _db.LedgerEntries.Where(l => l.Account!.IsCashOrBank && l.EntryDate <= t).SumAsync(l => l.Debit - l.Credit);
    }
}
