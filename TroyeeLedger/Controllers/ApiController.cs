using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

/// <summary>
/// Read-only JSON endpoints for research pipelines (Python/R notebooks, dashboards).
/// Uses the same cookie session as the web UI.
/// </summary>
[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IForensicService _forensics;

    public ApiController(AppDbContext db, IReportService reports, IForensicService forensics)
    {
        _db = db;
        _reports = reports;
        _forensics = forensics;
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts() =>
        Ok(await _db.ChartOfAccounts.AsNoTracking().OrderBy(a => a.Code)
            .Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.ParentId, a.IsGroup, a.IsCashOrBank, a.IsActive })
            .ToListAsync());

    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance(DateTime? asOf)
    {
        var tb = await _reports.TrialBalanceAsync((asOf ?? DateTime.Today).Date, true, false);
        return Ok(new
        {
            tb.AsOf, tb.TotalDebit, tb.TotalCredit, tb.IsBalanced,
            Rows = tb.Rows.Select(r => new { r.Code, r.Name, r.Type, Debit = r.NetDebit, Credit = r.NetCredit })
        });
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(DateTime? from, DateTime? to, int take = 5000)
    {
        var f = (from ?? DateTime.Today.AddMonths(-3)).Date;
        var t = (to ?? DateTime.Today).Date;
        return Ok(await _db.LedgerEntries.AsNoTracking()
            .Where(l => l.EntryDate >= f && l.EntryDate <= t)
            .OrderBy(l => l.EntryDate).ThenBy(l => l.Id).Take(Math.Clamp(take, 1, 50000))
            .Select(l => new
            {
                l.Id, l.EntryDate, l.Voucher!.VoucherNo, l.Voucher.VoucherType, AccountCode = l.Account!.Code,
                AccountName = l.Account.Name, l.Account.AccountType, l.Debit, l.Credit, l.CostCenterId, l.PartyId,
                CreatedAt = l.Voucher.CreatedAt, l.Voucher.CreatedById, l.Voucher.ApprovedById
            }).ToListAsync());
    }

    [HttpGet("vouchers/{voucherNo}")]
    public async Task<IActionResult> Voucher(string voucherNo)
    {
        var v = await _db.Vouchers.AsNoTracking().Include(x => x.Details).ThenInclude(d => d.Account)
            .FirstOrDefaultAsync(x => x.VoucherNo == voucherNo);
        if (v == null) return NotFound();
        return Ok(new
        {
            v.VoucherNo, v.VoucherType, v.VoucherDate, v.Status, v.Narration, v.TotalAmount, v.ChainIndex, v.PreviousHash, v.Hash,
            Lines = v.Details.OrderBy(d => d.LineNo).Select(d => new { d.LineNo, d.Account!.Code, d.Account.Name, d.Debit, d.Credit })
        });
    }

    [HttpGet("forensics/integrity")]
    public async Task<IActionResult> Integrity() => Ok(await _forensics.VerifyIntegrityAsync());

    [HttpGet("forensics/benford")]
    public async Task<IActionResult> Benford(DateTime? from, DateTime? to) =>
        Ok(await _forensics.BenfordAsync((from ?? DateTime.Today.AddYears(-1)).Date, (to ?? DateTime.Today).Date));

    [HttpGet("forensics/anomalies")]
    public async Task<IActionResult> Anomalies(DateTime? from, DateTime? to, string level = "Low") =>
        Ok(await _forensics.AnomaliesAsync((from ?? DateTime.Today.AddYears(-1)).Date, (to ?? DateTime.Today).Date, level));

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(int months = 12)
    {
        var n = Math.Clamp(months, 1, 60);
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(n - 1));
        return Ok(await _reports.MonthlyAsync(first, n));
    }
}
