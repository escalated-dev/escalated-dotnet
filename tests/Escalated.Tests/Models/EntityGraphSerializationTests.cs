using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Tests.Models;

/// <summary>
/// Controllers return EF entities, and MVC serializes them without a reference
/// handler. When both ends of a relationship are serialized, EF's relationship
/// fixup turns every loaded graph into a cycle: a reply's <c>Ticket</c> holds the
/// reply in its <c>Replies</c>, the department holds the ticket in its
/// <c>Tickets</c>, and System.Text.Json gives up at MVC's depth limit with
/// "A possible object cycle was detected".
///
/// <para><see cref="JsonContractTests"/> checks each type's contract on its own;
/// a cycle only shows up once there is a graph to walk.</para>
/// </summary>
public class EntityGraphSerializationTests
{
    /// <summary>What MVC serializes a response with: the web defaults, depth 32, no reference handler.</summary>
    private static readonly JsonSerializerOptions MvcJson = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    [Fact]
    public void EveryTwoWayRelationship_LeavesOneSideOutOfJson()
    {
        using var db = ModelOnlyContext();
        var offenders = new SortedSet<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var navigation in NavigationsOf(entity))
            {
                if (navigation.Inverse is not { } inverse)
                {
                    continue;
                }

                if (IsSerialized(navigation) && IsSerialized(inverse))
                {
                    var sides = new[] { Describe(navigation), Describe(inverse) }.OrderBy(s => s, StringComparer.Ordinal);
                    offenders.Add(string.Join(" <-> ", sides));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Both ends of these relationships are serialized, so any graph loading both is a cycle. " +
            "Put [JsonIgnore] on the back-reference the frontend does not read:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public async Task EveryEntityWithNavigations_SerializesWithItsGraphLoaded()
    {
        using var lease = TestDatabase.LeaseDatabase();

        await using (var seed = NewContext(lease))
        {
            lease.CreateTables(seed);
            await SeedGraphAsync(seed);
        }

        // One context for every query, as a request's scoped context is: whatever
        // one query loaded, fixup attaches to what the next one loads.
        await using var db = NewContext(lease);
        var loaded = new List<(Type Type, List<object> Rows)>();

        foreach (var entity in db.Model.GetEntityTypes().Where(e => NavigationsOf(e).Any()).OrderBy(e => e.Name))
        {
            IQueryable query = Set(db, entity.ClrType);
            foreach (var navigation in NavigationsOf(entity))
            {
                query = Include(query, entity.ClrType, navigation.Name);
            }

            loaded.Add((entity.ClrType, ((IEnumerable)query).Cast<object>().ToList()));
        }

        var unseeded = loaded.Where(l => l.Rows.Count == 0).Select(l => l.Type.Name).ToList();
        Assert.True(unseeded.Count == 0, "Seed at least one of each in SeedGraphAsync: " + string.Join(", ", unseeded));

        var failures = new List<string>();
        foreach (var (type, rows) in loaded)
        {
            try
            {
                foreach (var row in rows)
                {
                    JsonSerializer.Serialize(row, type, MvcJson);
                }
            }
            catch (JsonException ex)
            {
                failures.Add($"{type.Name}: {Truncate(ex.Message)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>One of everything that has a navigation, wired through the navigations.</summary>
    private static async Task SeedGraphAsync(EscalatedDbContext db)
    {
        var department = new Department { Name = "Billing", Slug = "billing" };
        var tag = new Tag { Name = "Invoices", Slug = "invoices" };
        var contact = new Contact { Email = "alice@example.com", Name = "Alice" };

        var ticket = new Ticket
        {
            Reference = "ESC-00001",
            Subject = "Invoice is wrong",
            RequesterId = "alice",
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            Department = department,
            SlaPolicy = new SlaPolicy { Name = "Standard" },
            Contact = contact,
        };
        var duplicate = new Ticket
        {
            Reference = "ESC-00002",
            Subject = "Invoice is wrong (again)",
            RequesterId = "alice",
            Department = department,
            MergedIntoTicket = ticket,
        };
        ticket.Tags.Add(tag);
        duplicate.Tags.Add(tag);

        var reply = new Reply { Body = "The total is off by one", AuthorId = "alice", Type = "reply" };
        reply.Attachments.Add(new Attachment { AttachableType = "reply", Filename = "invoice.pdf", Path = "https://files.example.com/invoice.pdf" });
        ticket.Replies.Add(reply);
        ticket.Replies.Add(new Reply { Body = "Asking finance", AuthorId = "agent-2", IsInternalNote = true, Type = "note" });
        ticket.Attachments.Add(new Attachment { AttachableType = "ticket", Filename = "screenshot.png", Path = "https://files.example.com/screenshot.png" });
        ticket.Activities.Add(new TicketActivity { Type = ActivityType.Replied, CauserId = "alice", Properties = "{}" });

        var sideConversation = new SideConversation { Subject = "Refund approval", CreatedBy = "agent-2" };
        sideConversation.Replies.Add(new SideConversationReply { Body = "Approved", AuthorId = "agent-3" });
        ticket.SideConversations.Add(sideConversation);

        ticket.LinksAsParent.Add(new TicketLink { ChildTicket = duplicate, LinkType = "duplicates" });
        ticket.Subjects.Add(new TicketSubjectLink { SubjectType = "project", SubjectId = "42" });
        ticket.SatisfactionRating = new SatisfactionRating { Rating = 5, Comment = "Thanks" };
        ticket.CustomFieldValues.Add(new CustomFieldValue
        {
            CustomField = new CustomField { Name = "Order number", Slug = "order_number" },
            EntityType = "ticket",
            Value = "A-1",
        });
        ticket.ChatSessions.Add(new ChatSession { Department = department, Status = "ended" });

        var workflow = new Workflow { Name = "Tag invoices", TriggerEvent = "ticket.created" };
        workflow.WorkflowLogs.Add(new WorkflowLog { Ticket = ticket, TriggerEvent = "ticket.created" });

        var webhook = new Webhook { Url = "https://example.com/hook", Events = """["*"]""" };
        webhook.Deliveries.Add(new WebhookDelivery { Event = "ticket.created", Payload = "{}", ResponseCode = 200 });

        var role = new Role { Name = "Escalated Admin", Slug = "escalated-admin" };
        role.Permissions.Add(new Permission { Name = "Manage newsletters", Slug = "newsletters.manage" });
        role.Users.Add(new RoleUser { UserId = "admin-1" });

        var skill = new Skill { Name = "Billing", Slug = "billing" };
        skill.AgentSkills.Add(new AgentSkill { UserId = "agent-2" });
        skill.RoutingTags.Add(new SkillRoutingTag { Tag = tag });
        skill.RoutingDepartments.Add(new SkillRoutingDepartment { Department = department });

        var schedule = new BusinessSchedule { Name = "Office hours" };
        schedule.Holidays.Add(new Holiday { Name = "New Year", Date = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc) });

        var billingCategory = new ArticleCategory { Name = "Billing", Slug = "billing" };
        var invoicesCategory = new ArticleCategory { Name = "Invoices", Slug = "invoices", Parent = billingCategory };
        invoicesCategory.Articles.Add(new Article { Title = "Reading your invoice", Slug = "reading-your-invoice", Status = "published" });

        var customObject = new CustomObject { Name = "Order", Slug = "order" };
        customObject.Records.Add(new CustomObjectRecord { Data = "{}" });

        var importJob = new ImportJob { Platform = "zendesk" };
        importJob.SourceMaps.Add(new ImportSourceMap { EntityType = "ticket", SourceId = "zd-1", EscalatedId = "1" });

        var list = new NewsletterList { Name = "Customers" };
        list.Members.Add(new NewsletterListMember { Contact = contact });
        var newsletter = new NewsletterEntity
        {
            Subject = "September update",
            FromEmail = "news@example.com",
            TargetList = list,
            Template = new NewsletterTemplate { Name = "Monthly", BodyMarkdown = "Hello" },
        };

        db.AddRange(ticket, duplicate, workflow, webhook, role, skill, schedule, billingCategory, invoicesCategory,
            customObject, importJob, newsletter);
        db.AddRange(
            new Mention { Reply = reply, UserId = "agent-3" },
            new InboundEmail { FromEmail = contact.Email, Ticket = ticket, Status = "processed" },
            new ChatRoutingRule { Name = "Billing chats", Department = department },
            new NewsletterDelivery { Newsletter = newsletter, Contact = contact, EmailAtSend = contact.Email, TrackingToken = "tracking-1" });

        await db.SaveChangesAsync();
    }

    private static IEnumerable<IReadOnlyNavigationBase> NavigationsOf(IReadOnlyEntityType entity) =>
        entity.GetNavigations().Cast<IReadOnlyNavigationBase>().Concat(entity.GetSkipNavigations());

    private static bool IsSerialized(IReadOnlyNavigationBase navigation) =>
        navigation.PropertyInfo is { } property
        && property.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always };

    private static string Describe(IReadOnlyNavigationBase navigation) =>
        $"{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}";

    private static EscalatedDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlite("DataSource=:memory:").Options);

    private static EscalatedDbContext NewContext(TestDatabase.Lease lease)
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>();
        lease.Configure(options);
        return new EscalatedDbContext(options.Options);
    }

    private static IQueryable Set(DbContext db, Type type) =>
        (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(type)
            .Invoke(db, null)!;

    private static IQueryable Include(IQueryable query, Type type, string navigation) =>
        (IQueryable)typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.Include)
                         && m.GetParameters() is [_, { ParameterType: var p }] && p == typeof(string))
            .MakeGenericMethod(type)
            .Invoke(null, new object[] { query, navigation })!;

    private static string Truncate(string message) => message.Length <= 300 ? message : message[..300] + "...";
}
