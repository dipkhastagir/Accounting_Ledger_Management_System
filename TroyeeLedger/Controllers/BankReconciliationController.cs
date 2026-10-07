using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class BankReconciliationController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReconciliationService _recon;

    public BankReconciliationController(AppDbContext db, IReconciliationService recon)
    {
        _db = db;
        _recon = recon;
    }

    public async Task<IActionResult> Index(int? accountId, DateTime? asOf)
    {
        var bankId = accountId ?? await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup && a.Name.Contains("Bank"))
            .OrderBy(a => a.Code).Select(a => (int?)a.Id).FirstOrDefaultAsync()
            ?? await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup).Select(a => a.Id).FirstOrDefaultAsync();
        if (bankId == 0)
        {
            Error("Create a cash or bank account first.");
            return RedirectToAction("Index", "ChartOfAccounts");
        }
        return View(await _recon.BuildAsync(bankId, (asOf ?? DateTime.Today).Date));
    }

    private async Task LoadBanksAsync() =>
        ViewBag.Banks = (await _db.ChartOfAccounts.Where(a => a.IsCashOrBank && !a.IsGroup).OrderBy(a => a.Code).ToListAsync())
            .Select(a => new SelectListItem($"{a.Code} — {a.Name}", a.Id.ToString())).ToList();

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Import(int? accountId)
    {
        await LoadBanksAsync();
        return View(new StatementImportVm { BankAccountId = accountId ?? 0 });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Import(StatementImportVm vm)
    {
        if (!ModelState.IsValid) { await LoadBanksAsync(); return View(vm); }
        var (imported, errors) = await _recon.ImportAsync(vm.BankAccountId, vm.CsvText);
        if (errors.Any()) Error($"{errors.Count} line(s) skipped: " + string.Join(" ", errors.Take(5)));
        if (imported > 0) Success($"{imported} statement line(s) imported. Run auto-match to pair them with the ledger.");
        return RedirectToAction(nameof(Index), new { accountId = vm.BankAccountId });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> AutoMatch(int accountId, int window = 3)
    {
        var n = await _recon.AutoMatchAsync(accountId, window);
        Success(n == 0 ? "No new matches found. Remaining items differ in amount or date." : $"{n} item(s) matched automatically.");
        return RedirectToAction(nameof(Index), new { accountId });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Match(int accountId, int statementLineId, long ledgerEntryId)
    {
        var r = await _recon.MatchAsync(statementLineId, ledgerEntryId);
        if (r.Ok) Success("Matched."); else Error(r.Error!);
        return RedirectToAction(nameof(Index), new { accountId });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Unmatch(int accountId, int statementLineId)
    {
        var r = await _recon.UnmatchAsync(statementLineId);
        if (r.Ok) Success("Match removed."); else Error(r.Error!);
        return RedirectToAction(nameof(Index), new { accountId });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> DeleteLine(int accountId, int statementLineId)
    {
        var line = await _db.BankStatementLines.FindAsync(statementLineId);
        if (line != null && !line.IsMatched)
        {
            _db.BankStatementLines.Remove(line);
            await _db.SaveChangesAsync();
            Success("Statement line removed.");
        }
        else Error("Unmatch the line before removing it.");
        return RedirectToAction(nameof(Index), new { accountId });
    }
}
