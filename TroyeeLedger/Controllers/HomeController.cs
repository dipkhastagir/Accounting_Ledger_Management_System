using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;
using TroyeeLedger.ViewModels;

namespace TroyeeLedger.Controllers;

public class HomeController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IForensicService _forensics;
    private readonly IRecurringService _recurring;
    private readonly ISettingsService _settings;

    public HomeController(AppDbContext db, IReportService reports, IForensicService forensics, IRecurringService recurring, ISettingsService settings)
    {
        _db = db;
        _reports = reports;
        _forensics = forensics;
        _recurring = recurring;
        _settings = settings;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var (fyFrom, _) = Range(null, today);
        var s = await _settings.GetAsync();

        var bs = await _reports.BalanceSheetAsync(today, hideZero: false);
        var leafAssets = bs.Assets.Where(r => !r.IsGroup).ToList();
        var leafLiab = bs.Liabilities.Where(r => !r.IsGroup).ToList();
        var months = await _reports.MonthlyAsync(monthStart.AddMonths(-11), 12);
        var fyPl = await _reports.ProfitLossAsync(fyFrom, today, hideZero: true);
        var integrity = await _forensics.VerifyIntegrityAsync();
        var anomalies = await _forensics.AnomaliesAsync(fyFrom, today, "High");

        var vm = new DashboardVm
        {
            CompanyName = s.CompanyName,
            CashAndBank = leafAssets.Where(r => r.IsCashOrBank).Sum(r => r.Natural),
            CashAccounts = leafAssets.Where(r => r.IsCashOrBank).ToList(),
            Receivables = leafAssets.Where(r => r.Code == "1020").Sum(r => r.Natural),
            Payables = leafLiab.Where(r => r.Code == "2001").Sum(r => r.Natural),
            MonthIncome = months.Last().Income,
            MonthExpense = months.Last().Expense,
            YearProfit = fyPl.NetProfit,
            Months = months,
            PendingApprovals = await _db.Vouchers.CountAsync(v => v.Status == VoucherStatus.PendingApproval),
            Drafts = await _db.Vouchers.CountAsync(v => v.Status == VoucherStatus.Draft || v.Status == VoucherStatus.Rejected),
            DueRecurring = await _recurring.CountDueAsync(today),
            HighRisk = anomalies.Items.Count,
            IntegrityOk = integrity.AllOk,
            ChainHead = integrity.ChainHead,
            ChainLength = integrity.VouchersChecked,
            Recent = await _db.Vouchers.AsNoTracking().Include(v => v.CreatedBy)
                .OrderByDescending(v => v.CreatedAt).Take(8).ToListAsync(),
            TopExpenses = fyPl.Expense.Where(r => !r.IsGroup).OrderByDescending(r => r.Current).Take(6)
                .Select(r => new KeyValuePair<string, decimal>(r.Name, r.Current)).ToList()
        };
        return View(vm);
    }

    public IActionResult About() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(model: Activity.Current?.Id ?? HttpContext.TraceIdentifier);
}
