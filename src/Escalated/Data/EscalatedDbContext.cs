using Escalated.Models;
using Escalated.Models.Newsletter;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        EscalatedModelConfiguration.Configure(modelBuilder);
    }
}
