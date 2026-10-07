using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class ForensicsController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IForensicService _forensics;
    private readonly IAccountingService _accounting;
    private readonly IAuditService _audit;

    public ForensicsController(AppDbContext db, IForensicService forensics, IAccountingService accounting, IAuditService audit)
    {
        _db = db;
        _forensics = forensics;
        _accounting = accounting;
        _audit = audit;
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var (f, t) = Range(from, to);
        var vm = new ForensicDashboardVm
        {
            Integrity = await _forensics.VerifyIntegrityAsync(),
            Benford = await _forensics.BenfordAsync(f.AddYears(-1), t),
            Anomalies = await _forensics.AnomaliesAsync(f, t, "Medium"),
            SodViolations = (await _forensics.SegregationAsync()).Violations.Count
        };
        ViewBag.From = f;
        ViewBag.To = t;
        return View(vm);
    }

    public async Task<IActionResult> Benford(DateTime? from, DateTime? to, string? export)
    {
        var (f, t) = Range(from ?? DateTime.Today.AddMonths(-15), to);
        var vm = await _forensics.BenfordAsync(f, t);
        if (IsCsv(export))
        {
            var inv = CultureInfo.InvariantCulture;
            var csv = new CsvBuilder().Row("Digit", "Count", "Observed", "Expected", "Z");
            foreach (var d in vm.Digits) csv.Row(d.Digit, d.Count, d.Observed.ToString("F4", inv), d.Expected.ToString("F4", inv), d.Z.ToString("F3", inv));
            csv.Row("N", vm.N).Row("Chi-square", vm.ChiSquare.ToString("F3", inv)).Row("MAD", vm.Mad.ToString("F5", inv)).Row("Conformity", vm.Conformity);
            return Csv(csv, "benford");
        }
        return View(vm);
    }

    public async Task<IActionResult> Anomalies(DateTime? from, DateTime? to, string level = "Low", string? export = null)
    {
        var (f, t) = Range(from ?? DateTime.Today.AddMonths(-15), to);
        var vm = await _forensics.AnomaliesAsync(f, t, level);
        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Voucher", "Date", "Entered at", "Type", "Amount", "Score", "Level", "Reasons");
            foreach (var i in vm.Items)
                csv.Row(i.VoucherNo, i.Date, i.CreatedAt.ToString("yyyy-MM-dd HH:mm"), i.Type, i.Amount, i.Score, i.Level,
                    string.Join(" | ", i.Reasons.Select(r => $"{r.Rule} (+{r.Points}): {r.Detail}")));
            return Csv(csv, "anomalies");
        }
        return View(vm);
    }

    public async Task<IActionResult> Integrity() => View(await _forensics.VerifyIntegrityAsync());

    public async Task<IActionResult> Segregation() => View(await _forensics.SegregationAsync());

    // ---------- Research demonstrations (admin only) ----------

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SimulateTamper(string kind)
    {
        if (kind == "ledger")
        {
            var entry = await _db.LedgerEntries.Where(l => l.Debit > 0 && l.Voucher!.ChainIndex != null)
                .OrderByDescending(l => l.Id).Skip(5).FirstOrDefaultAsync();
            if (entry == null) { Error("No ledger rows to tamper with."); return RedirectToAction(nameof(Integrity)); }
            entry.Debit += 1000;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("TamperSimulation", "LedgerEntry", entry.Id.ToString(), $"ledger:{entry.Id}");
            Success("Simulated an attacker adding 1,000 to a ledger row directly in the database. Run the check to see it caught.");
        }
        else
        {
            var v = await _db.Vouchers.Where(x => x.ChainIndex != null).OrderByDescending(x => x.ChainIndex).Skip(20).FirstOrDefaultAsync();
            if (v == null) { Error("No sealed vouchers to tamper with."); return RedirectToAction(nameof(Integrity)); }
            var original = v.VoucherDate;
            v.VoucherDate = v.VoucherDate.AddDays(-1);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("TamperSimulation", "Voucher", v.VoucherNo, $"voucher:{v.Id}:{original:yyyy-MM-dd}");
            Success($"Simulated back-dating {v.VoucherNo} by one day directly in the database. Run the check to see it caught.");
        }
        return RedirectToAction(nameof(Integrity));
    }

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Repair()
    {
        var report = await _forensics.VerifyIntegrityAsync();
        int fixedLedger = 0, restored = 0;

        foreach (var issue in report.Issues.Where(i => i.Area == "Ledger" && i.VoucherId.HasValue).DistinctBy(i => i.VoucherId))
            if (await _accounting.RebuildLedgerAsync(issue.VoucherId!.Value) > 0) fixedLedger++;

        var sims = await _db.AuditLogs.Where(a => a.Action == "TamperSimulation" && a.Details != null && a.Details.StartsWith("voucher:"))
            .OrderByDescending(a => a.Id).ToListAsync();
        foreach (var s in sims)
        {
            var parts = s.Details!.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out var vid) &&
                DateTime.TryParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var v = await _db.Vouchers.FindAsync(vid);
                if (v != null && v.VoucherDate != date) { v.VoucherDate = date; restored++; }
            }
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Repair", "Integrity", null, $"Rebuilt ledger for {fixedLedger} voucher(s); restored {restored} simulated voucher edit(s)");
        Success($"Rebuilt ledger rows for {fixedLedger} voucher(s) from their sealed lines and undid {restored} simulated voucher edit(s).");
        return RedirectToAction(nameof(Integrity));
    }
}
