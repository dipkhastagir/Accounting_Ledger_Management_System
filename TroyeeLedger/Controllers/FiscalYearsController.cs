using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class FiscalYearsController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IFiscalYearService _fiscal;
    private readonly IAuditService _audit;

    public FiscalYearsController(AppDbContext db, IFiscalYearService fiscal, IAuditService audit)
    {
        _db = db;
        _fiscal = fiscal;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var years = await _db.FiscalYears.OrderByDescending(f => f.StartDate).ToListAsync();
        var counts = new Dictionary<int, int>();
        foreach (var y in years)
            counts[y.Id] = await _db.Vouchers.CountAsync(v => v.VoucherDate >= y.StartDate && v.VoucherDate <= y.EndDate && v.Status == VoucherStatus.Posted);
        ViewBag.Counts = counts;
        return View(years);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create()
    {
        var last = await _db.FiscalYears.OrderByDescending(f => f.EndDate).FirstOrDefaultAsync();
        var start = last?.EndDate.AddDays(1) ?? new DateTime(DateTime.Today.Month >= 7 ? DateTime.Today.Year : DateTime.Today.Year - 1, 7, 1);
        return View(new FiscalYear { StartDate = start, EndDate = start.AddYears(1).AddDays(-1), Name = $"FY {start.Year}-{(start.Year + 1) % 100:D2}" });
    }

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create(FiscalYear model)
    {
        if (model.EndDate <= model.StartDate)
            ModelState.AddModelError(nameof(model.EndDate), "The end date must be after the start date.");
        if (await _db.FiscalYears.AnyAsync(f => f.StartDate <= model.EndDate && f.EndDate >= model.StartDate))
            ModelState.AddModelError(nameof(model.StartDate), "These dates overlap an existing fiscal year.");
        if (!ModelState.IsValid) return View(model);
        model.IsClosed = false;
        _db.FiscalYears.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "FiscalYear", model.Name, $"{model.StartDate:yyyy-MM-dd} to {model.EndDate:yyyy-MM-dd}");
        Success($"{model.Name} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Close(int id)
    {
        var fy = await _db.FiscalYears.FindAsync(id);
        if (fy == null) return NotFound();
        var preview = await _fiscal.PreviewCloseAsync(id);
        ViewBag.Income = preview.Income;
        ViewBag.Expense = preview.Expense;
        ViewBag.Pending = preview.Pending;
        return View(fy);
    }

    [HttpPost, ActionName("Close"), Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> CloseConfirmed(int id)
    {
        var r = await _fiscal.CloseAsync(id);
        if (r.Ok) Success("Fiscal year closed. Income and expenses were transferred to retained earnings."); else Error(r.Error!);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Reopen(int id)
    {
        var r = await _fiscal.ReopenAsync(id);
        if (r.Ok) Success("Fiscal year reopened and its closing entry reversed."); else Error(r.Error!);
        return RedirectToAction(nameof(Index));
    }
}
