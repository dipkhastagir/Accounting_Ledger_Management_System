using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class VouchersController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly IForensicService _forensics;
    private readonly ISettingsService _settings;
    private readonly ICurrentUser _user;

    public VouchersController(AppDbContext db, IAccountingService accounting, IForensicService forensics, ISettingsService settings, ICurrentUser user)
    {
        _db = db;
        _accounting = accounting;
        _forensics = forensics;
        _settings = settings;
        _user = user;
    }

    public async Task<IActionResult> Index(string? search, VoucherType? type, VoucherStatus? status, DateTime? from, DateTime? to, string? export, int page = 1)
    {
        var q = _db.Vouchers.AsNoTracking().Include(v => v.CreatedBy).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(v => v.VoucherNo.Contains(s) || (v.Narration != null && v.Narration.Contains(s)) || (v.Reference != null && v.Reference.Contains(s)));
        }
        if (type.HasValue) q = q.Where(v => v.VoucherType == type);
        if (status.HasValue) q = q.Where(v => v.Status == status);
        if (from.HasValue) q = q.Where(v => v.VoucherDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(v => v.VoucherDate <= to.Value.Date);
        q = q.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id);

        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Voucher no", "Date", "Type", "Status", "Narration", "Reference", "Amount", "Created by", "Hash");
            foreach (var v in await q.ToListAsync())
                csv.Row(v.VoucherNo, v.VoucherDate, v.VoucherType, v.Status, v.Narration, v.Reference, v.TotalAmount, v.CreatedBy?.FullName, v.Hash);
            return Csv(csv, "vouchers");
        }

        const int size = 25;
        page = Math.Max(1, page);
        var vm = new VoucherIndexVm
        {
            Search = search, Type = type, Status = status, From = from, To = to,
            Result = new PagedResult<Voucher>
            {
                Page = page, PageSize = size, TotalCount = await q.CountAsync(),
                Items = await q.Skip((page - 1) * size).Take(size).ToListAsync()
            }
        };
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var v = await _db.Vouchers.AsNoTracking()
            .Include(x => x.Details).ThenInclude(d => d.Account)
            .Include(x => x.Details).ThenInclude(d => d.CostCenter)
            .Include(x => x.Details).ThenInclude(d => d.Party)
            .Include(x => x.CreatedBy).Include(x => x.ApprovedBy).Include(x => x.ReversalOf)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound();

        ViewBag.ReversedBy = v.ReversedByVoucherId.HasValue
            ? await _db.Vouchers.Where(x => x.Id == v.ReversedByVoucherId).Select(x => x.VoucherNo).FirstOrDefaultAsync() : null;
        ViewBag.Risk = await _forensics.ScoreVoucherAsync(id);
        ViewBag.LedgerRows = await _db.LedgerEntries.CountAsync(l => l.VoucherId == id);
        ViewBag.ChainOk = v.ChainIndex == null || HashUtil.VoucherHash(v, v.Details, v.PreviousHash ?? "") == v.Hash;
        var s = await _settings.GetAsync();
        ViewBag.CanApprove = v.Status == VoucherStatus.PendingApproval && _user.CanEdit
                             && !(s.EnforceSegregationOfDuties && v.CreatedById == _user.Id);
        ViewBag.History = await _db.AuditLogs.AsNoTracking().Where(a => a.EntityName == "Voucher" && a.EntityId == v.VoucherNo)
            .OrderBy(a => a.Id).ToListAsync();
        return View(v);
    }

    public async Task<IActionResult> Print(int id)
    {
        var v = await _db.Vouchers.AsNoTracking()
            .Include(x => x.Details).ThenInclude(d => d.Account)
            .Include(x => x.Details).ThenInclude(d => d.Party)
            .Include(x => x.Details).ThenInclude(d => d.CostCenter)
            .Include(x => x.CreatedBy).Include(x => x.ApprovedBy)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound();
        ViewBag.Settings = await _settings.GetAsync();
        return View(v);
    }

    private async Task LoadLookupsAsync()
    {
        ViewBag.Accounts = await _db.ChartOfAccounts.AsNoTracking().Where(a => !a.IsGroup && a.IsActive).OrderBy(a => a.Code).ToListAsync();
        ViewBag.CostCenters = await _db.CostCenters.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
        ViewBag.Parties = await _db.Parties.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
        ViewBag.TaxRates = await _db.TaxRates.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.RequireApproval = (await _settings.GetAsync()).RequireApproval;
    }

    private static List<VoucherDetail> ToDetails(VoucherFormVm vm) =>
        vm.Lines.Where(l => l.AccountId != 0 || l.Debit != 0 || l.Credit != 0)
            .Select(l => new VoucherDetail
            {
                AccountId = l.AccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                LineNarration = string.IsNullOrWhiteSpace(l.LineNarration) ? null : l.LineNarration.Trim(),
                CostCenterId = l.CostCenterId,
                PartyId = l.PartyId
            }).ToList();

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(VoucherType type = VoucherType.Payment)
    {
        await LoadLookupsAsync();
        return View(new VoucherFormVm
        {
            VoucherType = type,
            VoucherDate = DateTime.Today,
            Lines = new List<VoucherLineVm> { new(), new() }
        });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Copy(int id)
    {
        var v = await _db.Vouchers.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound();
        await LoadLookupsAsync();
        var vm = new VoucherFormVm
        {
            VoucherType = v.VoucherType,
            VoucherDate = DateTime.Today,
            Narration = v.Narration,
            Lines = v.Details.OrderBy(d => d.LineNo).Select(d => new VoucherLineVm
            {
                AccountId = d.AccountId, Debit = d.Debit, Credit = d.Credit, LineNarration = d.LineNarration,
                CostCenterId = d.CostCenterId, PartyId = d.PartyId
            }).ToList()
        };
        Success($"Copied from {v.VoucherNo}. Review the date and amounts before saving.");
        return View("Create", vm);
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Create(VoucherFormVm vm, string submitAction)
    {
        var header = new Voucher { VoucherType = vm.VoucherType, VoucherDate = vm.VoucherDate, Narration = vm.Narration?.Trim(), Reference = vm.Reference?.Trim() };
        var result = await _accounting.CreateAsync(header, ToDetails(vm), submitAction == "submit");
        if (!result.Ok)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e);
            await LoadLookupsAsync();
            while (vm.Lines.Count < 2) vm.Lines.Add(new VoucherLineVm());
            return View(vm);
        }
        var saved = await _db.Vouchers.AsNoTracking().FirstAsync(x => x.Id == result.Id);
        Success(saved.Status switch
        {
            VoucherStatus.Posted => $"{saved.VoucherNo} posted to the ledger.",
            VoucherStatus.PendingApproval => $"{saved.VoucherNo} sent for approval.",
            _ => $"{saved.VoucherNo} saved as a draft."
        });
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id)
    {
        var v = await _db.Vouchers.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound();
        if (v.Status is not (VoucherStatus.Draft or VoucherStatus.Rejected))
        {
            Error("Only draft or rejected vouchers can be edited.");
            return RedirectToAction(nameof(Details), new { id });
        }
        await LoadLookupsAsync();
        return View(new VoucherFormVm
        {
            Id = v.Id, VoucherNo = v.VoucherNo, VoucherType = v.VoucherType, VoucherDate = v.VoucherDate,
            Narration = v.Narration, Reference = v.Reference, RejectionReason = v.RejectionReason,
            Lines = v.Details.OrderBy(d => d.LineNo).Select(d => new VoucherLineVm
            {
                AccountId = d.AccountId, Debit = d.Debit, Credit = d.Credit, LineNarration = d.LineNarration,
                CostCenterId = d.CostCenterId, PartyId = d.PartyId
            }).ToList()
        });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Edit(int id, VoucherFormVm vm, string submitAction)
    {
        var header = new Voucher { VoucherType = vm.VoucherType, VoucherDate = vm.VoucherDate, Narration = vm.Narration?.Trim(), Reference = vm.Reference?.Trim() };
        var result = await _accounting.UpdateAsync(id, header, ToDetails(vm), submitAction == "submit");
        if (!result.Ok)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e);
            await LoadLookupsAsync();
            vm.Id = id;
            while (vm.Lines.Count < 2) vm.Lines.Add(new VoucherLineVm());
            return View(vm);
        }
        Success("Voucher updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Submit(int id)
    {
        var r = await _accounting.SubmitAsync(id);
        if (r.Ok) Success("Voucher submitted."); else Error(r.Error!);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Approvals()
    {
        var pending = await _db.Vouchers.AsNoTracking()
            .Include(v => v.Details).ThenInclude(d => d.Account)
            .Include(v => v.CreatedBy)
            .Where(v => v.Status == VoucherStatus.PendingApproval)
            .OrderBy(v => v.SubmittedAt).ToListAsync();
        var risks = new Dictionary<int, AnomalyItem>();
        foreach (var v in pending)
        {
            var r = await _forensics.ScoreVoucherAsync(v.Id);
            if (r != null) risks[v.Id] = r;
        }
        ViewBag.Risks = risks;
        ViewBag.SoD = (await _settings.GetAsync()).EnforceSegregationOfDuties;
        ViewBag.Me = _user.Id;
        return View(pending);
    }

    [HttpPost, Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Approve(int id, string? returnTo)
    {
        var r = await _accounting.ApproveAsync(id);
        if (r.Ok) Success("Voucher approved and sealed into the ledger chain."); else Error(r.Error!);
        return returnTo == "queue" ? RedirectToAction(nameof(Approvals)) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Reject(int id, string reason, string? returnTo)
    {
        var r = await _accounting.RejectAsync(id, reason);
        if (r.Ok) Success("Voucher returned to its author."); else Error(r.Error!);
        return returnTo == "queue" ? RedirectToAction(nameof(Approvals)) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _accounting.DeleteDraftAsync(id);
        if (r.Ok) { Success("Draft deleted."); return RedirectToAction(nameof(Index)); }
        Error(r.Error!);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Reverse(int id)
    {
        var v = await _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound();
        if (v.Status != VoucherStatus.Posted)
        {
            Error("Only posted vouchers can be reversed.");
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(new ReverseVm { Id = v.Id, VoucherNo = v.VoucherNo, Amount = v.TotalAmount, ReversalDate = DateTime.Today });
    }

    [HttpPost, Authorize(Roles = Roles.Editors)]
    public async Task<IActionResult> Reverse(ReverseVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var r = await _accounting.ReverseAsync(vm.Id, vm.ReversalDate, vm.Reason.Trim());
        if (!r.Ok)
        {
            foreach (var e in r.Errors) ModelState.AddModelError(string.Empty, e);
            return View(vm);
        }
        Success($"{vm.VoucherNo} reversed. The reversing voucher is shown below.");
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }
}
