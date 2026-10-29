using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// One row of a plugin's <c>ctx.store</c> or <c>ctx.config</c>
/// (<c>escalated_plugin_store</c>). Keyed by the plugin's manifest name; config
/// is the row with collection and key <c>__config__</c>. Uninstalling a plugin
/// keeps its rows.
/// </summary>
public class PluginStoreRecord
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(191)]
    public string Plugin { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Collection { get; set; } = string.Empty;

    [MaxLength(191)]
    public string? Key { get; set; }

    /// <summary>The value as JSON text (<c>jsonb</c> on PostgreSQL).</summary>
    public string? Data { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
