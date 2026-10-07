using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Helpers;

public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal v) => v.ToString("#,##0.00", Inv);

    /// <summary>Blank for zero — ledgers traditionally leave empty columns blank.</summary>
    public static string MoneyOrBlank(decimal v) => v == 0 ? "" : Money(v);

    /// <summary>Negative numbers shown in brackets, the accounting convention.</summary>
    public static string Signed(decimal v) => v < 0 ? $"({Money(-v)})" : Money(v);

    public static string DrCr(decimal debitMinusCredit) =>
        debitMinusCredit == 0 ? "0.00" : $"{Money(Math.Abs(debitMinusCredit))} {(debitMinusCredit > 0 ? "Dr" : "Cr")}";

    public static string Date(DateTime d) => d.ToString("dd MMM yyyy", Inv);
    public static string Date(DateTime? d) => d.HasValue ? Date(d.Value) : "—";
    public static string DateTime(DateTime d) => d.ToString("dd MMM yyyy, HH:mm", Inv);
    public static string DateTime(DateTime? d) => d.HasValue ? DateTime(d.Value) : "—";
    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

    public static string Pct(double v, int decimals = 1) => (v * 100).ToString("F" + decimals, Inv) + "%";
    public static string Pct(decimal v, int decimals = 1) => Pct((double)v, decimals);

    public static string Display(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        var attr = member?.GetCustomAttribute<DisplayAttribute>();
        return attr?.GetName() ?? value.ToString();
    }

    public static string StatusCss(VoucherStatus s) => s switch
    {
        VoucherStatus.Draft => "st-draft",
        VoucherStatus.PendingApproval => "st-pending",
        VoucherStatus.Posted => "st-posted",
        VoucherStatus.Rejected => "st-rejected",
        VoucherStatus.Reversed => "st-reversed",
        _ => ""
    };

    public static string TypeCode(VoucherType t) => t switch
    {
        VoucherType.Payment => "PV",
        VoucherType.Receipt => "RV",
        VoucherType.Journal => "JV",
        VoucherType.Contra => "CV",
        _ => "XV"
    };

    public static string ShortHash(string? h) => string.IsNullOrEmpty(h) ? "—" : h[..Math.Min(12, h.Length)].ToLowerInvariant() + "…";
}
