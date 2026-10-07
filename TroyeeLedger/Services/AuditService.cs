using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;

namespace TroyeeLedger.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string? entityId, string? details = null);
}

/// <summary>
/// Append-only audit trail. Every record carries the SHA-256 of its predecessor,
/// so deleting or editing any historic row breaks the chain and is detectable.
/// </summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public AuditService(AppDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task LogAsync(string action, string entityName, string? entityId, string? details = null)
    {
        var previous = await _db.AuditLogs.OrderByDescending(a => a.Id).Select(a => a.Hash).FirstOrDefaultAsync()
                       ?? HashUtil.Genesis;
        var log = new AuditLog
        {
            Timestamp = DateTime.Now,
            UserName = _user.UserName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            IpAddress = _user.IpAddress,
            PreviousHash = previous
        };
        log.Hash = HashUtil.AuditHash(log, previous);
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
