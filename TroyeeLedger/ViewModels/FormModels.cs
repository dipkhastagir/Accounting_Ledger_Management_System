using System.ComponentModel.DataAnnotations;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.ViewModels;

public class LoginVm
{
    [Required(ErrorMessage = "Enter your username")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Enter your password"), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordVm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "Use at least 8 characters"), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match"), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class UserFormVm
{
    public int Id { get; set; }

    [Required, StringLength(50), RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Letters, numbers, dot, dash and underscore only")]
    public string Username { get; set; } = "";

    [Required, StringLength(120), Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    public string Role { get; set; } = Roles.Accountant;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [DataType(DataType.Password), MinLength(8, ErrorMessage = "Use at least 8 characters")]
    public string? Password { get; set; }
}

public class ResetPasswordVm
{
    public int Id { get; set; }
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "Use at least 8 characters"), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
}

public class VoucherLineVm
{
    public int AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? LineNarration { get; set; }
    public int? CostCenterId { get; set; }
    public int? PartyId { get; set; }
}

public class VoucherFormVm
{
    public int Id { get; set; }
    public string? VoucherNo { get; set; }

    [Display(Name = "Voucher type")]
    public VoucherType VoucherType { get; set; } = VoucherType.Payment;

    [DataType(DataType.Date), Display(Name = "Voucher date")]
    public DateTime VoucherDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    public string? Narration { get; set; }

    [StringLength(60), Display(Name = "Reference / cheque no")]
    public string? Reference { get; set; }

    public List<VoucherLineVm> Lines { get; set; } = new();

    public string? RejectionReason { get; set; }
}

public class VoucherIndexVm
{
    public Helpers.PagedResult<Voucher> Result { get; set; } = new();
    public string? Search { get; set; }
    public VoucherType? Type { get; set; }
    public VoucherStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class ReverseVm
{
    public int Id { get; set; }
    public string VoucherNo { get; set; } = "";
    public decimal Amount { get; set; }

    [DataType(DataType.Date), Display(Name = "Reversal date")]
    public DateTime ReversalDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Explain why this voucher is being reversed"), StringLength(250)]
    public string Reason { get; set; } = "";
}

public class BudgetFormVm
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Display(Name = "Fiscal year")]
    public int FiscalYearId { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }

    public List<BudgetLineVm> Lines { get; set; } = new();
}

public class BudgetLineVm
{
    public int AccountId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public decimal Amount { get; set; }
}

public class RecurringFormVm
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

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

    public List<VoucherLineVm> Lines { get; set; } = new();
}

public class StatementImportVm
{
    [Display(Name = "Bank account")]
    public int BankAccountId { get; set; }

    [Required(ErrorMessage = "Paste at least one line"), Display(Name = "Statement lines (CSV)")]
    public string CsvText { get; set; } = "";
}

public class ReconcileVm
{
    public ChartOfAccount Account { get; set; } = null!;
    public List<ChartOfAccount> BankAccounts { get; set; } = new();
    public DateTime AsOf { get; set; }
    public List<BankStatementLine> StatementLines { get; set; } = new();
    public List<LedgerEntry> BookEntries { get; set; } = new();
    public decimal BookBalance { get; set; }
    public decimal UnreconciledBook => BookEntries.Where(e => !e.IsReconciled).Sum(e => e.Debit - e.Credit);
    public decimal UnmatchedStatement => StatementLines.Where(s => !s.IsMatched).Sum(s => s.Amount);
    public decimal ReconciledBalance => BookBalance - UnreconciledBook;
}

public class DashboardVm
{
    public string CompanyName { get; set; } = "";
    public decimal CashAndBank { get; set; }
    public decimal Receivables { get; set; }
    public decimal Payables { get; set; }
    public decimal MonthIncome { get; set; }
    public decimal MonthExpense { get; set; }
    public decimal YearProfit { get; set; }
    public int PendingApprovals { get; set; }
    public int Drafts { get; set; }
    public int HighRisk { get; set; }
    public int DueRecurring { get; set; }
    public bool IntegrityOk { get; set; }
    public string? ChainHead { get; set; }
    public long ChainLength { get; set; }
    public List<MonthlyPoint> Months { get; set; } = new();
    public List<Voucher> Recent { get; set; } = new();
    public List<KeyValuePair<string, decimal>> TopExpenses { get; set; } = new();
    public List<AccountBalanceRow> CashAccounts { get; set; } = new();
}

public class PagerVm
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public static PagerVm From<T>(Helpers.PagedResult<T> r) => new() { Page = r.Page, TotalPages = r.TotalPages, TotalCount = r.TotalCount };
}

public class VoucherLineRowVm
{
    public VoucherLineVm Line { get; set; } = new();
    public string Index { get; set; } = "0";
    public bool WithDimensions { get; set; } = true;
}
