using System.Net;
using System.Net.Http.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParameterlessRegistration = Escalated.Extensions.ServiceCollectionExtensions;

namespace Escalated.Tests.Events;

/// <summary>
/// The shared event vocabulary (escalated-developer-context ADR
/// 2026-10-21-event-vocabulary), driven through the real mutation sites with
/// everything <c>AddEscalated()</c> registers: webhooks and workflow triggers
/// emit only the canonical names, and stored legacy names are read as their
/// canonical name.
/// </summary>
public class EventVocabularyTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var name = Guid.NewGuid().ToString();
        services.AddDbContext<EscalatedDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        ParameterlessRegistration.AddEscalated(services);
        services.AddHttpClient("EscalatedWebhook").ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler());

        return services.BuildServiceProvider();
    }

    private static async Task SeedWebhookAsync(IServiceProvider sp, string events)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        db.Webhooks.Add(new Webhook { Url = "https://hooks.example.com/escalated", Events = events, Active = true });
        await db.SaveChangesAsync();
    }

    private static async Task SeedWorkflowAsync(IServiceProvider sp, string trigger, string note)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        db.Workflows.Add(new Workflow
        {
            Name = trigger,
            TriggerEvent = trigger,
            Conditions = "{}",
            Actions = $$"""[{"type":"add_note","value":"{{note}}"}]""",
            IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedTagAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var tag = new Tag { Name = "VIP", Slug = "vip" };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag.Id;
    }

    private static async Task<Ticket> CreateTicketAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TicketService>()
            .CreateAsync("Printer on fire", "It is actually on fire", requesterId: "7");
    }

    private static async Task WithTicketAsync(IServiceProvider sp, int ticketId, Func<IServiceProvider, Ticket, Task> act)
    {
        using var scope = sp.CreateScope();
        var ticket = await scope.ServiceProvider.GetRequiredService<EscalatedDbContext>().Tickets.FirstAsync(t => t.Id == ticketId);
        await act(scope.ServiceProvider, ticket);
    }

    private static List<string> DeliveredEvents(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EscalatedDbContext>().WebhookDeliveries
            .OrderBy(d => d.Id).Select(d => d.Event).ToList();
    }

    private static List<WorkflowLog> WorkflowLogs(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EscalatedDbContext>().WorkflowLogs.ToList();
    }

    private static List<string> NoteBodies(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EscalatedDbContext>().Replies
            .Where(r => r.IsInternalNote).Select(r => r.Body).ToList();
    }

    [Fact]
    public async Task Webhooks_ReceiveCanonicalNamesForNotesAndTags()
    {
        await using var sp = Build();
        await SeedWebhookAsync(sp, """["*"]""");
        var tagId = await SeedTagAsync(sp);
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, async (s, t) =>
        {
            var tickets = s.GetRequiredService<TicketService>();
            await tickets.AddReplyAsync(t, "Internal only", authorId: "1", isNote: true);
            await tickets.AddTagsAsync(t, new[] { tagId }, "1");
            await tickets.RemoveTagsAsync(t, new[] { tagId }, "1");
        });

        var events = DeliveredEvents(sp);
        Assert.Contains("note.created", events);
        Assert.Contains("ticket.tag_added", events);
        Assert.Contains("ticket.tag_removed", events);
        Assert.DoesNotContain("internal_note.added", events);
        Assert.DoesNotContain("ticket.tagged", events);
        Assert.DoesNotContain("ticket.untagged", events);
    }

    [Theory]
    [InlineData("internal_note.added")]
    [InlineData("internal_note.created")]
    public async Task Webhook_SubscribedUnderALegacyNoteName_ReceivesNoteCreated(string legacy)
    {
        await using var sp = Build();
        await SeedWebhookAsync(sp, $"""["{legacy}"]""");
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddReplyAsync(t, "Internal only", authorId: "1", isNote: true));

        Assert.Equal(new[] { "note.created" }, DeliveredEvents(sp));
    }

    [Theory]
    [InlineData("ticket.tagged")]
    [InlineData("tag.added")]
    public async Task Webhook_SubscribedUnderALegacyTagName_ReceivesTagAdded(string legacy)
    {
        await using var sp = Build();
        await SeedWebhookAsync(sp, $"""["{legacy}"]""");
        var tagId = await SeedTagAsync(sp);
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddTagsAsync(t, new[] { tagId }, "1"));

        Assert.Equal(new[] { "ticket.tag_added" }, DeliveredEvents(sp));
    }

    [Fact]
    public async Task Webhooks_ReceiveTheChatEvents()
    {
        await using var sp = Build();
        await SeedWebhookAsync(sp, """["chat.started","chat.assigned","chat.message","chat.ended"]""");

        int sessionId;
        using (var scope = sp.CreateScope())
        {
            var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            var session = await chats.StartAsync("Visitor", "visitor@example.com", "Hello?");
            sessionId = session.Id;
        }

        using (var scope = sp.CreateScope())
        {
            var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            await chats.AcceptAsync(sessionId, "5");
        }

        using (var scope = sp.CreateScope())
        {
            var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            await chats.SendMessageAsync(sessionId, "Hi, how can I help?", "5", "agent");
        }

        using (var scope = sp.CreateScope())
        {
            var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            await chats.EndAsync(sessionId, "5");
        }

        Assert.Equal(new[] { "chat.started", "chat.assigned", "chat.message", "chat.ended" }, DeliveredEvents(sp));
    }

    [Fact]
    public async Task Webhook_SubscribedToChatSessionStarted_ReceivesChatStarted()
    {
        await using var sp = Build();
        await SeedWebhookAsync(sp, """["chat.session_started"]""");

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ChatSessionService>().StartAsync("Visitor", "visitor@example.com");
        }

        Assert.Equal(new[] { "chat.started" }, DeliveredEvents(sp));
    }

    [Fact]
    public async Task Workflow_SavedWithLegacyTicketReplied_FiresOnAPublicReply()
    {
        await using var sp = Build();
        await SeedWorkflowAsync(sp, "ticket.replied", "legacy-reply");
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddReplyAsync(t, "Any update?", authorId: "7"));

        var log = Assert.Single(WorkflowLogs(sp));
        Assert.Equal("reply.created", log.TriggerEvent);
        Assert.Contains("legacy-reply", NoteBodies(sp));
    }

    [Fact]
    public async Task Workflow_OnNoteCreated_FiresForANoteAndNotForAReply()
    {
        await using var sp = Build();
        await SeedWorkflowAsync(sp, "note.created", "note-seen");
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddReplyAsync(t, "Public reply", authorId: "1"));
        Assert.Empty(WorkflowLogs(sp));

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddReplyAsync(t, "Internal", authorId: "1", isNote: true));

        var log = Assert.Single(WorkflowLogs(sp));
        Assert.Equal("note.created", log.TriggerEvent);
    }

    [Theory]
    [InlineData("ticket.tag_added")]
    [InlineData("ticket.tagged")]
    public async Task Workflow_OnTagAdded_FiresUnderTheCanonicalAndLegacyName(string trigger)
    {
        await using var sp = Build();
        await SeedWorkflowAsync(sp, trigger, "tagged");
        var tagId = await SeedTagAsync(sp);
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, (s, t) =>
            s.GetRequiredService<TicketService>().AddTagsAsync(t, new[] { tagId }, "1"));

        var log = Assert.Single(WorkflowLogs(sp));
        Assert.Equal("ticket.tag_added", log.TriggerEvent);
    }

    public static IEnumerable<object[]> TicketTriggers() => new[]
    {
        new object[] { "ticket.resolved" },
        new object[] { "ticket.closed" },
        new object[] { "ticket.unassigned" },
        new object[] { "ticket.escalated" },
        new object[] { "ticket.tag_removed" },
    };

    [Theory]
    [MemberData(nameof(TicketTriggers))]
    public async Task Workflow_FiresOnEveryCanonicalTicketTrigger(string trigger)
    {
        await using var sp = Build();
        await SeedWorkflowAsync(sp, trigger, "fired");
        var tagId = await SeedTagAsync(sp);
        var ticket = await CreateTicketAsync(sp);

        await WithTicketAsync(sp, ticket.Id, async (s, t) =>
        {
            var tickets = s.GetRequiredService<TicketService>();
            var assignment = s.GetRequiredService<AssignmentService>();
            switch (trigger)
            {
                case "ticket.resolved":
                    await tickets.ChangeStatusAsync(t, TicketStatus.Resolved, "1");
                    break;
                case "ticket.closed":
                    await tickets.ChangeStatusAsync(t, TicketStatus.Closed, "1");
                    break;
                case "ticket.unassigned":
                    await assignment.AssignAsync(t, "5", "1");
                    await assignment.UnassignAsync(t, "1");
                    break;
                case "ticket.escalated":
                    await tickets.ChangeStatusAsync(t, TicketStatus.Escalated, "1");
                    break;
                case "ticket.tag_removed":
                    await tickets.AddTagsAsync(t, new[] { tagId }, "1");
                    await tickets.RemoveTagsAsync(t, new[] { tagId }, "1");
                    break;
            }
        });

        var log = Assert.Single(WorkflowLogs(sp));
        Assert.Equal(trigger, log.TriggerEvent);
        Assert.Contains("fired", NoteBodies(sp));
    }

    [Theory]
    [InlineData("chat.started")]
    [InlineData("chat.session_started")]
    public async Task Workflow_OnChatStarted_FiresForTheChatTicket(string trigger)
    {
        await using var sp = Build();
        await SeedWorkflowAsync(sp, trigger, "chat-seen");

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ChatSessionService>().StartAsync("Visitor", "visitor@example.com");
        }

        var log = Assert.Single(WorkflowLogs(sp));
        Assert.Equal("chat.started", log.TriggerEvent);
    }

    [Fact]
    public async Task CreatingAWebhook_StoresCanonicalNames()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync("admin-1", "escalated-admin");

        var response = await host.ClientFor("admin-1").PostAsJsonAsync("/support/admin/webhooks", new
        {
            url = "https://hooks.example.com/escalated",
            events = """["ticket.tagged","internal_note.added","reply.created"]""",
        });

        response.EnsureSuccessStatusCode();
        var stored = await host.QueryAsync(db => db.Webhooks.Select(w => w.Events).SingleAsync());
        Assert.Equal("""["ticket.tag_added","note.created","reply.created"]""", stored);
    }

    [Fact]
    public void TriggerList_OffersOnlyCanonicalNamesThatFire()
    {
        Assert.DoesNotContain("reply.agent_reply", WorkflowEngine.TriggerEvents);
        Assert.DoesNotContain("ticket.tagged", WorkflowEngine.TriggerEvents);
        Assert.Contains("reply.created", WorkflowEngine.TriggerEvents);
        Assert.Contains("note.created", WorkflowEngine.TriggerEvents);
        Assert.Contains("ticket.tag_added", WorkflowEngine.TriggerEvents);
        Assert.Contains("chat.started", WorkflowEngine.TriggerEvents);
    }
}
