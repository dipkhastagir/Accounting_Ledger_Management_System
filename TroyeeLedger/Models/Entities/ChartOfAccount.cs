using System.ComponentModel.DataAnnotations;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Models.Entities;

/// <summary>
/// A node in the multi-level Chart of Accounts. Group accounts aggregate their
/// children and can never receive postings directly; leaf accounts hold the ledger.
/// </summary>
public class ChartOfAccount
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "Account code")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Account name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Account type")]
    public AccountType AccountType { get; set; }

    [Display(Name = "Parent group")]
    public int? ParentId { get; set; }
    public ChartOfAccount? Parent { get; set; }
    public List<ChartOfAccount> Children { get; set; } = new();

    [Display(Name = "Group account (no direct posting)")]
    public bool IsGroup { get; set; }

    [Display(Name = "Cash or bank account")]
    public bool IsCashOrBank { get; set; }

    [Display(Name = "Current (short-term)")]
    public bool IsCurrent { get; set; } = true;

    [Display(Name = "Cash-flow classification")]
    public CashFlowCategory CashFlowCategory { get; set; } = CashFlowCategory.Auto;

    [StringLength(300)]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Asset and Expense accounts carry a debit balance by nature.</summary>
    public bool IsDebitNature => AccountType is AccountType.Asset or AccountType.Expense;
}
