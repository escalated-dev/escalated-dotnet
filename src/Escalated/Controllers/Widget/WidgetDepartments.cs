using Escalated.Data;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Widget;

/// <summary>
/// The department a widget visitor picked. Empty means none; anything else
/// must be an active department, so a visitor cannot route work into a
/// department the widget does not offer.
/// </summary>
internal static class WidgetDepartments
{
    public const string Field = "department_id";
    public const string Invalid = "The selected department is invalid.";

    public static async Task<bool> IsAllowedAsync(EscalatedDbContext db, int? departmentId, CancellationToken ct = default) =>
        departmentId is null || await db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct);
}
