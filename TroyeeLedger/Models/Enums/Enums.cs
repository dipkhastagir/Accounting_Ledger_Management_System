using System.ComponentModel.DataAnnotations;

namespace TroyeeLedger.Models.Enums;

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5
}

public enum VoucherType
{
    Payment = 1,
    Receipt = 2,
    Journal = 3,
    Contra = 4
}

public enum VoucherStatus
{
    Draft = 0,
    [Display(Name = "Pending approval")]
    PendingApproval = 1,
    Posted = 2,
    Rejected = 3,
    Reversed = 4
}

public enum CashFlowCategory
{
    [Display(Name = "Automatic")]
    Auto = 0,
    Operating = 1,
    Investing = 2,
    Financing = 3
}

public enum PartyType
{
    Customer = 1,
    Supplier = 2,
    Employee = 3,
    Other = 4
}

public enum TaxKind
{
    [Display(Name = "Input VAT (on purchases)")]
    Input = 1,
    [Display(Name = "Output VAT (on sales)")]
    Output = 2
}

public enum RecurrenceFrequency
{
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
    public const string Auditor = "Auditor";
    public const string Viewer = "Viewer";

    public const string Editors = Admin + "," + Accountant;
    public const string Approvers = Admin + "," + Accountant;
    public const string Reviewers = Admin + "," + Auditor;

    public static readonly string[] All = { Admin, Accountant, Auditor, Viewer };
}
