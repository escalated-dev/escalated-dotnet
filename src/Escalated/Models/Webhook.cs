using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Escalated.Events;

namespace Escalated.Models;

public class Webhook
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Signs deliveries. Never serialized: pages and API clients see
    /// <see cref="HasSecret"/> instead.
    /// </summary>
    [JsonIgnore]
    [MaxLength(255)]
    public string? Secret { get; set; }

    /// <summary>Whether a signing secret is stored, which is all a page or API client learns of it.</summary>
    [NotMapped]
    [JsonPropertyName("has_secret")]
    public bool HasSecret => !string.IsNullOrEmpty(Secret);

    [MaxLength(100)]
    public string? Description { get; set; }

    /// <summary>
    /// JSON array of event names this webhook subscribes to
    /// </summary>
    public string Events { get; set; } = "[]";

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();

    public bool SubscribedTo(string eventName)
    {
        try
        {
            // Stored names are read as canonical (ADR 2026-10-21), so a subscription
            // saved under a legacy name such as `ticket.tagged` keeps receiving it.
            var events = JsonSerializer.Deserialize<List<string>>(Events);
            var wanted = EventNames.Canonicalize(eventName);
            return events != null && events.Any(e => e == "*" || EventNames.Canonicalize(e) == wanted);
        }
        catch
        {
            return false;
        }
    }
}
