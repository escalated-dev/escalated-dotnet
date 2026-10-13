using Escalated.Authorization;
using Escalated.Configuration;
using Microsoft.Extensions.Options;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Data;

public class EscalatedDbContext : DbContext
{
    private readonly EscalatedOptions packageOptions;
    public EscalatedDbContext(DbContextOptions<EscalatedDbContext> options, IOptions<EscalatedOptions>? packageOptions = null) : base(options)
    {
        this.packageOptions = packageOptions?.Value ?? new EscalatedOptions();
    }

    public async Task AssertTicketAccessAsync(int ticketId, CancellationToken ct = default)
    {
        if (RestrictTickets && !await Tickets.AsNoTracking().AnyAsync(t => t.Id == ticketId, ct))
            throw new TicketAccessDeniedException();
    }

    private async Task PrepareTicketChangesAsync(CancellationToken ct)
    {
        var entries = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var ids = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry.Entity is Ticket ticket)
            {
                if (entry.State != EntityState.Added) ids.Add(ticket.Id);
                else if (packageOptions.Accounts.Enabled && packageOptions.Accounts.Resolver is not null
                    && ticket.AccountType is null && ticket.AccountId is null && ticket.RequesterType is not null && ticket.RequesterId is not null)
                {
                    var account = await packageOptions.Accounts.Resolver(new TicketAccountReference("requester", ticket.RequesterType, ticket.RequesterId), ct);
                    if (account is { Type.Length: > 0 and <= 191, Id.Length: > 0 and <= 191 })
                    {
                        ticket.AccountType = account.Type;
                        ticket.AccountId = account.Id;
                    }
                }
            }
            else if (RestrictTickets)
            {
                // Resolve indirect parents through scoped queries as well, including original keys.
                foreach (var values in entry.State == EntityState.Added
                    ? new[] { entry.CurrentValues } : new[] { entry.CurrentValues, entry.OriginalValues })
                {
                    if (entry.Entity is Mention && values[nameof(Mention.ReplyId)] is int replyId && replyId > 0
                        && !await Replies.AsNoTracking().AnyAsync(r => r.Id == replyId, ct)) throw new TicketAccessDeniedException();
                    if (entry.Entity is SideConversationReply && values[nameof(SideConversationReply.SideConversationId)] is int sideId && sideId > 0
                        && !await SideConversations.AsNoTracking().AnyAsync(r => r.Id == sideId, ct)) throw new TicketAccessDeniedException();
                    if (entry.Entity is Attachment && values[nameof(Attachment.AttachableId)] is int attachmentParent && attachmentParent > 0)
                    {
                        var type = ((string)values[nameof(Attachment.AttachableType)]!).ToLowerInvariant();
                        if (type == "ticket") ids.Add(attachmentParent);
                        else if (type != "reply" || !await Replies.AsNoTracking().AnyAsync(r => r.Id == attachmentParent, ct)) throw new TicketAccessDeniedException();
                    }
                    if (entry.Entity is CustomFieldValue && string.Equals((string?)values[nameof(CustomFieldValue.EntityType)], "ticket", StringComparison.OrdinalIgnoreCase)
                        && values[nameof(CustomFieldValue.EntityId)] is int entityId && entityId > 0) ids.Add(entityId);
                }
                // Include both old and new foreign keys, so moving a child cannot evade the check.
                foreach (var property in entry.Properties.Where(p => p.Metadata.Name is "TicketId" or "ParentTicketId" or "ChildTicketId"))
                {
                    if (property.CurrentValue is int id && id > 0) ids.Add(id);
                    if (entry.State != EntityState.Added && property.OriginalValue is int old && old > 0) ids.Add(old);
                }
            }
        }
        if (RestrictTickets)
            foreach (var id in ids) await AssertTicketAccessAsync(id, ct);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await PrepareTicketChangesAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareTicketChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public DbSet<DepartmentAgent> DepartmentAgents => Set<DepartmentAgent>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<Mention> Mentions => Set<Mention>();
    public DbSet<TicketFollower> TicketFollowers => Set<TicketFollower>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TicketActivity> TicketActivities => Set<TicketActivity>();
    public DbSet<TicketStatusModel> TicketStatuses => Set<TicketStatusModel>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();
    public DbSet<TicketSubjectLink> TicketSubjectLinks => Set<TicketSubjectLink>();
    public DbSet<TicketTag> TicketTags => Set<TicketTag>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<SatisfactionRating> SatisfactionRatings => Set<SatisfactionRating>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<EscalationRule> EscalationRules => Set<EscalationRule>();
    public DbSet<BusinessSchedule> BusinessSchedules => Set<BusinessSchedule>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentCapacity> AgentCapacities => Set<AgentCapacity>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<AgentSkill> AgentSkills => Set<AgentSkill>();
    public DbSet<SkillRoutingTag> SkillRoutingTags => Set<SkillRoutingTag>();
    public DbSet<SkillRoutingDepartment> SkillRoutingDepartments => Set<SkillRoutingDepartment>();
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<Macro> Macros => Set<Macro>();
    public DbSet<SideConversation> SideConversations => Set<SideConversation>();
    public DbSet<SideConversationReply> SideConversationReplies => Set<SideConversationReply>();
    public DbSet<InboundEmail> InboundEmails => Set<InboundEmail>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleUser> RoleUsers => Set<RoleUser>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Plugin> Plugins => Set<Plugin>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<CustomObject> CustomObjects => Set<CustomObject>();
    public DbSet<CustomObjectRecord> CustomObjectRecords => Set<CustomObjectRecord>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportSourceMap> ImportSourceMaps => Set<ImportSourceMap>();
    public DbSet<EscalatedSettings> Settings => Set<EscalatedSettings>();
    public DbSet<Automation> Automations => Set<Automation>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleCategory> ArticleCategories => Set<ArticleCategory>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatRoutingRule> ChatRoutingRules => Set<ChatRoutingRule>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowLog> WorkflowLogs => Set<WorkflowLog>();
    public DbSet<NewsletterList> NewsletterLists => Set<NewsletterList>();
    public DbSet<NewsletterListMember> NewsletterListMembers => Set<NewsletterListMember>();
    public DbSet<NewsletterTemplate> NewsletterTemplates => Set<NewsletterTemplate>();
    public DbSet<Newsletter> Newsletters => Set<Newsletter>();
    public DbSet<NewsletterDelivery> NewsletterDeliveries => Set<NewsletterDelivery>();

    private bool RestrictDepartments => TicketAccessScope.Current?.DepartmentRestricted == true;
    private bool RestrictAccounts => TicketAccessScope.Current?.AccountRestricted == true;
    private bool RestrictTickets => RestrictDepartments || RestrictAccounts;
    private string? AccessUserId => TicketAccessScope.Current?.UserId;
    private string? AccessAccountType => TicketAccessScope.Current?.Account?.Type;
    private string? AccessAccountId => TicketAccessScope.Current?.Account?.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        EscalatedModelConfiguration.Configure(modelBuilder);
        var collation = Database.IsNpgsql() ? "C" : Database.IsSqlServer() ? "Latin1_General_100_BIN2" : "BINARY";
        modelBuilder.Entity<Ticket>().Property(t => t.AccountType).UseCollation(collation);
        modelBuilder.Entity<Ticket>().Property(t => t.AccountId).UseCollation(collation);
        modelBuilder.Entity<Ticket>().HasIndex(t => new { t.AccountType, t.AccountId });
        modelBuilder.Entity<Ticket>().HasQueryFilter(t => t.DeletedAt == null
            && (!RestrictDepartments || DepartmentAgents.Any(a => a.DepartmentId == t.DepartmentId && a.AgentId == AccessUserId))
            && (!RestrictAccounts || (AccessAccountType != null && AccessAccountId != null && t.AccountType == AccessAccountType && t.AccountId == AccessAccountId)));
        modelBuilder.Entity<Reply>().HasQueryFilter(r => r.DeletedAt == null && (!RestrictTickets || Tickets.Any(t => t.Id == r.TicketId)));
        modelBuilder.Entity<TicketActivity>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<TicketSubjectLink>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<TicketTag>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<WorkflowLog>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<TicketFollower>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<SatisfactionRating>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<ChatSession>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<SideConversation>().HasQueryFilter(r => !RestrictTickets || Tickets.Any(t => t.Id == r.TicketId));
        modelBuilder.Entity<SideConversationReply>().HasQueryFilter(r => !RestrictTickets || SideConversations.Any(s => s.Id == r.SideConversationId));
        modelBuilder.Entity<Mention>().HasQueryFilter(r => !RestrictTickets || Replies.Any(s => s.Id == r.ReplyId));
        modelBuilder.Entity<TicketLink>().HasQueryFilter(r => !RestrictTickets || (Tickets.Any(t => t.Id == r.ParentTicketId) && Tickets.Any(t => t.Id == r.ChildTicketId)));
        modelBuilder.Entity<Attachment>().HasQueryFilter(r => !RestrictTickets ||
            (r.AttachableType.ToLower() == "ticket" && Tickets.Any(t => t.Id == r.AttachableId)) ||
            (r.AttachableType.ToLower() == "reply" && Replies.Any(t => t.Id == r.AttachableId)));
        modelBuilder.Entity<CustomFieldValue>().HasQueryFilter(r => !RestrictTickets || r.EntityType.ToLower() != "ticket" || Tickets.Any(t => t.Id == r.EntityId));
    }
}
