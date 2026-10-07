using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.Services;

public interface IRecurringService
{
    Task<List<string>> RunDueAsync(DateTime today, int? templateId = null);
    Task<int> CountDueAsync(DateTime today);
    DateTime Advance(DateTime date, RecurrenceFrequency f);
}

/// <summary>Generates vouchers from standing templates (rent, salaries, subscriptions…).</summary>
public class RecurringService : IRecurringService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly IAuditService _audit;

    public RecurringService(AppDbContext db, IAccountingService accounting, IAuditService audit)
    {
        _db = db;
        _accounting = accounting;
        _audit = audit;
    }

    public DateTime Advance(DateTime date, RecurrenceFrequency f) => f switch
    {
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Monthly => date.AddMonths(1),
        RecurrenceFrequency.Quarterly => date.AddMonths(3),
        _ => date.AddYears(1)
    };

    public Task<int> CountDueAsync(DateTime today) =>
        _db.RecurringTemplates.CountAsync(t => t.IsActive && t.NextRunDate <= today && (t.EndDate == null || t.NextRunDate <= t.EndDate));

    public async Task<List<string>> RunDueAsync(DateTime today, int? templateId = null)
    {
        var messages = new List<string>();
        var q = _db.RecurringTemplates.Include(t => t.Lines)
            .Where(t => t.IsActive && t.NextRunDate <= today && (t.EndDate == null || t.NextRunDate <= t.EndDate));
        if (templateId.HasValue) q = q.Where(t => t.Id == templateId.Value);
        var templates = await q.ToListAsync();

        foreach (var t in templates)
        {
            int guard = 0;
            while (t.NextRunDate <= today && (t.EndDate == null || t.NextRunDate <= t.EndDate) && guard++ < 24)
            {
                var header = new Voucher
                {
                    VoucherType = t.VoucherType,
                    VoucherDate = t.NextRunDate,
                    Narration = $"{t.Narration ?? t.Name} ({t.NextRunDate:MMM yyyy})",
                    Reference = $"AUTO-{t.Id}",
                    RecurringTemplateId = t.Id
                };
                var lines = t.Lines.Select(l => new VoucherDetail
                {
                    AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, LineNarration = l.LineNarration
                }).ToList();

                var result = await _accounting.CreateAsync(header, lines, submit: true);
                if (!result.Ok)
                {
                    messages.Add($"{t.Name} on {t.NextRunDate:dd MMM yyyy}: {result.Error}");
                    break;
                }
                messages.Add($"{t.Name}: created {header.VoucherNo} for {t.NextRunDate:dd MMM yyyy}.");
                t.NextRunDate = Advance(t.NextRunDate, t.Frequency);
                t.LastGeneratedAt = DateTime.Now;
                t.GeneratedCount++;
                await _db.SaveChangesAsync();
            }
        }
        if (messages.Count > 0)
            await _audit.LogAsync("RunRecurring", "RecurringTemplate", templateId?.ToString(), string.Join(" | ", messages));
        return messages;
    }
}
