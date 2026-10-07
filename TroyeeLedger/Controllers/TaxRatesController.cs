using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class TaxRatesController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public TaxRatesController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.TaxRates.Include(t => t.Account).OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync());

    private async Task LoadAccountsAsync()
    {
        var accounts = await _db.ChartOfAccounts
            .Where(a => !a.IsGroup && (a.AccountType == AccountType.Asset || a.AccountType == AccountType.Liability))
            .OrderBy(a => a.Code).ToListAsync();
        ViewBag.Accounts = accounts.Select(a => new SelectListItem($"{a.Code} — {a.Name}", a.Id.ToString())).ToList();
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create()
    {
        await LoadAccountsAsync();
        return View(new TaxRate { RatePercent = 15 });
    }

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create(TaxRate model)
    {
        ModelState.Remove(nameof(model.Account));
        if (!ModelState.IsValid) { await LoadAccountsAsync(); return View(model); }
        _db.TaxRates.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "TaxRate", model.Id.ToString(), $"{model.Name} {model.RatePercent}%");
        Success($"{model.Name} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.TaxRates.FindAsync(id);
        if (model == null) return NotFound();
        await LoadAccountsAsync();
        return View(model);
    }

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Edit(int id, TaxRate model)
    {
        if (id != model.Id) return BadRequest();
        ModelState.Remove(nameof(model.Account));
        if (!ModelState.IsValid) { await LoadAccountsAsync(); return View(model); }
        _db.TaxRates.Update(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "TaxRate", model.Id.ToString(), $"{model.Name} {model.RatePercent}%");
        Success($"{model.Name} updated.");
        return RedirectToAction(nameof(Index));
    }
}
