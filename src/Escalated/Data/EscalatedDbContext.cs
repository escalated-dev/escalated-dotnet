using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Data;

public class EscalatedDbContext : DbContext
{
    public EscalatedDbContext(DbContextOptions<EscalatedDbContext> options) : base(options) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TicketActivity> TicketActivities => Set<TicketActivity>();
    public DbSet<TicketStatusModel> TicketStatuses => Set<TicketStatusModel>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();
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
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<Macro> Macros => Set<Macro>();
    public DbSet<SideConversation> SideConversations => Set<SideConversation>();
    public DbSet<SideConversationReply> SideConversationReplies => Set<SideConversationReply>();
    public DbSet<InboundEmail> InboundEmails => Set<InboundEmail>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Table names with prefix
        const string prefix = "escalated_";

        modelBuilder.Entity<Ticket>(e =>
        {
            e.ToTable($"{prefix}tickets");
            e.HasIndex(t => t.Reference).IsUnique();
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.Priority);
            e.HasIndex(t => t.AssignedTo);
            e.HasIndex(t => t.DepartmentId);
            e.HasIndex(t => t.GuestToken);
            e.HasIndex(t => t.GuestEmail);
            e.HasIndex(t => new { t.RequesterType, t.RequesterId });
            e.HasIndex(t => t.CreatedAt);
            e.HasQueryFilter(t => t.DeletedAt == null);

            e.HasOne(t => t.Department).WithMany(d => d.Tickets).HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.SlaPolicy).WithMany(s => s.Tickets).HasForeignKey(t => t.SlaPolicyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.MergedIntoTicket).WithMany().HasForeignKey(t => t.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.Replies).WithOne(r => r.Ticket).HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Activities).WithOne(a => a.Ticket).HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.SideConversations).WithOne(s => s.Ticket).HasForeignKey(s => s.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.LinksAsParent).WithOne(l => l.ParentTicket).HasForeignKey(l => l.ParentTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.LinksAsChild).WithOne(l => l.ChildTicket).HasForeignKey(l => l.ChildTicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.SatisfactionRating).WithOne(r => r.Ticket).HasForeignKey<SatisfactionRating>(r => r.TicketId);
        });

        modelBuilder.Entity<Reply>(e =>
        {
            e.ToTable($"{prefix}replies");
            e.HasIndex(r => r.TicketId);
            e.HasIndex(r => r.MessageId);
            e.HasQueryFilter(r => r.DeletedAt == null);
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.ToTable($"{prefix}attachments");
            e.HasIndex(a => new { a.AttachableType, a.AttachableId });
        });

        modelBuilder.Entity<TicketActivity>(e =>
        {
            e.ToTable($"{prefix}ticket_activities");
            e.HasIndex(a => a.TicketId);
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<TicketStatusModel>(e =>
        {
            e.ToTable($"{prefix}ticket_statuses");
            e.HasIndex(s => s.Slug).IsUnique();
        });

        modelBuilder.Entity<TicketLink>(e =>
        {
            e.ToTable($"{prefix}ticket_links");
            e.HasIndex(l => new { l.ParentTicketId, l.ChildTicketId });
        });

        modelBuilder.Entity<TicketTag>(e =>
        {
            e.ToTable($"{prefix}ticket_tag");
            e.HasKey(tt => new { tt.TicketId, tt.TagId });
            e.HasOne(tt => tt.Ticket).WithMany().HasForeignKey(tt => tt.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tt => tt.Tag).WithMany().HasForeignKey(tt => tt.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable($"{prefix}tags");
            e.HasIndex(t => t.Slug).IsUnique();
            e.HasMany(t => t.Tickets).WithMany(t => t.Tags)
                .UsingEntity<TicketTag>(
                    j => j.HasOne(tt => tt.Ticket).WithMany().HasForeignKey(tt => tt.TicketId),
                    j => j.HasOne(tt => tt.Tag).WithMany().HasForeignKey(tt => tt.TagId));
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.ToTable($"{prefix}departments");
            e.HasIndex(d => d.Slug).IsUnique();
        });

        modelBuilder.Entity<SatisfactionRating>(e =>
        {
            e.ToTable($"{prefix}satisfaction_ratings");
            e.HasIndex(r => r.TicketId).IsUnique();
        });

        modelBuilder.Entity<SlaPolicy>(e =>
        {
            e.ToTable($"{prefix}sla_policies");
        });

        modelBuilder.Entity<EscalationRule>(e =>
        {
            e.ToTable($"{prefix}escalation_rules");
        });

        modelBuilder.Entity<BusinessSchedule>(e =>
        {
            e.ToTable($"{prefix}business_schedules");
            e.HasMany(b => b.Holidays).WithOne(h => h.Schedule).HasForeignKey(h => h.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Holiday>(e =>
        {
            e.ToTable($"{prefix}holidays");
        });

        modelBuilder.Entity<AgentProfile>(e =>
        {
            e.ToTable($"{prefix}agent_profiles");
            e.HasIndex(a => a.UserId).IsUnique();
        });

        modelBuilder.Entity<AgentCapacity>(e =>
        {
            e.ToTable($"{prefix}agent_capacity");
            e.HasIndex(a => new { a.UserId, a.Channel }).IsUnique();
        });

        modelBuilder.Entity<Skill>(e =>
        {
            e.ToTable($"{prefix}skills");
            e.HasIndex(s => s.Slug).IsUnique();
        });

        modelBuilder.Entity<AgentSkill>(e =>
        {
            e.ToTable($"{prefix}agent_skill");
            e.HasKey(a => new { a.UserId, a.SkillId });
            e.HasOne(a => a.Skill).WithMany(s => s.AgentSkills).HasForeignKey(a => a.SkillId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CannedResponse>(e =>
        {
            e.ToTable($"{prefix}canned_responses");
        });

        modelBuilder.Entity<Macro>(e =>
        {
            e.ToTable($"{prefix}macros");
        });

        modelBuilder.Entity<SideConversation>(e =>
        {
            e.ToTable($"{prefix}side_conversations");
        });

        modelBuilder.Entity<SideConversationReply>(e =>
        {
            e.ToTable($"{prefix}side_conversation_replies");
        });

        modelBuilder.Entity<InboundEmail>(e =>
        {
            e.ToTable($"{prefix}inbound_emails");
            e.HasIndex(i => i.MessageId);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable($"{prefix}roles");
            e.HasIndex(r => r.Slug).IsUnique();
            e.HasMany(r => r.Permissions).WithMany(p => p.Roles)
                .UsingEntity<RolePermission>(
                    j => j.HasOne(rp => rp.Permission).WithMany().HasForeignKey(rp => rp.PermissionId),
                    j => j.HasOne(rp => rp.Role).WithMany().HasForeignKey(rp => rp.RoleId));
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable($"{prefix}permissions");
            e.HasIndex(p => p.Slug).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable($"{prefix}role_permission");
            e.HasKey(rp => new { rp.RoleId, rp.PermissionId });
        });

        modelBuilder.Entity<RoleUser>(e =>
        {
            e.ToTable($"{prefix}role_user");
            e.HasKey(ru => new { ru.RoleId, ru.UserId });
            e.HasOne(ru => ru.Role).WithMany(r => r.Users).HasForeignKey(ru => ru.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiToken>(e =>
        {
            e.ToTable($"{prefix}api_tokens");
            e.HasIndex(a => a.TokenHash).IsUnique();
        });

        modelBuilder.Entity<Webhook>(e =>
        {
            e.ToTable($"{prefix}webhooks");
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.ToTable($"{prefix}webhook_deliveries");
            e.HasOne(d => d.Webhook).WithMany(w => w.Deliveries).HasForeignKey(d => d.WebhookId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable($"{prefix}audit_logs");
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<Plugin>(e =>
        {
            e.ToTable($"{prefix}plugins");
            e.HasIndex(p => p.Slug).IsUnique();
        });

        modelBuilder.Entity<CustomField>(e =>
        {
            e.ToTable($"{prefix}custom_fields");
            e.HasIndex(f => f.Slug).IsUnique();
            e.HasMany(f => f.Values).WithOne(v => v.CustomField).HasForeignKey(v => v.CustomFieldId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomFieldValue>(e =>
        {
            e.ToTable($"{prefix}custom_field_values");
            e.HasIndex(v => new { v.EntityType, v.EntityId });
        });

        modelBuilder.Entity<CustomObject>(e =>
        {
            e.ToTable($"{prefix}custom_objects");
            e.HasIndex(o => o.Slug).IsUnique();
            e.HasMany(o => o.Records).WithOne(r => r.Object).HasForeignKey(r => r.ObjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomObjectRecord>(e =>
        {
            e.ToTable($"{prefix}custom_object_records");
        });

        modelBuilder.Entity<ImportJob>(e =>
        {
            e.ToTable($"{prefix}import_jobs");
        });

        modelBuilder.Entity<ImportSourceMap>(e =>
        {
            e.ToTable($"{prefix}import_source_maps");
            e.HasIndex(m => new { m.ImportJobId, m.EntityType, m.SourceId }).IsUnique();
        });

        modelBuilder.Entity<EscalatedSettings>(e =>
        {
            e.ToTable($"{prefix}settings");
            e.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<Automation>(e =>
        {
            e.ToTable($"{prefix}automations");
        });

        modelBuilder.Entity<SavedView>(e =>
        {
            e.ToTable($"{prefix}saved_views");
        });

        modelBuilder.Entity<Article>(e =>
        {
            e.ToTable($"{prefix}articles");
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasOne(a => a.Category).WithMany(c => c.Articles).HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ArticleCategory>(e =>
        {
            e.ToTable($"{prefix}article_categories");
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
