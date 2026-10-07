using Microsoft.AspNetCore.Mvc;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class ReportsController : BaseController
{
    private readonly IReportService _reports;
    public ReportsController(IReportService reports) => _reports = reports;

    public IActionResult Index() => View();

    public async Task<IActionResult> TrialBalance(DateTime? asOf, bool hideZero = true, bool groups = true, string? export = null)
    {
        var vm = await _reports.TrialBalanceAsync((asOf ?? DateTime.Today).Date, hideZero, groups);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Trial balance as of", vm.AsOf).Row("Code", "Account", "Type", "Debit", "Credit");
            foreach (var r in vm.Rows.Where(r => !r.IsGroup)) csv.Row(r.Code, r.Name, r.Type, r.NetDebit, r.NetCredit);
            csv.Row("", "Total", "", vm.TotalDebit, vm.TotalCredit);
            return Csv(csv, "trial-balance");
        }
        return View(vm);
    }

    public async Task<IActionResult> ProfitLoss(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from, to);
        var vm = await _reports.ProfitLossAsync(f, t);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Profit and loss", vm.From, vm.To).Row("Section", "Code", "Account", "Current", "Previous");
            foreach (var r in vm.Income) csv.Row("Income", r.Code, r.Name, r.Current, r.Previous);
            foreach (var r in vm.Expense) csv.Row("Expense", r.Code, r.Name, r.Current, r.Previous);
            csv.Row("", "", "Net profit", vm.NetProfit, vm.PrevNetProfit);
            return Csv(csv, "profit-and-loss");
        }
        return View(vm);
    }

    public async Task<IActionResult> BalanceSheet(DateTime? asOf, string? export)
    {
        var vm = await _reports.BalanceSheetAsync((asOf ?? DateTime.Today).Date);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Balance sheet as of", vm.AsOf).Row("Section", "Code", "Account", "Amount");
            foreach (var r in vm.Assets) csv.Row("Assets", r.Code, r.Name, r.Natural);
            foreach (var r in vm.Liabilities) csv.Row("Liabilities", r.Code, r.Name, r.Natural);
            foreach (var r in vm.Equity) csv.Row("Equity", r.Code, r.Name, r.Natural);
            csv.Row("Equity", "", "Current period earnings", vm.CurrentEarnings);
            return Csv(csv, "balance-sheet");
        }
        return View(vm);
    }

    public async Task<IActionResult> CashFlow(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from, to);
        var vm = await _reports.CashFlowAsync(f, t);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Cash flow statement", vm.From, vm.To).Row("Activity", "Code", "Account", "Cash effect");
            foreach (var l in vm.Lines) csv.Row(l.Category, l.AccountCode, l.AccountName, l.Amount);
            csv.Row("", "", "Opening cash", vm.OpeningCash).Row("", "", "Net change", vm.NetChange).Row("", "", "Closing cash", vm.ClosingCash);
            return Csv(csv, "cash-flow");
        }
        return View(vm);
    }

    public async Task<IActionResult> Vat(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1), to);
        var vm = await _reports.VatAsync(f, t);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("VAT summary", vm.From, vm.To).Row("Tax", "Kind", "Rate %", "Account", "Debit", "Credit", "Net", "Implied taxable value");
            foreach (var r in vm.Rows) csv.Row(r.TaxName, r.Kind, r.RatePercent, r.AccountName, r.Debit, r.Credit, r.Net, r.ImpliedTaxableValue);
            csv.Row("", "", "", "Net VAT payable", "", "", vm.NetPayable, "");
            return Csv(csv, "vat-summary");
        }
        return View(vm);
    }

    public async Task<IActionResult> CostCenters(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from, to);
        var vm = await _reports.CostCentersAsync(f, t);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Cost centre", "Account", "Type", "Amount");
            foreach (var r in vm.Rows)
                foreach (var l in r.Lines) csv.Row($"{r.Code} {r.Name}", $"{l.AccountCode} {l.AccountName}", l.Type, l.Amount);
            return Csv(csv, "cost-centres");
        }
        return View(vm);
    }

    public async Task<IActionResult> PartyBalances(DateTime? asOf, PartyType? type, string? export)
    {
        var vm = await _reports.PartyBalancesAsync((asOf ?? DateTime.Today).Date);
        if (type.HasValue) vm.Rows = vm.Rows.Where(r => r.Type == type).ToList();
        ViewBag.Type = type;
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Code", "Party", "Type", "Debit", "Credit", "Balance (Dr+/Cr-)", "Credit limit");
            foreach (var r in vm.Rows) csv.Row(r.Code, r.Name, r.Type, r.Debit, r.Credit, r.Balance, r.CreditLimit);
            return Csv(csv, "party-balances");
        }
        return View(vm);
    }
}
