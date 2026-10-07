using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class BudgetsController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IAuditService _audit;

    public BudgetsController(AppDbContext db, IReportService reports, IAuditService audit)
    {
        _db = db;
        _reports = reports;
        _audit = audit;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.Budgets.Include(b => b.FiscalYear).Include(b => b.Lines).OrderByDescending(b => b.CreatedAt).ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var budget = await _db.Budgets.Include(b => b.FiscalYear).Include(b => b.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (budget == null) return NotFound();
        var fy = budget.FiscalYear!;
        var to = DateTime.Today < fy.EndDate ? DateTime.Today : fy.EndDate;
        var totalDays = (fy.EndDate - fy.StartDate).TotalDays + 1;
        var elapsed = Math.Clamp(((to - fy.StartDate).TotalDays + 1) / totalDays, 0, 1);
        var sums = await _reports.SumsAsync(fy.StartDate, to, excludeClosing: true);

        var vm = new BudgetVarianceVm { Budget = budget, ElapsedFraction = elapsed };
        foreach (var l in budget.Lines.OrderBy(l => l.Account!.Code))
        {
            sums.TryGetValue(l.AccountId, out var s);
            var actual = l.Account!.AccountType == AccountType.Income ? s.C - s.D : s.D - s.C;
            vm.Rows.Add(new BudgetVarianceRow
            {
                AccountId = l.AccountId, Code = l.Account.Code, Name = l.Account.Name, Type = l.Account.AccountType,
                Budget = l.Amount, Actual = actual, ExpectedToDate = Math.Round(l.Amount * (decimal)elapsed, 2)
            });
        }
        return View(vm);
    }

    private async Task<BudgetFormVm> FormAsync(Budget? b)
    {
        var accounts = await _db.ChartOfAccounts.Where(a => !a.IsGroup && a.IsActive
            && (a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense)).OrderBy(a => a.Code).ToListAsync();
        var existing = b?.Lines.ToDictionary(l => l.AccountId, l => l.Amount) ?? new Dictionary<int, decimal>();
        return new BudgetFormVm
        {
            Id = b?.Id ?? 0, Name = b?.Name ?? "", FiscalYearId = b?.FiscalYearId ?? 0, Notes = b?.Notes,
            Lines = accounts.Select(a => new BudgetLineVm
            {
                AccountId = a.Id, Code = a.Code, Name = a.Name, Type = a.AccountType,
                Amount = existing.TryGetValue(a.Id, out var amt) ? amt : 0
            }).ToList()
        };
    }

    private async Task LoadYearsAsync() =>
        ViewBag.Years = (await _db.FiscalYears.OrderByDescending(f => f.StartDate).ToListAsync())
            .Select(f => new SelectListItem(f.Name + (f.IsClosed ? " (closed)" : ""), f.Id.ToString())).ToList();

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create()
    {
        await LoadYearsAsync();
        var vm = await FormAsync(null);
        vm.FiscalYearId = (await _reports.FiscalYearForAsync(DateTime.Today))?.Id ?? 0;
        return View(vm);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(BudgetFormVm vm)
    {
        if (!vm.Lines.Any(l => l.Amount > 0)) ModelState.AddModelError(string.Empty, "Enter an amount for at least one account.");
        if (!await _db.FiscalYears.AnyAsync(f => f.Id == vm.FiscalYearId)) ModelState.AddModelError(nameof(vm.FiscalYearId), "Choose a fiscal year.");
        if (!ModelState.IsValid) { await LoadYearsAsync(); return View(vm); }
        var b = new Budget { Name = vm.Name, FiscalYearId = vm.FiscalYearId, Notes = vm.Notes };
        foreach (var l in vm.Lines.Where(l => l.Amount > 0)) b.Lines.Add(new BudgetLine { AccountId = l.AccountId, Amount = l.Amount });
        _db.Budgets.Add(b);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Budget", b.Id.ToString(), b.Name);
        Success("Budget saved.");
        return RedirectToAction(nameof(Details), new { id = b.Id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var b = await _db.Budgets.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        await LoadYearsAsync();
        return View(await FormAsync(b));
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, BudgetFormVm vm)
    {
        var b = await _db.Budgets.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        if (!vm.Lines.Any(l => l.Amount > 0)) ModelState.AddModelError(string.Empty, "Enter an amount for at least one account.");
        if (!ModelState.IsValid) { await LoadYearsAsync(); vm.Id = id; return View(vm); }
        b.Name = vm.Name; b.FiscalYearId = vm.FiscalYearId; b.Notes = vm.Notes;
        _db.BudgetLines.RemoveRange(b.Lines);
        b.Lines = vm.Lines.Where(l => l.Amount > 0).Select(l => new BudgetLine { AccountId = l.AccountId, Amount = l.Amount }).ToList();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Budget", b.Id.ToString(), b.Name);
        Success("Budget updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await _db.Budgets.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        _db.Budgets.Remove(b);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Budget", id.ToString(), b.Name);
        Success("Budget deleted.");
        return RedirectToAction(nameof(Index));
    }
}
