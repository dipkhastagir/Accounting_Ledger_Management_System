using System.ComponentModel.DataAnnotations;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Models.Entities;

public class FiscalYear
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    [DataType(DataType.Date), Display(Name = "Start date")]
    public DateTime StartDate { get; set; }

    [DataType(DataType.Date), Display(Name = "End date")]
    public DateTime EndDate { get; set; }

    [Display(Name = "Closed")]
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    [StringLength(50)]
    public string? ClosedBy { get; set; }
    public int? ClosingVoucherId { get; set; }
}

public class CostCenter
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class Party
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Party type")]
    public PartyType PartyType { get; set; } = PartyType.Customer;

    [StringLength(30)]
    public string? Phone { get; set; }

    [EmailAddress, StringLength(150)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(40), Display(Name = "BIN / TIN")]
    public string? TaxId { get; set; }

    [Display(Name = "Credit limit")]
    public decimal CreditLimit { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class TaxRate
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100), Display(Name = "Rate (%)")]
    public decimal RatePercent { get; set; }

    public TaxKind Kind { get; set; } = TaxKind.Output;

    [Display(Name = "Tax ledger account")]
    public int AccountId { get; set; }
    public ChartOfAccount? Account { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class CompanySetting
{
    public int Id { get; set; }

    [Required, StringLength(150), Display(Name = "Company name")]
    public string CompanyName { get; set; } = "Best Business Bond Ltd. (3BL)";

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(40)]
    public string? Phone { get; set; }

    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(40), Display(Name = "Company BIN")]
    public string? TaxId { get; set; }

    [Required, StringLength(5), Display(Name = "Currency symbol")]
    public string CurrencySymbol { get; set; } = "Tk";

    [Display(Name = "Require approval before posting (maker–checker)")]
    public bool RequireApproval { get; set; } = true;

    [Display(Name = "Block self-approval (segregation of duties)")]
    public bool EnforceSegregationOfDuties { get; set; } = true;

    [Range(1.0, 10.0), Display(Name = "Outlier z-score threshold")]
    public double AnomalyZThreshold { get; set; } = 3.0;

    [Display(Name = "Large transaction threshold")]
    public decimal LargeAmountThreshold { get; set; } = 500000m;

    [StringLength(60), Display(Name = "Weekend days (comma separated)")]
    public string WeekendDays { get; set; } = "Friday,Saturday";

    [Range(1, 365), Display(Name = "Backdating tolerance (days)")]
    public int BackdateToleranceDays { get; set; } = 30;
}
