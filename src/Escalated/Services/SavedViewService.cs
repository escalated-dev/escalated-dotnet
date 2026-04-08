using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class SavedViewService
{
    private readonly EscalatedDbContext _db;

    public SavedViewService(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<SavedView> CreateAsync(string name, string filters, int? userId = null,
        bool isShared = false, string? sortBy = null, string? sortDir = null,
        CancellationToken ct = default)
    {
        var view = new SavedView
        {
            Name = name,
            Filters = filters,
            UserId = userId,
            IsShared = isShared,
            SortBy = sortBy,
            SortDir = sortDir,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.SavedViews.Add(view);
        await _db.SaveChangesAsync(ct);
        return view;
    }

    public async Task<List<SavedView>> GetForUserAsync(int userId, CancellationToken ct = default)
    {
        return await _db.SavedViews
            .Where(v => v.UserId == userId || v.IsShared)
            .OrderBy(v => v.Position)
            .ToListAsync(ct);
    }

    public async Task<SavedView?> UpdateAsync(int id, string? name = null, string? filters = null,
        bool? isShared = null, CancellationToken ct = default)
    {
        var view = await _db.SavedViews.FindAsync(new object[] { id }, ct);
        if (view == null) return null;

        if (name != null) view.Name = name;
        if (filters != null) view.Filters = filters;
        if (isShared.HasValue) view.IsShared = isShared.Value;
        view.UpdatedAt = DateTime.UtcNow;

        _db.SavedViews.Update(view);
        await _db.SaveChangesAsync(ct);
        return view;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var view = await _db.SavedViews.FindAsync(new object[] { id }, ct);
        if (view == null) return false;
        _db.SavedViews.Remove(view);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
