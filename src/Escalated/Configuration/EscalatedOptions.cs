namespace Escalated.Configuration;

public class EscalatedOptions
{
    public const string SectionName = "Escalated";

    /// <summary>
    /// Route prefix for all Escalated endpoints (default: "support").
    /// </summary>
    public string RoutePrefix { get; set; } = "support";

    /// <summary>
    /// Ticket reference prefix (default: "ESC").
    /// </summary>
    public string TicketReferencePrefix { get; set; } = "ESC";

    /// <summary>
    /// Default ticket priority for new tickets.
    /// </summary>
    public string DefaultPriority { get; set; } = "medium";

    /// <summary>
    /// Whether customers can close their own tickets.
    /// </summary>
    public bool AllowCustomerClose { get; set; } = true;

    /// <summary>
    /// Number of days after resolution before auto-close (0 = disabled).
    /// </summary>
    public int AutoCloseResolvedAfterDays { get; set; } = 7;

    /// <summary>
    /// SLA configuration.
    /// </summary>
    public SlaOptions Sla { get; set; } = new();

    /// <summary>
    /// Attachment configuration.
    /// </summary>
    public AttachmentOptions Attachments { get; set; } = new();

    /// <summary>
    /// Knowledge base configuration.
    /// </summary>
    public KnowledgeBaseOptions KnowledgeBase { get; set; } = new();

    /// <summary>
    /// Whether to enable SignalR hubs for real-time updates.
    /// </summary>
    public bool EnableRealTime { get; set; }

    /// <summary>
    /// CSAT configuration.
    /// </summary>
    public CsatOptions Csat { get; set; } = new();

    /// <summary>
    /// Outbound + inbound email config.
    /// </summary>
    public EmailOptions Email { get; set; } = new();
}

/// <summary>
/// Email threading config. <see cref="Domain"/> is the right-hand side
/// of RFC 5322 Message-IDs and signed Reply-To addresses.
/// <see cref="InboundSecret"/> is the HMAC key used to sign Reply-To
/// addresses so inbound provider webhooks can verify ticket identity
/// without trusting the mail client's threading headers. An empty
/// <see cref="InboundSecret"/> disables Reply-To signing — basic
/// threading still works via Message-ID + In-Reply-To.
/// </summary>
public class EmailOptions
{
    public string Domain { get; set; } = "localhost";
    public string InboundSecret { get; set; } = string.Empty;
}

public class SlaOptions
{
    public bool Enabled { get; set; } = true;
    public bool BusinessHoursOnly { get; set; }
    public BusinessHoursConfig BusinessHours { get; set; } = new();
}

public class BusinessHoursConfig
{
    public string Start { get; set; } = "09:00";
    public string End { get; set; } = "17:00";
    public string Timezone { get; set; } = "UTC";
    public int[] Days { get; set; } = { 1, 2, 3, 4, 5 };
}

public class AttachmentOptions
{
    public int MaxSizeKb { get; set; } = 10240; // 10 MB
    public int MaxPerReply { get; set; } = 5;
    public string Disk { get; set; } = "local";
}

public class KnowledgeBaseOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowFeedback { get; set; } = true;
}

public class CsatOptions
{
    public bool Enabled { get; set; } = true;
    public bool SendOnResolved { get; set; } = true;
}
