using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class ChartOfAccountsController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IAuditService _audit;

    public ChartOfAccountsController(AppDbContext db, IReportService reports, IAuditService audit)
    {
        _db = db;
        _reports = reports;
        _audit = audit;
    }

    public async Task<IActionResult> Index(AccountType? type, string? export)
    {
        var rows = await _reports.BalanceTreeAsync(null, DateTime.Today, a => type == null || a.AccountType == type, hideZero: false);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Code", "Name", "Type", "Level", "Group", "Debit", "Credit", "Balance");
            foreach (var r in rows) csv.Row(r.Code, r.Name, r.Type, r.Level, r.IsGroup ? "Yes" : "No", r.Debit, r.Credit, r.Natural);
            return Csv(csv, "chart-of-accounts");
        }
        ViewBag.Type = type;
        ViewBag.Inactive = await _db.ChartOfAccounts.Where(a => !a.IsActive).Select(a => a.Id).ToListAsync();
        return View(rows);
    }

    public async Task<IActionResult> Details(int id)
    {
        var account = await _db.ChartOfAccounts.Include(a => a.Parent).Include(a => a.Children).FirstOrDefaultAsync(a => a.Id == id);
        if (account == null) return NotFound();
        var t = DateTime.Today;
        var (from, to) = Range(null, t);
        ViewBag.Statement = account.IsGroup ? null : await _reports.AccountStatementAsync(id, from, to);
        var tree = await _reports.BalanceTreeAsync(null, t, a => a.AccountType == account.AccountType, false);
        ViewBag.Balance = tree.FirstOrDefault(r => r.AccountId == id);
        ViewBag.Subtree = tree;
        return View(account);
    }

    private async Task LoadParentsAsync(int? excludeId = null)
    {
        var groups = await _db.ChartOfAccounts.Where(a => a.IsGroup && a.Id != excludeId).OrderBy(a => a.Code).ToListAsync();
        ViewBag.Parents = groups.Select(g => new SelectListItem($"{g.Code} — {g.Name} ({g.AccountType})", g.Id.ToString())).ToList();
    }

    private async Task ValidateAccountAsync(ChartOfAccount model)
    {
        if (await _db.ChartOfAccounts.AnyAsync(a => a.Code == model.Code && a.Id != model.Id))
            ModelState.AddModelError(nameof(model.Code), "Another account already uses this code.");
        if (model.ParentId.HasValue)
        {
            var parent = await _db.ChartOfAccounts.FindAsync(model.ParentId.Value);
            if (parent == null || !parent.IsGroup)
                ModelState.AddModelError(nameof(model.ParentId), "The parent must be a group account.");
            else if (parent.AccountType != model.AccountType)
                ModelState.AddModelError(nameof(model.AccountType), $"The parent group is a {parent.AccountType} account, so this account must be {parent.AccountType} too.");
        }
        if (model.IsGroup && model.IsCashOrBank)
            ModelState.AddModelError(nameof(model.IsCashOrBank), "A group account cannot be a cash or bank account.");
        if (model.IsCashOrBank && model.AccountType != AccountType.Asset)
            ModelState.AddModelError(nameof(model.IsCashOrBank), "Only asset accounts can be cash or bank accounts.");
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(int? parentId)
    {
        await LoadParentsAsync();
        var model = new ChartOfAccount();
        if (parentId.HasValue)
        {
            var p = await _db.ChartOfAccounts.FindAsync(parentId.Value);
            if (p != null) { model.ParentId = p.Id; model.AccountType = p.AccountType; model.IsCurrent = p.IsCurrent; }
        }
        return View(model);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(ChartOfAccount model)
    {
        ModelState.Remove(nameof(model.Parent));
        ModelState.Remove(nameof(model.Children));
        await ValidateAccountAsync(model);
        if (!ModelState.IsValid)
        {
            await LoadParentsAsync();
            return View(model);
        }
        model.Code = model.Code.Trim();
        model.CreatedAt = DateTime.Now;
        _db.ChartOfAccounts.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Account", model.Code, model.Name);
        Success($"Account {model.Code} {model.Name} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.ChartOfAccounts.FindAsync(id);
        if (model == null) return NotFound();
        await LoadParentsAsync(id);
        ViewBag.HasPostings = await _db.LedgerEntries.AnyAsync(l => l.AccountId == id) || await _db.VoucherDetails.AnyAsync(d => d.AccountId == id);
        return View(model);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, ChartOfAccount model)
    {
        if (id != model.Id) return BadRequest();
        ModelState.Remove(nameof(model.Parent));
        ModelState.Remove(nameof(model.Children));
        var existing = await _db.ChartOfAccounts.Include(a => a.Children).FirstOrDefaultAsync(a => a.Id == id);
        if (existing == null) return NotFound();

        var hasPostings = await _db.LedgerEntries.AnyAsync(l => l.AccountId == id) || await _db.VoucherDetails.AnyAsync(d => d.AccountId == id);
        if (hasPostings && model.AccountType != existing.AccountType)
            ModelState.AddModelError(nameof(model.AccountType), "This account already has postings, so its type cannot change.");
        if (hasPostings && model.IsGroup && !existing.IsGroup)
            ModelState.AddModelError(nameof(model.IsGroup), "An account with postings cannot become a group.");
        if (existing.Children.Any() && !model.IsGroup)
            ModelState.AddModelError(nameof(model.IsGroup), "This account has sub-accounts, so it must remain a group.");

        // Prevent cycles: the new parent cannot be this account or one of its descendants
        var cursor = model.ParentId;
        while (cursor.HasValue)
        {
            if (cursor == id) { ModelState.AddModelError(nameof(model.ParentId), "An account cannot sit under itself."); break; }
            cursor = await _db.ChartOfAccounts.Where(a => a.Id == cursor).Select(a => a.ParentId).FirstOrDefaultAsync();
        }
        await ValidateAccountAsync(model);

        if (!ModelState.IsValid)
        {
            await LoadParentsAsync(id);
            ViewBag.HasPostings = hasPostings;
            return View(model);
        }

        existing.Code = model.Code.Trim();
        existing.Name = model.Name;
        existing.AccountType = model.AccountType;
        existing.ParentId = model.ParentId;
        existing.IsGroup = model.IsGroup;
        existing.IsCashOrBank = model.IsCashOrBank;
        existing.IsCurrent = model.IsCurrent;
        existing.CashFlowCategory = model.CashFlowCategory;
        existing.Description = model.Description;
        existing.IsActive = model.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Account", existing.Code, existing.Name);
        Success($"Account {existing.Code} updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await _db.ChartOfAccounts.Include(a => a.Parent).FirstOrDefaultAsync(a => a.Id == id);
        if (model == null) return NotFound();
        ViewBag.Blockers = await BlockersAsync(id);
        return View(model);
    }

    private async Task<List<string>> BlockersAsync(int id)
    {
        var list = new List<string>();
        var children = await _db.ChartOfAccounts.CountAsync(a => a.ParentId == id);
        if (children > 0) list.Add($"It has {children} sub-account(s).");
        var lines = await _db.VoucherDetails.CountAsync(d => d.AccountId == id);
        if (lines > 0) list.Add($"It is used on {lines} voucher line(s). Mark it inactive instead.");
        if (await _db.TaxRates.AnyAsync(t => t.AccountId == id)) list.Add("A tax rate posts to it.");
        if (await _db.BudgetLines.AnyAsync(b => b.AccountId == id)) list.Add("A budget includes it.");
        if (await _db.RecurringTemplateLines.AnyAsync(r => r.AccountId == id)) list.Add("A recurring template uses it.");
        if (await _db.BankStatementLines.AnyAsync(b => b.BankAccountId == id)) list.Add("Bank statement lines are linked to it.");
        return list;
    }

    [HttpPost, ActionName("Delete"), Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var model = await _db.ChartOfAccounts.FindAsync(id);
        if (model == null) return NotFound();
        var blockers = await BlockersAsync(id);
        if (blockers.Any())
        {
            Error("This account cannot be deleted: " + string.Join(" ", blockers));
            return RedirectToAction(nameof(Delete), new { id });
        }
        _db.ChartOfAccounts.Remove(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Account", model.Code, model.Name);
        Success($"Account {model.Code} deleted.");
        return RedirectToAction(nameof(Index));
    }
}
