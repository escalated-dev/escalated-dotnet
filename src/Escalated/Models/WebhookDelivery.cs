using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class WebhookDelivery
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WebhookId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Event { get; set; } = string.Empty;

    public string? Payload { get; set; } // JSON

    public int? ResponseCode { get; set; }

    public string? ResponseBody { get; set; }

    public int Attempts { get; set; } = 1;

    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(WebhookId))]
    public Webhook? Webhook { get; set; }
}
