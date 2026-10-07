using System.ComponentModel.DataAnnotations;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Models.Entities;

public class Voucher
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    [Display(Name = "Voucher no")]
    public string VoucherNo { get; set; } = string.Empty;

    [Display(Name = "Voucher type")]
    public VoucherType VoucherType { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Voucher date")]
    public DateTime VoucherDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    public string? Narration { get; set; }

    [StringLength(60)]
    [Display(Name = "Reference / cheque no")]
    public string? Reference { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;

    public decimal TotalAmount { get; set; }

    public int CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? SubmittedAt { get; set; }

    public int? ApprovedById { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime? PostedAt { get; set; }

    [StringLength(300)]
    public string? RejectionReason { get; set; }

    /// <summary>Set on a reversal voucher: the voucher it cancels.</summary>
    public int? ReversalOfId { get; set; }
    public Voucher? ReversalOf { get; set; }

    /// <summary>Set on an original voucher once it has been reversed.</summary>
    public int? ReversedByVoucherId { get; set; }

    public bool IsSystemGenerated { get; set; }

    public int? RecurringTemplateId { get; set; }

    // ---- Tamper-evident hash chain ----
    public long? ChainIndex { get; set; }
    [StringLength(64)]
    public string? PreviousHash { get; set; }
    [StringLength(64)]
    public string? Hash { get; set; }

    public List<VoucherDetail> Details { get; set; } = new();
}

public class VoucherDetail
{
    public int Id { get; set; }

    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public int LineNo { get; set; }

    public int AccountId { get; set; }
    public ChartOfAccount? Account { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    [StringLength(250)]
    public string? LineNarration { get; set; }

    public int? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }

    public int? PartyId { get; set; }
    public Party? Party { get; set; }
}
