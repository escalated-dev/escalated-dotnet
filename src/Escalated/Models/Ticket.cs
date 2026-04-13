using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Escalated.Enums;

namespace Escalated.Models;

public class Ticket
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Reference { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Subject { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public TicketStatus Status { get; set; } = TicketStatus.Open;

    [Required]
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [MaxLength(50)]
    public string? TicketType { get; set; }

    // Polymorphic requester
    [MaxLength(255)]
    public string? RequesterType { get; set; }
    public int? RequesterId { get; set; }

    public int? AssignedTo { get; set; }
    public int? DepartmentId { get; set; }
    public int? SlaPolicyId { get; set; }
    public int? MergedIntoId { get; set; }

    // Guest fields
    [MaxLength(255)]
    public string? GuestName { get; set; }
    [MaxLength(255)]
    public string? GuestEmail { get; set; }
    [MaxLength(255)]
    public string? GuestToken { get; set; }

    // SLA fields
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public bool SlaFirstResponseBreached { get; set; }
    public bool SlaResolutionBreached { get; set; }

    // Timestamps
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? SnoozedUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // Metadata
    public string? Metadata { get; set; }

    // Navigation properties
    [ForeignKey(nameof(DepartmentId))]
    public Department? Department { get; set; }

    [ForeignKey(nameof(SlaPolicyId))]
    public SlaPolicy? SlaPolicy { get; set; }

    [ForeignKey(nameof(MergedIntoId))]
    public Ticket? MergedIntoTicket { get; set; }

    public ICollection<Reply> Replies { get; set; } = new List<Reply>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<TicketActivity> Activities { get; set; } = new List<TicketActivity>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<SideConversation> SideConversations { get; set; } = new List<SideConversation>();
    public ICollection<TicketLink> LinksAsParent { get; set; } = new List<TicketLink>();
    public ICollection<TicketLink> LinksAsChild { get; set; } = new List<TicketLink>();
    public SatisfactionRating? SatisfactionRating { get; set; }
    public ICollection<CustomFieldValue> CustomFieldValues { get; set; } = new List<CustomFieldValue>();
    public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();

    public static readonly string[] ValidTypes = { "question", "problem", "incident", "task" };

    public bool IsGuest => RequesterType == null && GuestToken != null;

    // Computed properties expected by the frontend.

    /// <summary>Guest name, or a name set by the controller when the requester is resolved.</summary>
    [NotMapped]
    [JsonPropertyName("requester_name")]
    public string? RequesterName { get; set; }

    /// <summary>Guest email, or an email set by the controller when the requester is resolved.</summary>
    [NotMapped]
    [JsonPropertyName("requester_email")]
    public string? RequesterEmail { get; set; }

    /// <summary>Timestamp of the most recent reply (computed from the Replies collection).</summary>
    [NotMapped]
    [JsonPropertyName("last_reply_at")]
    public DateTime? LastReplyAt => Replies?.MaxBy(r => r.CreatedAt)?.CreatedAt;

    /// <summary>Display name of the most recent reply's author. Set by controller or populated manually.</summary>
    [NotMapped]
    [JsonPropertyName("last_reply_author")]
    public string? LastReplyAuthor { get; set; }

    /// <summary>True when an active (non-ended) chat session is associated with this ticket.</summary>
    [NotMapped]
    [JsonPropertyName("is_live_chat")]
    public bool IsLiveChat => ChatSessions?.Any(cs => cs.Status != "ended") == true;

    /// <summary>True when the ticket is snoozed until a future time.</summary>
    [NotMapped]
    [JsonPropertyName("is_snoozed")]
    public bool IsSnoozed => SnoozedUntil.HasValue && SnoozedUntil.Value > DateTime.UtcNow;

    public bool IsOpen() => Status.IsOpen();

    public string GenerateReference(string prefix = "ESC")
    {
        return $"{prefix}-{Id:D5}";
    }

    /// <summary>
    /// Populates the settable computed fields from the ticket's own data.
    /// Call after loading navigation properties.
    /// </summary>
    public void PopulateComputedFields()
    {
        // Requester defaults to guest fields when no external requester is resolved.
        RequesterName ??= GuestName;
        RequesterEmail ??= GuestEmail;

        // Last reply author from the latest reply (if loaded).
        if (LastReplyAuthor == null && Replies?.Count > 0)
        {
            var latest = Replies.MaxBy(r => r.CreatedAt);
            if (latest != null)
            {
                // AuthorType may indicate the source; fall back to a generic label.
                LastReplyAuthor = latest.AuthorType ?? "Agent";
            }
        }
    }
}
