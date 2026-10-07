namespace TroyeeLedger.Models.Entities;

/// <summary>
/// An immutable posting produced when a voucher is approved. All financial
/// statements are computed live from these rows.
/// </summary>
public class LedgerEntry
{
    public long Id { get; set; }

    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public int VoucherDetailId { get; set; }
    public VoucherDetail? VoucherDetail { get; set; }

    public int AccountId { get; set; }
    public ChartOfAccount? Account { get; set; }

    public DateTime EntryDate { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public string? Narration { get; set; }

    public int? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }

    public int? PartyId { get; set; }
    public Party? Party { get; set; }

    public DateTime PostedAt { get; set; } = DateTime.Now;

    // Bank reconciliation
    public bool IsReconciled { get; set; }
    public DateTime? ReconciledAt { get; set; }
}
