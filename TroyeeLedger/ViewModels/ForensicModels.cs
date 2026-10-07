using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.ViewModels;

public class BenfordDigit
{
    public int Digit { get; set; }
    public int Count { get; set; }
    public double Observed { get; set; }
    public double Expected { get; set; }
    public double Z { get; set; }
    public bool Significant => Z > 1.96;
}

public class BenfordVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int N { get; set; }
    public List<BenfordDigit> Digits { get; set; } = new();
    public double ChiSquare { get; set; }
    public const double ChiCritical = 15.507; // df = 8, alpha = 0.05
    public bool PassesChiSquare => ChiSquare <= ChiCritical;
    public double Mad { get; set; }
    public string Conformity { get; set; } = "";
    public bool SmallSample => N < 100;
}

public class AnomalyReason
{
    public string Rule { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Points { get; set; }
}

public class AnomalyItem
{
    public int VoucherId { get; set; }
    public string VoucherNo { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public VoucherType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Narration { get; set; }
    public string CreatedBy { get; set; } = "";
    public int Score => Math.Min(100, Reasons.Sum(r => r.Points));
    public string Level => Score >= 50 ? "High" : Score >= 25 ? "Medium" : Score > 0 ? "Low" : "None";
    public List<AnomalyReason> Reasons { get; set; } = new();
}

public class AnomalyReportVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string MinLevel { get; set; } = "Low";
    public int Scanned { get; set; }
    public List<AnomalyItem> Items { get; set; } = new();
    public int CountHigh => Items.Count(i => i.Level == "High");
    public int CountMedium => Items.Count(i => i.Level == "Medium");
    public int CountLow => Items.Count(i => i.Level == "Low");
    public Dictionary<string, int> RuleCounts =>
        Items.SelectMany(i => i.Reasons).GroupBy(r => r.Rule).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
}

public class IntegrityIssue
{
    public string Area { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Message { get; set; } = "";
    public int? VoucherId { get; set; }
}

public class IntegrityReportVm
{
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public int VouchersChecked { get; set; }
    public int AuditRecordsChecked { get; set; }
    public int LedgerRowsChecked { get; set; }
    public bool VoucherChainOk { get; set; }
    public bool LedgerMatchesVouchers { get; set; }
    public bool LedgerBalanced { get; set; }
    public bool AuditChainOk { get; set; }
    public string? ChainHead { get; set; }
    public string? AuditHead { get; set; }
    public decimal LedgerDebit { get; set; }
    public decimal LedgerCredit { get; set; }
    public List<IntegrityIssue> Issues { get; set; } = new();
    public bool AllOk => VoucherChainOk && LedgerMatchesVouchers && LedgerBalanced && AuditChainOk;
}

public class SodUserStat
{
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public int Created { get; set; }
    public int Approved { get; set; }
    public int SelfApproved { get; set; }
}

public class SodVm
{
    public List<Voucher> Violations { get; set; } = new();
    public List<SodUserStat> Users { get; set; } = new();
}

public class ForensicDashboardVm
{
    public IntegrityReportVm Integrity { get; set; } = new();
    public BenfordVm Benford { get; set; } = new();
    public AnomalyReportVm Anomalies { get; set; } = new();
    public int SodViolations { get; set; }
}

public class RatioItem
{
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Value { get; set; }
    public string Kind { get; set; } = "x"; // x, pct, money, months
    public string Formula { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Status { get; set; } = "neutral"; // good, warn, bad, neutral
}

public class RatioVm
{
    public DateTime AsOf { get; set; }
    public DateTime PeriodFrom { get; set; }
    public List<RatioItem> Ratios { get; set; } = new();
    public Dictionary<string, decimal> Figures { get; set; } = new();
}

public class TrendVm
{
    public DateTime AsOf { get; set; }
    public List<MonthlyPoint> Months { get; set; } = new();
    public List<MonthlyPoint> Forecast { get; set; } = new();
    public double Slope { get; set; }
    public double Intercept { get; set; }
    public double RSquared { get; set; }
    public List<KeyValuePair<string, decimal>> ExpenseMix { get; set; } = new();
    public List<decimal> CashBalances { get; set; } = new();
}
