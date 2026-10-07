using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Data;

/// <summary>
/// Creates the database (if missing) and fills it with ~15 months of realistic,
/// fully double-entry business activity relative to today's date, including a
/// handful of deliberately planted irregularities for the forensic module.
/// </summary>
public class DbSeeder
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly IFiscalYearService _fiscal;
    private readonly IAuditService _audit;
    private readonly ILogger<DbSeeder> _log;
    private readonly Random _rnd = new(2026);

    private readonly Dictionary<string, int> _acc = new();
    private readonly Dictionary<string, int> _seq = new();
    private AppUser _admin = null!, _farhana = null!, _rafiq = null!;
    private List<Party> _customers = new(), _suppliers = new();
    private Dictionary<string, int> _cc = new();
    private int _skipped;

    public DbSeeder(AppDbContext db, IAccountingService accounting, IFiscalYearService fiscal, IAuditService audit, ILogger<DbSeeder> log)
    {
        _db = db;
        _accounting = accounting;
        _fiscal = fiscal;
        _audit = audit;
        _log = log;
    }

    public async Task SeedAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        if (await _db.Users.AnyAsync()) return;

        _log.LogInformation("Seeding TroyeeLedger demo data. This runs once and may take 20–60 seconds…");
        await SeedMasterDataAsync();
        await SeedTransactionsAsync();
        await SeedPlanningAsync();
        await _audit.LogAsync("Seed", "Database", null, $"Demo data created. Skipped vouchers: {_skipped}");
        _log.LogInformation("Seeding complete.");
    }

    // ------------------------------------------------------------------ master
    private async Task SeedMasterDataAsync()
    {
        _admin = new AppUser { Username = "admin", FullName = "System Administrator", Email = "admin@troyeeledger.local", Role = Roles.Admin, PasswordHash = PasswordHasher.Hash("Admin@123") };
        _farhana = new AppUser { Username = "accountant", FullName = "Farhana Akter", Email = "farhana@troyeeledger.local", Role = Roles.Accountant, PasswordHash = PasswordHasher.Hash("Account@123") };
        _rafiq = new AppUser { Username = "checker", FullName = "Rafiq Hasan", Email = "rafiq@troyeeledger.local", Role = Roles.Accountant, PasswordHash = PasswordHasher.Hash("Checker@123") };
        var auditor = new AppUser { Username = "auditor", FullName = "Nusrat Jahan", Email = "nusrat@troyeeledger.local", Role = Roles.Auditor, PasswordHash = PasswordHasher.Hash("Audit@123") };
        var viewer = new AppUser { Username = "viewer", FullName = "Guest Viewer", Role = Roles.Viewer, PasswordHash = PasswordHasher.Hash("Viewer@123") };
        _db.Users.AddRange(_admin, _farhana, _rafiq, auditor, viewer);

        _db.CompanySettings.Add(new CompanySetting
        {
            CompanyName = "Best Business Bond Ltd. (3BL)",
            Address = "46 Kazi Nazrul Islam Avenue, Karwan Bazar, Dhaka-1215",
            Phone = "+880 1817-566720",
            Email = "accounts@3bl.local",
            TaxId = "000123456-0101",
            CurrencySymbol = "Tk"
        });
        await _db.SaveChangesAsync();

        // code, name, type, parent, isGroup, cash, current, cashflow
        var chart = new (string Code, string Name, AccountType Type, string? Parent, bool Group, bool Cash, bool Current, CashFlowCategory Cf)[]
        {
            ("1000", "Assets", AccountType.Asset, null, true, false, true, CashFlowCategory.Auto),
            ("1100", "Current Assets", AccountType.Asset, "1000", true, false, true, CashFlowCategory.Auto),
            ("1001", "Cash in Hand", AccountType.Asset, "1100", false, true, true, CashFlowCategory.Auto),
            ("1002", "Petty Cash", AccountType.Asset, "1100", false, true, true, CashFlowCategory.Auto),
            ("1010", "Dutch-Bangla Bank — Current A/C", AccountType.Asset, "1100", false, true, true, CashFlowCategory.Auto),
            ("1011", "BRAC Bank — SND A/C", AccountType.Asset, "1100", false, true, true, CashFlowCategory.Auto),
            ("1020", "Accounts Receivable", AccountType.Asset, "1100", false, false, true, CashFlowCategory.Auto),
            ("1030", "Inventory", AccountType.Asset, "1100", false, false, true, CashFlowCategory.Auto),
            ("1040", "Advances & Prepayments", AccountType.Asset, "1100", false, false, true, CashFlowCategory.Auto),
            ("1050", "VAT Input Receivable", AccountType.Asset, "1100", false, false, true, CashFlowCategory.Auto),
            ("1200", "Non-Current Assets", AccountType.Asset, "1000", true, false, false, CashFlowCategory.Auto),
            ("1201", "Furniture & Fixtures", AccountType.Asset, "1200", false, false, false, CashFlowCategory.Investing),
            ("1202", "Computer Equipment", AccountType.Asset, "1200", false, false, false, CashFlowCategory.Investing),
            ("1203", "Vehicles", AccountType.Asset, "1200", false, false, false, CashFlowCategory.Investing),
            ("1209", "Accumulated Depreciation", AccountType.Asset, "1200", false, false, false, CashFlowCategory.Operating),
            ("2000", "Liabilities", AccountType.Liability, null, true, false, true, CashFlowCategory.Auto),
            ("2100", "Current Liabilities", AccountType.Liability, "2000", true, false, true, CashFlowCategory.Auto),
            ("2001", "Accounts Payable", AccountType.Liability, "2100", false, false, true, CashFlowCategory.Auto),
            ("2002", "Salary Payable", AccountType.Liability, "2100", false, false, true, CashFlowCategory.Auto),
            ("2003", "VAT Output Payable", AccountType.Liability, "2100", false, false, true, CashFlowCategory.Auto),
            ("2004", "Accrued Expenses", AccountType.Liability, "2100", false, false, true, CashFlowCategory.Auto),
            ("2200", "Long-term Liabilities", AccountType.Liability, "2000", true, false, false, CashFlowCategory.Auto),
            ("2201", "Term Loan — BRAC Bank", AccountType.Liability, "2200", false, false, false, CashFlowCategory.Financing),
            ("3000", "Equity", AccountType.Equity, null, true, false, true, CashFlowCategory.Auto),
            ("3001", "Owner's Capital", AccountType.Equity, "3000", false, false, true, CashFlowCategory.Financing),
            ("3002", "Retained Earnings", AccountType.Equity, "3000", false, false, true, CashFlowCategory.Financing),
            ("3003", "Owner's Drawings", AccountType.Equity, "3000", false, false, true, CashFlowCategory.Financing),
            ("4000", "Income", AccountType.Income, null, true, false, true, CashFlowCategory.Auto),
            ("4001", "Software Sales Revenue", AccountType.Income, "4000", false, false, true, CashFlowCategory.Auto),
            ("4002", "Service & Support Revenue", AccountType.Income, "4000", false, false, true, CashFlowCategory.Auto),
            ("4003", "Other Income", AccountType.Income, "4000", false, false, true, CashFlowCategory.Auto),
            ("5000", "Expenses", AccountType.Expense, null, true, false, true, CashFlowCategory.Auto),
            ("5100", "Operating Expenses", AccountType.Expense, "5000", true, false, true, CashFlowCategory.Auto),
            ("5001", "Salary Expense", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5002", "Office Rent Expense", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5003", "Utility Expense", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5004", "Internet & Communication", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5005", "Office Supplies", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5006", "Transportation", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5007", "Consultancy & Professional Fees", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5008", "Marketing & Promotion", AccountType.Expense, "5100", false, false, true, CashFlowCategory.Auto),
            ("5200", "Cost of Sales", AccountType.Expense, "5000", true, false, true, CashFlowCategory.Auto),
            ("5201", "Hardware & License Purchases", AccountType.Expense, "5200", false, false, true, CashFlowCategory.Auto),
            ("5300", "Financial & Other Expenses", AccountType.Expense, "5000", true, false, true, CashFlowCategory.Auto),
            ("5301", "Bank Charges", AccountType.Expense, "5300", false, false, true, CashFlowCategory.Auto),
            ("5302", "Interest Expense", AccountType.Expense, "5300", false, false, true, CashFlowCategory.Auto),
            ("5303", "Depreciation Expense", AccountType.Expense, "5300", false, false, true, CashFlowCategory.Auto),
        };
        var map = new Dictionary<string, ChartOfAccount>();
        foreach (var c in chart)
        {
            var a = new ChartOfAccount
            {
                Code = c.Code, Name = c.Name, AccountType = c.Type, IsGroup = c.Group, IsCashOrBank = c.Cash,
                IsCurrent = c.Current, CashFlowCategory = c.Cf, Parent = c.Parent == null ? null : map[c.Parent]
            };
            map[c.Code] = a;
            _db.ChartOfAccounts.Add(a);
        }
        await _db.SaveChangesAsync();
        foreach (var kv in map) _acc[kv.Key] = kv.Value.Id;

        var centers = new[] { ("HQ", "Head Office"), ("SALES", "Sales & Marketing"), ("DEV", "Software Development"), ("SUP", "Customer Support") };
        foreach (var (code, name) in centers) _db.CostCenters.Add(new CostCenter { Code = code, Name = name });

        _customers = new List<Party>
        {
            new() { Code = "C-001", Name = "Meghna Textiles Ltd.", PartyType = PartyType.Customer, Phone = "+880 2-9881234", Email = "accounts@meghnatex.local", CreditLimit = 1500000, Address = "Narayanganj" },
            new() { Code = "C-002", Name = "Padma Agro Industries", PartyType = PartyType.Customer, Phone = "+880 1711-200300", CreditLimit = 800000, Address = "Rajshahi" },
            new() { Code = "C-003", Name = "Karnaphuli Shipping Lines", PartyType = PartyType.Customer, Phone = "+880 31-712345", CreditLimit = 2000000, Address = "Chattogram" },
            new() { Code = "C-004", Name = "Sundarban Pharma", PartyType = PartyType.Customer, Phone = "+880 1819-445566", CreditLimit = 1000000, Address = "Khulna" },
            new() { Code = "C-005", Name = "Jamuna Poultry Farms", PartyType = PartyType.Customer, Phone = "+880 1552-778899", CreditLimit = 500000, Address = "Gazipur" },
            new() { Code = "C-006", Name = "Teesta Retail Chain", PartyType = PartyType.Customer, Phone = "+880 1913-101010", CreditLimit = 700000, Address = "Rangpur" },
        };
        _suppliers = new List<Party>
        {
            new() { Code = "S-001", Name = "Smart Technologies (BD) Ltd.", PartyType = PartyType.Supplier, Phone = "+880 2-8836001", Address = "Dhaka" },
            new() { Code = "S-002", Name = "Global Brand Pvt. Ltd.", PartyType = PartyType.Supplier, Phone = "+880 2-9112233", Address = "Dhaka" },
            new() { Code = "S-003", Name = "Ryans Computers", PartyType = PartyType.Supplier, Phone = "+880 1755-662211", Address = "Dhaka" },
            new() { Code = "S-004", Name = "Microsoft Licensing Partner BD", PartyType = PartyType.Supplier, Phone = "+880 1777-000111", Address = "Dhaka" },
        };
        _db.Parties.AddRange(_customers);
        _db.Parties.AddRange(_suppliers);
        _db.Parties.Add(new Party { Code = "E-001", Name = "Staff Welfare Fund", PartyType = PartyType.Other });

        _db.TaxRates.AddRange(
            new TaxRate { Name = "VAT 15% (standard, sales)", RatePercent = 15, Kind = TaxKind.Output, AccountId = _acc["2003"] },
            new TaxRate { Name = "VAT 5% (reduced, sales)", RatePercent = 5, Kind = TaxKind.Output, AccountId = _acc["2003"], IsActive = false },
            new TaxRate { Name = "VAT 15% (purchases)", RatePercent = 15, Kind = TaxKind.Input, AccountId = _acc["1050"] });
        await _db.SaveChangesAsync();
        _cc = await _db.CostCenters.ToDictionaryAsync(c => c.Code, c => c.Id);
    }

    // ----------------------------------------------------------- transactions
    private static DateTime FyStart(DateTime d) => new(d.Month >= 7 ? d.Year : d.Year - 1, 7, 1);

    private double Gaussian()
    {
        var u1 = 1.0 - _rnd.NextDouble();
        var u2 = 1.0 - _rnd.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private decimal LogNormal(double median, double sigma, double min, double max)
    {
        var v = median * Math.Exp(sigma * Gaussian());
        return Math.Round((decimal)Math.Clamp(v, min, max), 0);
    }

    private decimal Between(double min, double max) => Math.Round((decimal)(min + _rnd.NextDouble() * (max - min)), 0);

    private DateTime WorkTime(DateTime date)
    {
        var created = date.AddDays(_rnd.Next(0, 3)).AddHours(9 + _rnd.Next(0, 9)).AddMinutes(_rnd.Next(0, 60));
        if (created.DayOfWeek == DayOfWeek.Friday) created = created.AddDays(1);
        if (created.DayOfWeek == DayOfWeek.Saturday) created = created.AddDays(1);
        var limit = DateTime.Now.AddMinutes(-30);
        return created > limit ? limit : created;
    }

    private string NextNo(VoucherType t, DateTime d)
    {
        var key = $"{Fmt.TypeCode(t)}-{d:yyyy}-";
        _seq[key] = _seq.TryGetValue(key, out var n) ? n + 1 : 1;
        return key + _seq[key].ToString("D4");
    }

    /// <summary>Re-reads voucher numbers from the database after service-generated vouchers (reversals, closings).</summary>
    private async Task ResyncNumbersAsync()
    {
        var numbers = await _db.Vouchers.Select(v => v.VoucherNo).ToListAsync();
        foreach (var no in numbers)
        {
            var cut = no.LastIndexOf('-');
            if (cut < 0 || !int.TryParse(no[(cut + 1)..], out var n)) continue;
            var key = no[..(cut + 1)];
            if (!_seq.TryGetValue(key, out var cur) || n > cur) _seq[key] = n;
        }
    }

    private static VoucherDetail Dr(int acc, decimal amt, string? cc = null, int? party = null, string? note = null, Dictionary<string, int>? ccs = null) =>
        new() { AccountId = acc, Debit = amt, LineNarration = note, PartyId = party, CostCenterId = cc != null && ccs != null ? ccs[cc] : null };

    private static VoucherDetail Cr(int acc, decimal amt, string? cc = null, int? party = null, string? note = null, Dictionary<string, int>? ccs = null) =>
        new() { AccountId = acc, Credit = amt, LineNarration = note, PartyId = party, CostCenterId = cc != null && ccs != null ? ccs[cc] : null };

    private VoucherDetail D(string code, decimal amt, string? cc = null, int? party = null) => Dr(_acc[code], amt, cc, party, null, _cc);
    private VoucherDetail C(string code, decimal amt, string? cc = null, int? party = null) => Cr(_acc[code], amt, cc, party, null, _cc);

    private async Task<Voucher?> Post(VoucherType type, DateTime date, string narration, string? reference, params VoucherDetail[] lines)
        => await PostAs(type, date, narration, reference, null, null, null, lines);

    private async Task<Voucher?> PostAs(VoucherType type, DateTime date, string? narration, string? reference,
        AppUser? creator, AppUser? approver, DateTime? createdAt, params VoucherDetail[] lines)
    {
        if (date.Date > DateTime.Today) return null;
        creator ??= _rnd.NextDouble() < 0.7 ? _farhana : _rafiq;
        approver ??= creator == _farhana ? (_rnd.NextDouble() < 0.8 ? _rafiq : _admin) : (_rnd.NextDouble() < 0.8 ? _farhana : _admin);
        var created = createdAt ?? WorkTime(date);
        var v = new Voucher
        {
            VoucherNo = NextNo(type, date),
            VoucherType = type,
            VoucherDate = date.Date,
            Narration = narration,
            Reference = reference,
            CreatedById = creator.Id,
            CreatedAt = created,
            SubmittedAt = created.AddMinutes(_rnd.Next(2, 40)),
            ApprovedAt = created.AddHours(_rnd.Next(1, 20)),
            Details = lines.ToList()
        };
        var result = await _accounting.CreateAndPostAsync(v, approver.Id);
        if (!result.Ok)
        {
            _skipped++;
            _log.LogWarning("Seed voucher skipped ({Date:d} {Narration}): {Error}", date, narration, result.Error);
            _db.ChangeTracker.Clear();
            return null;
        }
        _db.ChangeTracker.Clear(); // keeps SaveChanges fast while seeding hundreds of vouchers
        return v;
    }

    private async Task SeedTransactionsAsync()
    {
        var today = DateTime.Today;
        var start = new DateTime(today.Year, today.Month, 1).AddMonths(-15);

        // Fiscal years (Bangladesh: 1 July – 30 June)
        for (var fy = FyStart(start); fy <= today; fy = fy.AddYears(1))
            _db.FiscalYears.Add(new FiscalYear { Name = $"FY {fy.Year}-{(fy.Year + 1) % 100:D2}", StartDate = fy, EndDate = fy.AddYears(1).AddDays(-1) });
        await _db.SaveChangesAsync();

        // Opening capital, loan and fixed assets
        await Post(VoucherType.Receipt, start, "Initial capital injected by the owners", "CAP-01", D("1010", 6000000), C("3001", 6000000));
        await Post(VoucherType.Contra, start.AddDays(1), "Cash withdrawn for office float", "CHQ-100201", D("1001", 150000), C("1010", 150000));
        await Post(VoucherType.Contra, start.AddDays(1), "Petty cash established", "PC-01", D("1002", 20000), C("1001", 20000));
        await Post(VoucherType.Receipt, start.AddDays(20), "Term loan disbursed by BRAC Bank (5 years, 11%)", "LN-7781", D("1011", 2500000), C("2201", 2500000));
        await Post(VoucherType.Payment, start.AddDays(4), "Developer workstations and servers", "INV-RY-5521", D("1202", 1265400, "DEV"), C("1010", 1265400));
        await Post(VoucherType.Payment, start.AddDays(6), "Office furniture for Karwan Bazar office", "INV-HT-339", D("1201", 418750, "HQ"), C("1010", 418750));
        await Post(VoucherType.Payment, start.AddDays(40), "Company vehicle (Toyota Probox)", "INV-NV-118", D("1203", 2185000, "SUP"), C("1011", 2185000));

        var openInvoices = new List<(DateTime Date, Party Party, decimal Gross)>();
        var openBills = new List<(DateTime Date, Party Party, decimal Gross)>();

        for (var month = start; month <= today; month = month.AddMonths(1))
        {
            int dim = DateTime.DaysInMonth(month.Year, month.Month);
            DateTime Day(int d) => new DateTime(month.Year, month.Month, Math.Min(d, dim));

            // Rent and utilities
            await Post(VoucherType.Payment, Day(3), $"Office rent for {month:MMMM yyyy}", $"CHQ-{_rnd.Next(100300, 109999)}", D("5002", 95000, "HQ"), C("1010", 95000));
            var util = Between(9200, 21800);
            await Post(VoucherType.Payment, Day(8), $"DESCO electricity and WASA bill — {month:MMM}", null, D("5003", util, "HQ"), C("1001", util));
            var net = Between(11400, 14900);
            await Post(VoucherType.Payment, Day(10), "Internet (Link3) and mobile bills", null, D("5004", net, "DEV"), C("1010", net));

            // Sales on credit with 15% VAT
            int salesCount = 6 + _rnd.Next(0, 6);
            for (int i = 0; i < salesCount; i++)
            {
                var cust = _customers[_rnd.Next(_customers.Count)];
                var baseAmt = LogNormal(140000, 0.85, 8000, 1200000);
                var vat = Math.Round(baseAmt * 0.15m, 0);
                var revenue = _rnd.NextDouble() < 0.55 ? "4001" : "4002";
                var date = Day(1 + _rnd.Next(0, dim));
                var cc = revenue == "4001" ? "SALES" : "SUP";
                var v = await Post(VoucherType.Journal, date, $"Invoice to {cust.Name} — {(revenue == "4001" ? "Troyee ERP licences" : "annual support & customisation")}",
                    $"INV-{date:yyMM}-{i + 1:D2}", D("1020", baseAmt + vat, null, cust.Id), C(revenue, baseAmt, cc), C("2003", vat));
                if (v != null) openInvoices.Add((date, cust, baseAmt + vat));
            }

            // Collections: invoices older than ~3 weeks are mostly collected
            foreach (var inv in openInvoices.Where(x => (Day(dim) - x.Date).TotalDays >= 20).ToList())
            {
                if (_rnd.NextDouble() < 0.12) continue; // a few stay overdue
                var date = inv.Date.AddDays(20 + _rnd.Next(0, 25));
                if (date > today) continue;
                var bank = _rnd.NextDouble() < 0.7 ? "1010" : "1011";
                var v = await Post(VoucherType.Receipt, date, $"Collection from {inv.Party.Name}", $"EFT-{_rnd.Next(100000, 999999)}",
                    D(bank, inv.Gross), C("1020", inv.Gross, null, inv.Party.Id));
                if (v != null) openInvoices.Remove(inv);
            }

            // Purchases on credit with input VAT
            int purchases = 2 + _rnd.Next(0, 3);
            for (int i = 0; i < purchases; i++)
            {
                var sup = _suppliers[_rnd.Next(_suppliers.Count)];
                var baseAmt = LogNormal(65000, 0.8, 4000, 700000);
                var vat = Math.Round(baseAmt * 0.15m, 0);
                var date = Day(1 + _rnd.Next(0, dim));
                var v = await Post(VoucherType.Journal, date, $"Bill from {sup.Name} — hardware & third-party licences",
                    $"BILL-{_rnd.Next(1000, 9999)}", D("5201", baseAmt, "SALES"), D("1050", vat), C("2001", baseAmt + vat, null, sup.Id));
                if (v != null) openBills.Add((date, sup, baseAmt + vat));
            }
            foreach (var bill in openBills.Where(x => (Day(dim) - x.Date).TotalDays >= 15).ToList())
            {
                var date = bill.Date.AddDays(15 + _rnd.Next(0, 20));
                if (date > today) continue;
                var v = await Post(VoucherType.Payment, date, $"Payment to {bill.Party.Name}", $"CHQ-{_rnd.Next(100300, 109999)}",
                    D("2001", bill.Gross, null, bill.Party.Id), C("1010", bill.Gross));
                if (v != null) openBills.Remove(bill);
            }

            // Payroll: accrue on the 28th, pay on the 3rd of next month
            var salaryDate = Day(28);
            var salary = Between(712000, 768000);
            if (salaryDate <= today)
            {
                var dev = Math.Round(salary * 0.55m, 0);
                var sup = Math.Round(salary * 0.2m, 0);
                var sales = Math.Round(salary * 0.15m, 0);
                await Post(VoucherType.Journal, salaryDate, $"Salary accrual for {month:MMMM yyyy}", null,
                    D("5001", dev, "DEV"), D("5001", sup, "SUP"), D("5001", sales, "SALES"), D("5001", salary - dev - sup - sales, "HQ"), C("2002", salary));
                await Post(VoucherType.Payment, salaryDate.AddDays(6), $"Salary disbursed for {month:MMMM yyyy}", "PAYROLL", D("2002", salary), C("1010", salary));
            }

            // Small cash expenses
            for (int i = 0; i < 3 + _rnd.Next(0, 4); i++)
            {
                var amt = Between(450, 6800);
                var useTransport = _rnd.NextDouble() < 0.5;
                await Post(VoucherType.Payment, Day(1 + _rnd.Next(0, dim)), useTransport ? "Local conveyance and courier" : "Stationery and pantry supplies", null,
                    D(useTransport ? "5006" : "5005", amt, useTransport ? "SUP" : "HQ"), C(_rnd.NextDouble() < 0.5 ? "1002" : "1001", amt));
            }
            await Post(VoucherType.Contra, Day(15), "Petty cash top-up", null, D("1002", 12000), C("1001", 12000));
            await Post(VoucherType.Contra, Day(16), "Cash withdrawn from Dutch-Bangla Bank", $"CHQ-{_rnd.Next(100300, 109999)}", D("1001", 60000), C("1010", 60000));

            if (_rnd.NextDouble() < 0.6)
            {
                var mk = LogNormal(38000, 0.6, 9000, 160000);
                await Post(VoucherType.Payment, Day(12 + _rnd.Next(0, 10)), "Facebook/Google ads and trade-fair materials", null, D("5008", mk, "SALES"), C("1011", mk));
            }

            // Bank charges, loan interest, depreciation
            var charge = Between(345, 1150);
            await Post(VoucherType.Payment, Day(dim), "Bank service charges and SMS banking fee", null, D("5301", charge), C("1010", charge));
            await Post(VoucherType.Payment, Day(dim), "Monthly interest on BRAC term loan", "LN-7781", D("5302", 22917), C("1011", 22917));
            await Post(VoucherType.Journal, Day(dim), "Monthly depreciation (straight line)", null, D("5303", 64980, "HQ"), C("1209", 64980));

            // Monthly VAT return: pay output VAT net of input VAT for the previous month
            if (month > start)
            {
                var prev = month.AddMonths(-1);
                var prevEnd = prev.AddMonths(1).AddDays(-1);
                var outVat = await _db.LedgerEntries.Where(l => l.AccountId == _acc["2003"] && l.EntryDate >= prev && l.EntryDate <= prevEnd).SumAsync(l => l.Credit - l.Debit);
                var inVat = await _db.LedgerEntries.Where(l => l.AccountId == _acc["1050"] && l.EntryDate >= prev && l.EntryDate <= prevEnd).SumAsync(l => l.Debit - l.Credit);
                if (outVat > inVat && inVat >= 0)
                    await Post(VoucherType.Payment, Day(14), $"VAT return (Mushak 9.1) for {prev:MMMM yyyy}", $"TR-{prev:yyMM}",
                        D("2003", outVat), C("1050", inVat), C("1010", outVat - inVat));
            }

            if (month.Month % 3 == 0)
                await Post(VoucherType.Payment, Day(20), "Quarterly loan principal repayment", "LN-7781", D("2201", 125000), C("1011", 125000));
        }

        await SeedIrregularitiesAsync(today);
        await ResyncNumbersAsync();
        await SeedWorkflowStatesAsync(today);
        await SeedBankStatementAsync(today);

        // Close every fiscal year except the current one
        var toClose = await _db.FiscalYears.Where(f => f.EndDate < today).OrderBy(f => f.StartDate).ToListAsync();
        foreach (var fy in toClose)
        {
            var r = await _fiscal.CloseAsync(fy.Id);
            if (!r.Ok) _log.LogWarning("Could not close {Fy}: {Error}", fy.Name, r.Error);
        }
    }

    /// <summary>Planted cases that the forensic module should surface.</summary>
    private async Task SeedIrregularitiesAsync(DateTime today)
    {
        var anchor = today.AddDays(-75);
        while (anchor.DayOfWeek != DayOfWeek.Thursday) anchor = anchor.AddDays(-1);

        // 1. Large round consultancy payment, entered late on a Friday night and self-approved
        var friday = anchor.AddDays(1).AddHours(22).AddMinutes(41);
        await PostAs(VoucherType.Payment, anchor, "Consultancy", "CHQ-109977", _farhana, _farhana, friday,
            D("5007", 1850000, "HQ"), C("1010", 1850000));

        // 2. Duplicate supplier payment three days apart
        var sup = _suppliers[0];
        var dupDate = today.AddDays(-50);
        await PostAs(VoucherType.Payment, dupDate, $"Payment to {sup.Name} — server maintenance", "CHQ-108812", null, null, null,
            D("2001", 236900, null, sup.Id), C("1010", 236900));
        await PostAs(VoucherType.Payment, dupDate.AddDays(3), $"Payment to {sup.Name} — server maintenance", "CHQ-108840", null, null, null,
            D("2001", 236900, null, sup.Id), C("1010", 236900));

        // 3. Just below the 500,000 review threshold
        await PostAs(VoucherType.Payment, today.AddDays(-40), "Advance to vendor for data-centre rack", null, _rafiq, _farhana, null,
            D("1040", 498500, "DEV"), C("1011", 498500));

        // 4. Backdated entry recorded two months after its voucher date, no narration
        var backDate = today.AddDays(-95);
        await PostAs(VoucherType.Payment, backDate, null, null, _rafiq, _admin, today.AddDays(-25).AddHours(11),
            D("5008", 87650, "SALES"), C("1001", 87650));

        // 5. Owner drawing self-approved by admin
        await PostAs(VoucherType.Payment, today.AddDays(-30), "Owner's personal withdrawal", null, _admin, _admin, null,
            D("3003", 300000), C("1010", 300000));

        // 6. A posting mistake that was later reversed
        var wrong = await PostAs(VoucherType.Payment, today.AddDays(-20), "Office supplies (entered against wrong account)", null, _farhana, _rafiq, null,
            D("5007", 18450, "HQ"), C("1001", 18450));
        if (wrong != null)
        {
            var r = await _accounting.ReverseAsync(wrong.Id, today.AddDays(-19), "Wrong expense head — should be Office Supplies");
            if (!r.Ok) _log.LogWarning("Reversal skipped: {Error}", r.Error);
            _db.ChangeTracker.Clear();
            await ResyncNumbersAsync();
            await PostAs(VoucherType.Payment, today.AddDays(-19), "Office supplies (corrected entry)", null, _farhana, _rafiq, null,
                D("5005", 18450, "HQ"), C("1001", 18450));
        }
    }

    private async Task SeedWorkflowStatesAsync(DateTime today)
    {
        async Task Add(VoucherStatus status, VoucherType type, DateTime date, string narration, AppUser creator, string? rejection, params VoucherDetail[] lines)
        {
            var v = new Voucher
            {
                VoucherNo = NextNo(type, date), VoucherType = type, VoucherDate = date, Narration = narration,
                Status = status, CreatedById = creator.Id, CreatedAt = DateTime.Now.AddHours(-_rnd.Next(2, 48)),
                SubmittedAt = status == VoucherStatus.Draft ? null : DateTime.Now.AddHours(-1), RejectionReason = rejection,
                Details = lines.ToList()
            };
            int n = 1;
            foreach (var l in v.Details) l.LineNo = n++;
            v.TotalAmount = v.Details.Sum(d => d.Debit);
            _db.Vouchers.Add(v);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        var cust = _customers[2];
        await Add(VoucherStatus.PendingApproval, VoucherType.Payment, today.AddDays(-1), "Annual Microsoft 365 renewal", _farhana, null,
            D("5004", 142300, "DEV"), C("1010", 142300));
        await Add(VoucherStatus.PendingApproval, VoucherType.Receipt, today, $"Advance received from {cust.Name}", _rafiq, null,
            D("1011", 350000), C("1020", 350000, null, cust.Id));
        await Add(VoucherStatus.PendingApproval, VoucherType.Journal, today, "Accrued audit fee for the year", _farhana, null,
            D("5007", 115000, "HQ"), C("2004", 115000));
        await Add(VoucherStatus.Draft, VoucherType.Payment, today, "Generator fuel (draft — awaiting receipt)", _farhana, null,
            D("5003", 6750, "HQ"), C("1001", 6750));
        await Add(VoucherStatus.Rejected, VoucherType.Payment, today.AddDays(-2), "Team dinner", _rafiq, "Attach the restaurant bill and split by cost centre.",
            D("5005", 24800, "HQ"), C("1001", 24800));
    }

    private async Task SeedBankStatementAsync(DateTime today)
    {
        var bankId = _acc["1010"];
        var from = today.AddDays(-35);
        var entries = await _db.LedgerEntries.AsNoTracking().Where(l => l.AccountId == bankId && l.EntryDate >= from).OrderBy(l => l.EntryDate).ToListAsync();
        foreach (var e in entries)
        {
            if (_rnd.NextDouble() < 0.18) continue; // outstanding cheques / deposits in transit
            _db.BankStatementLines.Add(new BankStatementLine
            {
                BankAccountId = bankId,
                TxnDate = e.EntryDate.AddDays(_rnd.Next(0, 3)),
                Description = (e.Debit > 0 ? "CR " : "DR ") + (e.Narration ?? "Transaction"),
                Reference = $"STMT-{e.Id}",
                Amount = e.Debit - e.Credit
            });
        }
        _db.BankStatementLines.Add(new BankStatementLine { BankAccountId = bankId, TxnDate = today.AddDays(-6), Description = "DR Excise duty on account balance", Reference = "BANK", Amount = -3000 });
        _db.BankStatementLines.Add(new BankStatementLine { BankAccountId = bankId, TxnDate = today.AddDays(-4), Description = "CR Interest credited", Reference = "BANK", Amount = 1240.55m });
        await _db.SaveChangesAsync();
    }

    // --------------------------------------------------------------- planning
    private async Task SeedPlanningAsync()
    {
        var today = DateTime.Today;
        var fy = await _db.FiscalYears.FirstOrDefaultAsync(f => f.StartDate <= today && f.EndDate >= today);
        if (fy != null)
        {
            var budget = new Budget { Name = $"Operating budget {fy.Name}", FiscalYearId = fy.Id, Notes = "Approved by the board." };
            var plan = new (string Code, decimal Amount)[]
            {
                ("4001", 14500000), ("4002", 9800000), ("5001", 9100000), ("5002", 1140000), ("5003", 190000),
                ("5004", 300000), ("5005", 60000), ("5006", 55000), ("5007", 600000), ("5008", 450000),
                ("5201", 2600000), ("5301", 10000), ("5302", 275000), ("5303", 780000)
            };
            foreach (var (code, amount) in plan) budget.Lines.Add(new BudgetLine { AccountId = _acc[code], Amount = amount });
            _db.Budgets.Add(budget);
        }

        var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
        var rent = new RecurringTemplate { Name = "Office rent", VoucherType = VoucherType.Payment, Narration = "Office rent", Frequency = RecurrenceFrequency.Monthly, NextRunDate = nextMonth.AddDays(2) };
        rent.Lines.Add(new RecurringTemplateLine { AccountId = _acc["5002"], Debit = 95000 });
        rent.Lines.Add(new RecurringTemplateLine { AccountId = _acc["1010"], Credit = 95000 });
        var hosting = new RecurringTemplate { Name = "Cloud hosting (AWS)", VoucherType = VoucherType.Payment, Narration = "Cloud hosting subscription", Frequency = RecurrenceFrequency.Monthly, NextRunDate = today.AddDays(-1) };
        hosting.Lines.Add(new RecurringTemplateLine { AccountId = _acc["5004"], Debit = 18640 });
        hosting.Lines.Add(new RecurringTemplateLine { AccountId = _acc["1011"], Credit = 18640 });
        var depr = new RecurringTemplate { Name = "Monthly depreciation", VoucherType = VoucherType.Journal, Narration = "Monthly depreciation (straight line)", Frequency = RecurrenceFrequency.Monthly, NextRunDate = nextMonth.AddDays(-1).AddMonths(1) };
        depr.Lines.Add(new RecurringTemplateLine { AccountId = _acc["5303"], Debit = 64980 });
        depr.Lines.Add(new RecurringTemplateLine { AccountId = _acc["1209"], Credit = 64980 });
        _db.RecurringTemplates.AddRange(rent, hosting, depr);
        await _db.SaveChangesAsync();
    }
}
