using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class LedgerController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;

    public LedgerController(AppDbContext db, IReportService reports)
    {
        _db = db;
        _reports = reports;
    }

    public async Task<IActionResult> Index(DateTime? asOf)
    {
        var t = (asOf ?? DateTime.Today).Date;
        ViewBag.AsOf = t;
        var rows = await _reports.BalanceTreeAsync(null, t, _ => true, hideZero: false);
        return View(rows);
    }

    private static CsvBuilder StatementCsv(AccountStatementVm vm)
    {
        var csv = new CsvBuilder().Row(vm.Title).Row("From", vm.From, "To", vm.To)
            .Row("Date", "Voucher", "Type", "Account", "Narration", "Counter accounts", "Debit", "Credit", "Balance (Dr+/Cr-)")
            .Row(vm.From, "", "", "", "Opening balance", "", "", "", vm.Opening);
        foreach (var l in vm.Lines)
            csv.Row(l.Date, l.VoucherNo, l.VoucherType, l.AccountName, l.Narration, l.Counterparts, l.Debit, l.Credit, l.Balance);
        csv.Row(vm.To, "", "", "", "Closing balance", "", vm.TotalDebit, vm.TotalCredit, vm.Closing);
        return csv;
    }

    public async Task<IActionResult> Account(int? id, DateTime? from, DateTime? to, string? export)
    {
        ViewBag.Accounts = await _db.ChartOfAccounts.AsNoTracking().Where(a => !a.IsGroup).OrderBy(a => a.Code).ToListAsync();
        if (id == null) return View((AccountStatementVm?)null);
        var account = await _db.ChartOfAccounts.FindAsync(id.Value);
        if (account == null) return NotFound();
        if (account.IsGroup)
        {
            Error("Group accounts have no ledger of their own. Pick one of its sub-accounts.");
            return RedirectToAction("Details", "ChartOfAccounts", new { id });
        }
        var (f, t) = Range(from, to);
        var vm = await _reports.AccountStatementAsync(id.Value, f, t);
        if (IsCsv(export)) return Csv(StatementCsv(vm), $"ledger-{account.Code}");
        return View(vm);
    }

    public async Task<IActionResult> CashBook(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from ?? DateTime.Today.AddDays(-30), to);
        var vm = await _reports.CashBookAsync(f, t);
        if (IsCsv(export)) return Csv(StatementCsv(vm), "cash-book");
        return View(vm);
    }

    public async Task<IActionResult> DayBook(DateTime? from, DateTime? to, VoucherType? type, string? export)
    {
        var (f, t) = Range(from ?? DateTime.Today.AddDays(-7), to);
        var vm = await _reports.DayBookAsync(f, t, type);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Date", "Voucher", "Type", "Narration", "Account", "Debit", "Credit");
            foreach (var v in vm.Vouchers)
                foreach (var d in v.Details.OrderBy(d => d.LineNo))
                    csv.Row(v.VoucherDate, v.VoucherNo, v.VoucherType, v.Narration, $"{d.Account!.Code} {d.Account.Name}", d.Debit, d.Credit);
            return Csv(csv, "day-book");
        }
        return View(vm);
    }
}
