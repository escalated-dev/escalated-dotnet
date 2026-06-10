using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services.Newsletter;

public class BounceSuppressionStore
{
    private const string Key = "newsletter.suppressed_emails";
    private readonly EscalatedDbContext _db;

    public BounceSuppressionStore(EscalatedDbContext db)
    {
        _db = db;
    }

    public Task MarkBouncedAsync(string email, CancellationToken ct = default) => MarkAsync(email, ct);

    public Task MarkComplainedAsync(string email, CancellationToken ct = default) => MarkAsync(email, ct);

    public async Task<bool> IsBouncedAsync(string email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        return (await LoadAsync(ct)).Contains(normalized);
    }

    public async Task<IReadOnlyList<string>> FilterSendableAsync(IEnumerable<string> emails, CancellationToken ct = default)
    {
        var suppressed = await LoadAsync(ct);
        return emails.Where(e => !suppressed.Contains(Normalize(e))).ToList();
    }

    private async Task MarkAsync(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var list = await LoadAsync(ct);
        if (!list.Add(normalized))
            return;

        var row = await _db.Settings.SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            row = new EscalatedSettings
            {
                Key = Key,
                Type = "json",
                Group = "newsletter",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.Settings.Add(row);
        }

        row.Value = JsonSerializer.Serialize(list.OrderBy(e => e));
        row.Type = "json";
        row.Group = "newsletter";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<HashSet<string>> LoadAsync(CancellationToken ct)
    {
        var value = await _db.Settings
            .Where(s => s.Key == Key)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var decoded = JsonSerializer.Deserialize<List<string>>(value) ?? new List<string>();
            return decoded.Select(Normalize)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string email) => Contact.NormalizeEmail(email);
}
