using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Models.Newsletter;

[Table("escalated_newsletters")]
[Index(nameof(Status), nameof(ScheduledAt))]
public class Newsletter
{
    public static readonly string[] Statuses =
        { "draft", "scheduled", "sending", "sent", "paused", "failed" };

    [Key]
    public int Id { get; set; }

    [Required, MaxLength(998)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string FromEmail { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? FromName { get; set; }

    [MaxLength(320)]
    public string? ReplyTo { get; set; }

    [Required]
    public int TargetListId { get; set; }

    public int? TemplateId { get; set; }

    [MaxLength(64)]
    public string? Theme { get; set; }

    public string? BodyMarkdown { get; set; }

    [Required, MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? SentBy { get; set; }

    public int SummaryTotal { get; set; }
    public int SummarySent { get; set; }
    public int SummaryOpened { get; set; }
    public int SummaryClicked { get; set; }
    public int SummaryBounced { get; set; }
    public int SummaryComplained { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TargetListId))]
    public NewsletterList? TargetList { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public NewsletterTemplate? Template { get; set; }
}
