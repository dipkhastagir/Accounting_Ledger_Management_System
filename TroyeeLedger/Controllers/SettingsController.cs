using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

[Authorize(Roles = Roles.Admin)]
public class SettingsController : BaseController
{
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;
    private readonly AppDbContext _db;

    public SettingsController(ISettingsService settings, IAuditService audit, AppDbContext db)
    {
        _settings = settings;
        _audit = audit;
        _db = db;
    }

    public async Task<IActionResult> Index() => View(await _settings.GetAsync());

    [HttpPost]
    public async Task<IActionResult> Index(CompanySetting model)
    {
        if (!ModelState.IsValid) return View(model);
        var s = await _settings.GetAsync();
        s.CompanyName = model.CompanyName; s.Address = model.Address; s.Phone = model.Phone; s.Email = model.Email;
        s.TaxId = model.TaxId; s.CurrencySymbol = model.CurrencySymbol;
        s.RequireApproval = model.RequireApproval; s.EnforceSegregationOfDuties = model.EnforceSegregationOfDuties;
        s.AnomalyZThreshold = model.AnomalyZThreshold; s.LargeAmountThreshold = model.LargeAmountThreshold;
        s.WeekendDays = model.WeekendDays; s.BackdateToleranceDays = model.BackdateToleranceDays;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Settings", null,
            $"Approval {(s.RequireApproval ? "on" : "off")}, SoD {(s.EnforceSegregationOfDuties ? "on" : "off")}, z={s.AnomalyZThreshold}, large={s.LargeAmountThreshold}");
        Success("Settings saved.");
        return RedirectToAction(nameof(Index));
    }
}
