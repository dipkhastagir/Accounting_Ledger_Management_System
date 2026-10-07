using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class CostCentersController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CostCentersController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var usage = await _db.VoucherDetails.Where(d => d.CostCenterId != null)
            .GroupBy(d => d.CostCenterId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync();
        ViewBag.Usage = usage.ToDictionary(u => u.Id!.Value, u => u.N);
        return View(await _db.CostCenters.OrderBy(c => c.Code).ToListAsync());
    }

    [Authorize(Roles = Roles.Editors)]
    public IActionResult Create() => View(new CostCenter());

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(CostCenter model)
    {
        if (await _db.CostCenters.AnyAsync(c => c.Code == model.Code))
            ModelState.AddModelError(nameof(model.Code), "This code is already used.");
        if (!ModelState.IsValid) return View(model);
        _db.CostCenters.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "CostCenter", model.Code, model.Name);
        Success($"Cost centre {model.Code} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.CostCenters.FindAsync(id);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, CostCenter model)
    {
        if (id != model.Id) return BadRequest();
        if (await _db.CostCenters.AnyAsync(c => c.Code == model.Code && c.Id != id))
            ModelState.AddModelError(nameof(model.Code), "This code is already used.");
        if (!ModelState.IsValid) return View(model);
        _db.CostCenters.Update(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "CostCenter", model.Code, model.Name);
        Success($"Cost centre {model.Code} updated.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await _db.CostCenters.FindAsync(id);
        if (model == null) return NotFound();
        ViewBag.InUse = await _db.VoucherDetails.CountAsync(d => d.CostCenterId == id);
        return View(model);
    }

    [HttpPost, ActionName("Delete"), Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var model = await _db.CostCenters.FindAsync(id);
        if (model == null) return NotFound();
        if (await _db.VoucherDetails.AnyAsync(d => d.CostCenterId == id))
        {
            model.IsActive = false;
            await _db.SaveChangesAsync();
            Success($"{model.Name} is used on vouchers, so it was deactivated instead of deleted.");
        }
        else
        {
            _db.CostCenters.Remove(model);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Delete", "CostCenter", model.Code, model.Name);
            Success($"Cost centre {model.Code} deleted.");
        }
        return RedirectToAction(nameof(Index));
    }
}
