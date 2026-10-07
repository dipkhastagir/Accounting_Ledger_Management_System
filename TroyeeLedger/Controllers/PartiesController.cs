using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class PartiesController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IAuditService _audit;

    public PartiesController(AppDbContext db, IReportService reports, IAuditService audit)
    {
        _db = db;
        _reports = reports;
        _audit = audit;
    }

    public async Task<IActionResult> Index(string? search, PartyType? type)
    {
        var balances = await _reports.PartyBalancesAsync(DateTime.Today);
        var rows = balances.Rows.AsEnumerable();
        if (type.HasValue) rows = rows.Where(r => r.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
            rows = rows.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Code.Contains(search, StringComparison.OrdinalIgnoreCase));
        ViewBag.Search = search;
        ViewBag.Type = type;
        ViewBag.Inactive = await _db.Parties.Where(p => !p.IsActive).Select(p => p.Id).ToListAsync();
        return View(rows.ToList());
    }

    public async Task<IActionResult> Details(int id, DateTime? from, DateTime? to)
    {
        var party = await _db.Parties.FindAsync(id);
        if (party == null) return NotFound();
        var (f, t) = Range(from ?? DateTime.Today.AddYears(-1), to);
        ViewBag.Statement = await _reports.PartyStatementAsync(id, f, t);
        return View(party);
    }

    private async Task<string> NextCodeAsync(PartyType type)
    {
        var prefix = type switch { PartyType.Customer => "C-", PartyType.Supplier => "S-", PartyType.Employee => "E-", _ => "O-" };
        var codes = await _db.Parties.Where(p => p.Code.StartsWith(prefix)).Select(p => p.Code).ToListAsync();
        var max = codes.Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return prefix + (max + 1).ToString("D3");
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(PartyType type = PartyType.Customer) =>
        View(new Party { PartyType = type, Code = await NextCodeAsync(type) });

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(Party model)
    {
        if (await _db.Parties.AnyAsync(p => p.Code == model.Code))
            ModelState.AddModelError(nameof(model.Code), "Another party already uses this code.");
        if (!ModelState.IsValid) return View(model);
        _db.Parties.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Party", model.Code, model.Name);
        Success($"{model.Name} added.");
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.Parties.FindAsync(id);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, Party model)
    {
        if (id != model.Id) return BadRequest();
        if (await _db.Parties.AnyAsync(p => p.Code == model.Code && p.Id != id))
            ModelState.AddModelError(nameof(model.Code), "Another party already uses this code.");
        if (!ModelState.IsValid) return View(model);
        _db.Parties.Update(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Party", model.Code, model.Name);
        Success($"{model.Name} updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await _db.Parties.FindAsync(id);
        if (model == null) return NotFound();
        ViewBag.InUse = await _db.VoucherDetails.CountAsync(d => d.PartyId == id);
        return View(model);
    }

    [HttpPost, ActionName("Delete"), Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var model = await _db.Parties.FindAsync(id);
        if (model == null) return NotFound();
        if (await _db.VoucherDetails.AnyAsync(d => d.PartyId == id))
        {
            model.IsActive = false;
            await _db.SaveChangesAsync();
            Success($"{model.Name} has transactions, so it was marked inactive instead of deleted.");
            return RedirectToAction(nameof(Index));
        }
        _db.Parties.Remove(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Party", model.Code, model.Name);
        Success($"{model.Name} deleted.");
        return RedirectToAction(nameof(Index));
    }
}
