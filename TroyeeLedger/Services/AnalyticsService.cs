using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Services;

public interface IAnalyticsService
{
    Task<RatioVm> RatiosAsync(DateTime asOf);
    Task<TrendVm> TrendsAsync(DateTime asOf);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;

    public AnalyticsService(AppDbContext db, IReportService reports)
    {
        _db = db;
        _reports = reports;
    }

    private static double? Div(decimal a, decimal b) => b == 0 ? null : (double)(a / b);

    public async Task<RatioVm> RatiosAsync(DateTime asOf)
    {
        var fy = await _reports.FiscalYearForAsync(asOf);
        var from = fy?.StartDate ?? new DateTime(asOf.Year, 1, 1);
        var bs = await _reports.BalanceSheetAsync(asOf, hideZero: false);
        var pl = await _reports.ProfitLossAsync(from, asOf, hideZero: false);

        var leafAssets = bs.Assets.Where(r => !r.IsGroup).ToList();
        var leafLiab = bs.Liabilities.Where(r => !r.IsGroup).ToList();
        decimal ca = leafAssets.Where(r => r.IsCurrent).Sum(r => r.Natural);
        decimal cash = leafAssets.Where(r => r.IsCashOrBank).Sum(r => r.Natural);
        decimal cl = leafLiab.Where(r => r.IsCurrent).Sum(r => r.Natural);
        decimal ta = bs.TotalAssets, tl = bs.TotalLiabilities, eq = bs.TotalEquityWithEarnings;
        decimal income = pl.TotalIncome, expense = pl.TotalExpense, np = pl.NetProfit;
        var monthsElapsed = Math.Max(1, ((asOf.Year - from.Year) * 12) + asOf.Month - from.Month + 1);
        decimal monthlyBurn = expense / monthsElapsed;

        string Band(double? v, double good, double warn, bool higherIsBetter) =>
            v == null ? "neutral" :
            higherIsBetter ? (v >= good ? "good" : v >= warn ? "warn" : "bad")
                           : (v <= good ? "good" : v <= warn ? "warn" : "bad");

        var vm = new RatioVm { AsOf = asOf.Date, PeriodFrom = from };
        vm.Figures["Current assets"] = ca;
        vm.Figures["Cash and bank"] = cash;
        vm.Figures["Current liabilities"] = cl;
        vm.Figures["Total assets"] = ta;
        vm.Figures["Total liabilities"] = tl;
        vm.Figures["Equity incl. earnings"] = eq;
        vm.Figures["Income (period)"] = income;
        vm.Figures["Expenses (period)"] = expense;
        vm.Figures["Net profit (period)"] = np;

        void Add(string group, string name, double? value, string kind, string formula, string meaning, string status) =>
            vm.Ratios.Add(new RatioItem { Group = group, Name = name, Value = value, Kind = kind, Formula = formula, Meaning = meaning, Status = status });

        var cr = Div(ca, cl);
        Add("Liquidity", "Current ratio", cr, "x", "Current assets ÷ current liabilities", "Ability to pay short-term obligations. 1.5 or above is comfortable.", Band(cr, 1.5, 1.0, true));
        var cashR = Div(cash, cl);
        Add("Liquidity", "Cash ratio", cashR, "x", "Cash and bank ÷ current liabilities", "Obligations that could be settled with cash on hand today.", Band(cashR, 0.5, 0.2, true));
        Add("Liquidity", "Working capital", (double)(ca - cl), "money", "Current assets − current liabilities", "Short-term cushion available to run operations.", ca - cl > 0 ? "good" : "bad");

        var de = Div(tl, eq);
        Add("Solvency", "Debt-to-equity", de, "x", "Total liabilities ÷ equity", "How much the business relies on borrowed money. Under 1.0 is conservative.", Band(de, 1.0, 2.0, false));
        var dr = Div(tl, ta);
        Add("Solvency", "Debt ratio", dr, "pct", "Total liabilities ÷ total assets", "Share of assets financed by creditors.", Band(dr, 0.5, 0.7, false));

        var npm = Div(np, income);
        Add("Profitability", "Net profit margin", npm, "pct", "Net profit ÷ income", "Profit kept from each taka of income.", Band(npm, 0.10, 0.0, true));
        var er = Div(expense, income);
        Add("Profitability", "Expense ratio", er, "pct", "Expenses ÷ income", "Share of income consumed by expenses.", Band(er, 0.85, 1.0, false));
        var roa = Div(np, ta);
        Add("Profitability", "Return on assets", roa, "pct", "Net profit ÷ total assets", "How efficiently assets generate profit (period, not annualised).", Band(roa, 0.05, 0.0, true));
        var roe = Div(np, eq);
        Add("Profitability", "Return on equity", roe, "pct", "Net profit ÷ equity", "Return generated on the owners' stake (period, not annualised).", Band(roe, 0.08, 0.0, true));

        var runway = monthlyBurn == 0 ? (double?)null : (double)(cash / monthlyBurn);
        Add("Sustainability", "Cash runway", runway, "months", "Cash ÷ average monthly expenses", "Months the business could operate on current cash with no new income.", Band(runway, 6, 3, true));
        Add("Sustainability", "Average monthly expenses", (double)monthlyBurn, "money", "Period expenses ÷ months elapsed", "Typical monthly spending in the period.", "neutral");
        return vm;
    }

