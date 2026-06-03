using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Models.Newsletter;

[Table("escalated_newsletter_deliveries")]
[Index(nameof(NewsletterId), nameof(Status))]
[Index(nameof(Status), nameof(ClaimedAt))]
[Index(nameof(TrackingToken), IsUnique = true)]
public class NewsletterDelivery
{
    public static readonly string[] Statuses =
        { "pending", "queued", "sent", "bounced", "complained", "suppressed", "failed" };

    [Key]
    public long Id { get; set; }

    [Required]
    public int NewsletterId { get; set; }

    [Required]
    public int ContactId { get; set; }

    [Required, MaxLength(320)]
    public string EmailAtSend { get; set; } = string.Empty;

    [Required, MaxLength(16)]
    public string Status { get; set; } = "pending";

    [Required, MaxLength(40)]
    public string TrackingToken { get; set; } = string.Empty;

    public DateTime? SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? LastClickedAt { get; set; }
    public int ClicksCount { get; set; }
    public string? BounceReason { get; set; }
    public string? FailureReason { get; set; }
    public short AttemptCount { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public bool IsTest { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(NewsletterId))]
    public Newsletter? Newsletter { get; set; }

    [ForeignKey(nameof(ContactId))]
    public Contact? Contact { get; set; }
}
