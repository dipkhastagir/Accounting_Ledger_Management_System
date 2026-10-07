using System.ComponentModel.DataAnnotations;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Models.Entities;

public class Budget
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Fiscal year")]
    public int FiscalYearId { get; set; }
    public FiscalYear? FiscalYear { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<BudgetLine> Lines { get; set; } = new();
}

public class BudgetLine
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public Budget? Budget { get; set; }

    public int AccountId { get; set; }
    public ChartOfAccount? Account { get; set; }

    public decimal Amount { get; set; }
}

public class RecurringTemplate
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Voucher type")]
    public VoucherType VoucherType { get; set; } = VoucherType.Payment;

    [StringLength(300)]
    public string? Narration { get; set; }

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    [DataType(DataType.Date), Display(Name = "Next run date")]
    public DateTime NextRunDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date), Display(Name = "End date (optional)")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public DateTime? LastGeneratedAt { get; set; }
    public int GeneratedCount { get; set; }

    public List<RecurringTemplateLine> Lines { get; set; } = new();
}

public class RecurringTemplateLine
{
    public int Id { get; set; }
    public int RecurringTemplateId { get; set; }
    public RecurringTemplate? RecurringTemplate { get; set; }

    public int AccountId { get; set; }
    public ChartOfAccount? Account { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    [StringLength(250)]
    public string? LineNarration { get; set; }
}

public class BankStatementLine
{
    public int Id { get; set; }

    [Display(Name = "Bank account")]
    public int BankAccountId { get; set; }
    public ChartOfAccount? BankAccount { get; set; }

    [DataType(DataType.Date), Display(Name = "Transaction date")]
    public DateTime TxnDate { get; set; }

    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;

    [StringLength(60)]
    public string? Reference { get; set; }

    /// <summary>Positive = deposit (bank debit), negative = withdrawal.</summary>
    public decimal Amount { get; set; }

    public bool IsMatched { get; set; }
    public long? MatchedLedgerEntryId { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.Now;
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }

    [StringLength(50)]
    public string UserName { get; set; } = "system";

    [StringLength(60)]
    public string Action { get; set; } = string.Empty;

    [StringLength(60)]
    public string EntityName { get; set; } = string.Empty;

    [StringLength(40)]
    public string? EntityId { get; set; }

    public string? Details { get; set; }

    [StringLength(60)]
    public string? IpAddress { get; set; }

    [StringLength(64)]
    public string PreviousHash { get; set; } = string.Empty;

    [StringLength(64)]
    public string Hash { get; set; } = string.Empty;
}
