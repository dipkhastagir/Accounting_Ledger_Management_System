using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Entities;

namespace TroyeeLedger.Services;

public interface ISettingsService
{
    Task<CompanySetting> GetAsync();
}

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private CompanySetting? _cached;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<CompanySetting> GetAsync()
    {
        if (_cached != null) return _cached;
        _cached = await _db.CompanySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (_cached == null)
        {
            _cached = new CompanySetting();
            _db.CompanySettings.Add(_cached);
            await _db.SaveChangesAsync();
        }
        return _cached;
    }
}
