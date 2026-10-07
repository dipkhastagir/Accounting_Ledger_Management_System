using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class RecurringController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IRecurringService _recurring;
    private readonly IAuditService _audit;

    public RecurringController(AppDbContext db, IRecurringService recurring, IAuditService audit)
    {
        _db = db;
        _recurring = recurring;
        _audit = audit;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.RecurringTemplates.Include(t => t.Lines).ThenInclude(l => l.Account).OrderBy(t => t.NextRunDate).ToListAsync());

    private async Task LoadAccountsAsync() =>
        ViewBag.Accounts = await _db.ChartOfAccounts.AsNoTracking().Where(a => !a.IsGroup && a.IsActive).OrderBy(a => a.Code).ToListAsync();

    private static List<string> Validate(RecurringFormVm vm)
    {
        var errors = new List<string>();
        var lines = vm.Lines.Where(l => l.AccountId != 0).ToList();
        if (lines.Count < 2) errors.Add("Add at least two lines.");
        if (lines.Sum(l => l.Debit) != lines.Sum(l => l.Credit)) errors.Add("Total debit must equal total credit.");
        if (lines.Any(l => (l.Debit > 0) == (l.Credit > 0))) errors.Add("Each line needs either a debit or a credit amount.");
        return errors;
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create()
    {
        await LoadAccountsAsync();
        return View(new RecurringFormVm { NextRunDate = DateTime.Today.AddDays(1), Lines = new() { new(), new() } });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(RecurringFormVm vm)
    {
        foreach (var e in Validate(vm)) ModelState.AddModelError(string.Empty, e);
        if (!ModelState.IsValid) { await LoadAccountsAsync(); while (vm.Lines.Count < 2) vm.Lines.Add(new()); return View(vm); }
        var t = new RecurringTemplate
        {
            Name = vm.Name, VoucherType = vm.VoucherType, Narration = vm.Narration, Frequency = vm.Frequency,
            NextRunDate = vm.NextRunDate.Date, EndDate = vm.EndDate, IsActive = vm.IsActive,
            Lines = vm.Lines.Where(l => l.AccountId != 0).Select(l => new RecurringTemplateLine
            { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, LineNarration = l.LineNarration }).ToList()
        };
        _db.RecurringTemplates.Add(t);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "RecurringTemplate", t.Id.ToString(), t.Name);
        Success($"Recurring template '{t.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await _db.RecurringTemplates.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        await LoadAccountsAsync();
        return View(new RecurringFormVm
        {
            Id = t.Id, Name = t.Name, VoucherType = t.VoucherType, Narration = t.Narration, Frequency = t.Frequency,
            NextRunDate = t.NextRunDate, EndDate = t.EndDate, IsActive = t.IsActive,
            Lines = t.Lines.Select(l => new VoucherLineVm { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, LineNarration = l.LineNarration }).ToList()
        });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, RecurringFormVm vm)
    {
        var t = await _db.RecurringTemplates.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        foreach (var e in Validate(vm)) ModelState.AddModelError(string.Empty, e);
        if (!ModelState.IsValid) { await LoadAccountsAsync(); vm.Id = id; while (vm.Lines.Count < 2) vm.Lines.Add(new()); return View(vm); }
        t.Name = vm.Name; t.VoucherType = vm.VoucherType; t.Narration = vm.Narration; t.Frequency = vm.Frequency;
        t.NextRunDate = vm.NextRunDate.Date; t.EndDate = vm.EndDate; t.IsActive = vm.IsActive;
        _db.RecurringTemplateLines.RemoveRange(t.Lines);
        t.Lines = vm.Lines.Where(l => l.AccountId != 0).Select(l => new RecurringTemplateLine
        { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, LineNarration = l.LineNarration }).ToList();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "RecurringTemplate", t.Id.ToString(), t.Name);
        Success($"'{t.Name}' updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Run(int? id)
    {
        var messages = await _recurring.RunDueAsync(DateTime.Today, id);
        if (messages.Count == 0) Success("Nothing is due today.");
        else TempData["Success"] = string.Join("\n", messages);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.RecurringTemplates.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        _db.RecurringTemplates.Remove(t);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "RecurringTemplate", id.ToString(), t.Name);
        Success($"'{t.Name}' deleted. Vouchers it already created are kept.");
        return RedirectToAction(nameof(Index));
    }
}
