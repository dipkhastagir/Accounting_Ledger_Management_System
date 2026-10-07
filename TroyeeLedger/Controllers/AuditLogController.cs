using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Helpers;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Controllers;

[Authorize(Roles = Roles.Reviewers)]
public class AuditLogController : BaseController
{
    private readonly AppDbContext _db;
    public AuditLogController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search, string? action, string? user, DateTime? from, DateTime? to, string? export, int page = 1)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.EntityName.Contains(search) || (a.EntityId != null && a.EntityId.Contains(search)) || (a.Details != null && a.Details.Contains(search)));
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.UserName == user);
        if (from.HasValue) q = q.Where(a => a.Timestamp >= from.Value.Date);
        if (to.HasValue) { var end = to.Value.Date.AddDays(1); q = q.Where(a => a.Timestamp < end); }
        q = q.OrderByDescending(a => a.Id);

        if (IsCsv(export))
        {
            var csv = new CsvBuilder().Row("Id", "Timestamp", "User", "Action", "Entity", "Entity id", "Details", "IP", "Previous hash", "Hash");
            foreach (var a in await q.ToListAsync())
                csv.Row(a.Id, a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), a.UserName, a.Action, a.EntityName, a.EntityId, a.Details, a.IpAddress, a.PreviousHash, a.Hash);
            return Csv(csv, "audit-trail");
        }

        const int size = 40;
        page = Math.Max(1, page);
        ViewBag.Actions = await _db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        ViewBag.Users = await _db.AuditLogs.Select(a => a.UserName).Distinct().OrderBy(a => a).ToListAsync();
        ViewBag.Search = search; ViewBag.Action = action; ViewBag.User = user; ViewBag.From = from; ViewBag.To = to;
        return View(new PagedResult<AuditLog>
        {
            Page = page, PageSize = size, TotalCount = await q.CountAsync(),
            Items = await q.Skip((page - 1) * size).Take(size).ToListAsync()
        });
    }

    public async Task<IActionResult> Details(long id)
    {
        var log = await _db.AuditLogs.FindAsync(id);
        if (log == null) return NotFound();
        ViewBag.Recomputed = HashUtil.AuditHash(log, log.PreviousHash);
        var prev = await _db.AuditLogs.Where(a => a.Id < id).OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        ViewBag.PrevId = prev?.Id;
        ViewBag.PrevHash = prev?.Hash;
        return View(log);
    }
}