    public async Task<TrendVm> TrendsAsync(DateTime asOf)
    {
        var first = new DateTime(asOf.Year, asOf.Month, 1).AddMonths(-11);
        var months = await _reports.MonthlyAsync(first, 12);
        var vm = new TrendVm { AsOf = asOf.Date, Months = months };

        // Ordinary least squares on monthly net profit — a transparent baseline forecaster.
        var active = months.Select((m, i) => (x: (double)i, y: (double)m.Net)).ToList();
        int n = active.Count;
        double mx = active.Average(p => p.x), my = active.Average(p => p.y);
        double sxx = active.Sum(p => (p.x - mx) * (p.x - mx));
        double sxy = active.Sum(p => (p.x - mx) * (p.y - my));
        vm.Slope = sxx == 0 ? 0 : sxy / sxx;
        vm.Intercept = my - vm.Slope * mx;
        double ssTot = active.Sum(p => (p.y - my) * (p.y - my));
        double ssRes = active.Sum(p => Math.Pow(p.y - (vm.Intercept + vm.Slope * p.x), 2));
        vm.RSquared = ssTot == 0 ? 0 : 1 - ssRes / ssTot;
        for (int k = 0; k < 3; k++)
        {
            var m = first.AddMonths(n + k);
            var predicted = (decimal)(vm.Intercept + vm.Slope * (n + k));
            vm.Forecast.Add(new MonthlyPoint { MonthStart = m, Label = m.ToString("MMM yy"), Income = predicted, Expense = 0 });
        }

        // Expense mix over the same 12 months
        var expenseRows = await _reports.BalanceTreeAsync(first, asOf, a => a.AccountType == AccountType.Expense, true, excludeClosing: true);
        var leaves = expenseRows.Where(r => !r.IsGroup && r.Natural > 0).OrderByDescending(r => r.Natural).ToList();
        vm.ExpenseMix = leaves.Take(7).Select(r => new KeyValuePair<string, decimal>(r.Name, r.Natural)).ToList();
        var other = leaves.Skip(7).Sum(r => r.Natural);
        if (other > 0) vm.ExpenseMix.Add(new KeyValuePair<string, decimal>("Other", other));

        // Month-end cash position
        var cashIds = await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup).Select(a => a.Id).ToListAsync();
        var opening = await _db.LedgerEntries.Where(l => cashIds.Contains(l.AccountId) && l.EntryDate < first).SumAsync(l => l.Debit - l.Credit);
        var end = first.AddMonths(12);
        var monthly = await _db.LedgerEntries
            .Where(l => cashIds.Contains(l.AccountId) && l.EntryDate >= first && l.EntryDate < end)
            .GroupBy(l => new { l.EntryDate.Year, l.EntryDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(x => x.Debit - x.Credit) })
            .ToListAsync();
        var running = opening;
        foreach (var m in months)
        {
            running += monthly.Where(x => x.Year == m.MonthStart.Year && x.Month == m.MonthStart.Month).Sum(x => x.Net);
            vm.CashBalances.Add(running);
        }
        return vm;
    }
}
