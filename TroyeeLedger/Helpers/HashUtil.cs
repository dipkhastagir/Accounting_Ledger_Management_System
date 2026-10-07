using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TroyeeLedger.Models.Entities;

namespace TroyeeLedger.Helpers;

/// <summary>
/// Builds the canonical payloads that are fingerprinted into the tamper-evident
/// hash chains (one for posted vouchers, one for the audit trail).
/// </summary>
public static class HashUtil
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Sha256(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    public static string VoucherPayload(Voucher v, IEnumerable<VoucherDetail> lines, string previousHash)
    {
        var sb = new StringBuilder();
        sb.Append(v.VoucherNo).Append('|')
          .Append(v.VoucherDate.ToString("yyyy-MM-dd", Inv)).Append('|')
          .Append((int)v.VoucherType).Append('|')
          .Append(v.TotalAmount.ToString("F2", Inv)).Append('|');
        foreach (var l in lines.OrderBy(x => x.LineNo).ThenBy(x => x.AccountId))
        {
            sb.Append(l.AccountId).Append(':')
              .Append(l.Debit.ToString("F2", Inv)).Append(':')
              .Append(l.Credit.ToString("F2", Inv)).Append(':')
              .Append(l.PartyId?.ToString(Inv) ?? "-").Append(':')
              .Append(l.CostCenterId?.ToString(Inv) ?? "-").Append(';');
        }
        sb.Append('|').Append(previousHash);
        return sb.ToString();
    }

    public static string VoucherHash(Voucher v, IEnumerable<VoucherDetail> lines, string previousHash) =>
        Sha256(VoucherPayload(v, lines, previousHash));

    public static string AuditHash(AuditLog a, string previousHash) =>
        Sha256(string.Join("|",
            a.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv),
            a.UserName, a.Action, a.EntityName, a.EntityId ?? "", a.Details ?? "", previousHash));
}
