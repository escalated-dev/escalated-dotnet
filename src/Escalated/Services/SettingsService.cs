using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class SettingsService
{
    private readonly EscalatedDbContext _db;

    public SettingsService(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string key, string? defaultValue = null,
        CancellationToken ct = default)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value ?? defaultValue;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting == null)
        {
            setting = new EscalatedSettings
            {
                Key = key,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
            _db.Settings.Update(setting);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, string?>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Settings
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
    }
}
