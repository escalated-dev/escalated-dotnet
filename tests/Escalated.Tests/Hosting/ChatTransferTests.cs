using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Live chat transfer, as escalated-laravel's ChatController::transfer and
/// ChatSessionService::transferChat: <c>escalated.admin.chat.transfer</c>
/// takes the chat session id (the shared Agent/TicketShow page passes
/// <c>chat_session_id</c>) and <c>{agent_id, department_id}</c>; the shared
/// ChatActionBar's <c>{type, id}</c> is read too. It emits
/// <c>chat.transferred</c> to webhooks and workflows.
/// </summary>
public class ChatTransferTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
    }

    private static async Task<EscalatedTestHost> StartAsync(Action<EscalatedOptions>? configure = null)
    {
        var host = await EscalatedTestHost.StartAsync(
            configure,
            services => services.AddHttpClient("EscalatedWebhook").ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler()));
        await host.GrantRoleAsync("from", "escalated-agent");
        await host.GrantRoleAsync("to", "escalated-agent");
        await host.SeedAsync(async db =>
        {
            db.Webhooks.Add(new Webhook
            {
                Url = "https://hooks.example.com/escalated",
                Events = """["chat.transferred","chat.assigned"]""",
                Active = true,
            });
            await db.SaveChangesAsync();
        });
        return host;
    }

    private static async Task<ChatSession> ActiveChatAsync(EscalatedTestHost host, int? departmentId = null)
    {
        using var scope = host.Services.CreateScope();
        var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
        var session = await chats.StartAsync("Visitor", "visitor@example.com", "Hello?", departmentId);
        if (session.Status == "waiting")
        {
            session = await chats.AcceptAsync(session.Id, "from");
        }
        return session;
    }

    private static async Task<int> DepartmentAsync(EscalatedTestHost host)
    {
        var department = new Department { Name = "Billing", Slug = "billing-" + Guid.NewGuid().ToString("N")[..8] };
        await host.SeedAsync(async db =>
        {
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        });
        return department.Id;
    }

    private static Task<HttpResponseMessage> TransferAsync(EscalatedTestHost host, int sessionId, object body, string user = "from") =>
        host.ClientFor(user).PostAsync(
            $"/support/agent/chat/{sessionId}/transfer",
            new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"));

    private static Task<ChatSession> SessionAsync(EscalatedTestHost host, int id) =>
        host.QueryAsync(db => db.ChatSessions.AsNoTracking().SingleAsync(s => s.Id == id));

    private static Task<Ticket> TicketAsync(EscalatedTestHost host, int id) =>
        host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Id == id));

    private static Task<List<WebhookDelivery>> DeliveriesAsync(EscalatedTestHost host, string eventName) =>
        host.QueryAsync(db => db.WebhookDeliveries.AsNoTracking().Where(d => d.Event == eventName).OrderBy(d => d.Id).ToListAsync());

    [Fact]
    public void TheRouteHasLaravelsName()
    {
        Assert.Equal("{prefix}/admin/chat/{session}/transfer", UiRoutes.Defaults["escalated.admin.chat.transfer"]);
    }

    [Fact]
    public async Task TransferToAnAgent_ReassignsTheChatAndTicket()
    {
        await using var host = await StartAsync();
        await host.SeedAsync(async db =>
        {
            db.Workflows.Add(new Workflow
            {
                Name = "On transfer",
                TriggerEvent = "chat.transferred",
                Conditions = "{}",
                Actions = """[{"type":"add_note","value":"transferred"}]""",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        });
        var session = await ActiveChatAsync(host);

        using var response = await TransferAsync(host, session.Id, new { agent_id = "to" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Chat transferred.", body.RootElement.GetProperty("message").GetString());

        var fresh = await SessionAsync(host, session.Id);
        Assert.Equal("to", fresh.AgentId);
        Assert.Equal("active", fresh.Status);
        Assert.Equal("to", (await TicketAsync(host, session.TicketId)).AssignedTo);

        var delivery = Assert.Single(await DeliveriesAsync(host, "chat.transferred"));
        using var payload = JsonDocument.Parse(delivery.Payload!);
        var chat = payload.RootElement.GetProperty("chat");
        Assert.Equal(session.Id, chat.GetProperty("session_id").GetInt32());
        Assert.Equal("from", chat.GetProperty("from_agent_id").GetString());
        Assert.Equal("to", chat.GetProperty("to_agent_id").GetString());
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("to_department_id").ValueKind);
        Assert.DoesNotContain("visitor@example.com", delivery.Payload);

        var log = Assert.Single(await host.QueryAsync(db => db.WorkflowLogs.AsNoTracking().ToListAsync()));
        Assert.Equal("chat.transferred", log.TriggerEvent);
        Assert.Equal(session.TicketId, log.TicketId);
    }

    [Fact]
    public async Task DepartmentOnlyTransfer_RequeuesAndReroutes()
    {
        await using var host = await StartAsync();
        await host.GrantRoleAsync("routed", "escalated-agent");
        var billing = await DepartmentAsync(host);
        var session = await ActiveChatAsync(host);
        await host.SeedAsync(async db =>
        {
            db.ChatRoutingRules.Add(new ChatRoutingRule { Name = "Billing", DepartmentId = billing, AgentId = "routed", IsActive = true });
            await db.SaveChangesAsync();
        });

        using var response = await TransferAsync(host, session.Id, new { department_id = billing });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = await SessionAsync(host, session.Id);
        Assert.Equal(billing, fresh.DepartmentId);
        Assert.Equal("routed", fresh.AgentId);
        Assert.Equal("active", fresh.Status);
        var ticket = await TicketAsync(host, session.TicketId);
        Assert.Equal(billing, ticket.DepartmentId);
        Assert.Equal("routed", ticket.AssignedTo);

        var delivery = Assert.Single(await DeliveriesAsync(host, "chat.transferred"));
        using var payload = JsonDocument.Parse(delivery.Payload!);
        var chat = payload.RootElement.GetProperty("chat");
        Assert.Equal("from", chat.GetProperty("from_agent_id").GetString());
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("to_agent_id").ValueKind);
        Assert.Equal(billing, chat.GetProperty("to_department_id").GetInt32());
    }

    [Fact]
    public async Task DepartmentOnlyTransfer_WithNoAgent_LeavesTheChatWaiting()
    {
        await using var host = await StartAsync();
        var billing = await DepartmentAsync(host);
        var session = await ActiveChatAsync(host);

        using var response = await TransferAsync(host, session.Id, new { department_id = billing.ToString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = await SessionAsync(host, session.Id);
        Assert.Null(fresh.AgentId);
        Assert.Equal("waiting", fresh.Status);
        Assert.Null(fresh.AcceptedAt);
        var ticket = await TicketAsync(host, session.TicketId);
        Assert.Null(ticket.AssignedTo);
        Assert.Equal(billing, ticket.DepartmentId);
    }

    [Fact]
    public async Task AgentAndDepartmentTogether()
    {
        await using var host = await StartAsync();
        var billing = await DepartmentAsync(host);
        var session = await ActiveChatAsync(host);

        using var response = await TransferAsync(host, session.Id, new { agent_id = "to", department_id = billing });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = await SessionAsync(host, session.Id);
        Assert.Equal("to", fresh.AgentId);
        Assert.Equal("active", fresh.Status);
        Assert.Equal(billing, (await TicketAsync(host, session.TicketId)).DepartmentId);
    }

    [Fact]
    public async Task TheChatActionBarBody_IsAccepted()
    {
        await using var host = await StartAsync();
        var billing = await DepartmentAsync(host);
        var session = await ActiveChatAsync(host);

        using (var response = await TransferAsync(host, session.Id, new { type = "agent", id = "to" }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal("to", (await SessionAsync(host, session.Id)).AgentId);

        using (var response = await TransferAsync(host, session.Id, new { type = "department", id = billing }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(billing, (await TicketAsync(host, session.TicketId)).DepartmentId);
    }

    [Fact]
    public async Task Refusals()
    {
        await using var host = await StartAsync();
        var session = await ActiveChatAsync(host);

        using (var anonymous = await host.Client.PostAsJsonAsync($"/support/agent/chat/{session.Id}/transfer", new { agent_id = "to" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using (var customer = await TransferAsync(host, session.Id, new { agent_id = "to" }, user: "customer-9"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        }

        foreach (var body in new object[]
                 {
                     new { },
                     new { agent_id = (string?)null, department_id = "" },
                     new { department_id = 999999 },
                     new { department_id = "billing" },
                 })
        {
            using var response = await TransferAsync(host, session.Id, body);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        using (var missing = await TransferAsync(host, 999999, new { agent_id = "to" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ChatSessionService>().EndAsync(session.Id, "from");
        }
        using (var ended = await TransferAsync(host, session.Id, new { agent_id = "to" }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, ended.StatusCode);
        }

        Assert.Equal("from", (await SessionAsync(host, session.Id)).AgentId);
        Assert.Empty(await DeliveriesAsync(host, "chat.transferred"));
    }

    [Fact]
    public async Task AnAgentWhoCannotSeeTheTicket_IsRefused()
    {
        await using var host = await StartAsync(options => options.Visibility = "department");
        var billing = await DepartmentAsync(host);
        await host.SeedAsync(async db =>
        {
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = billing, AgentId = "from" });
            await db.SaveChangesAsync();
        });
        var session = await ActiveChatAsync(host, billing);

        foreach (var outsider in new[] { "to", "customer-9" })
        {
            using var response = await TransferAsync(host, session.Id, new { agent_id = outsider });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        Assert.Equal("from", (await SessionAsync(host, session.Id)).AgentId);
        Assert.Equal(billing, (await TicketAsync(host, session.TicketId)).DepartmentId);
        Assert.Empty(await DeliveriesAsync(host, "chat.transferred"));
    }

    [Theory]
    [InlineData(null, "/support")]
    [InlineData("helpdesk", "/helpdesk")]
    public async Task TheNamedRoute_TransfersFromTheTicketPage(string? prefix, string mount)
    {
        await using var host = await StartAsync(prefix is null ? null : options => options.RoutePrefix = prefix);
        var session = await ActiveChatAsync(host);

        using var response = await host.ClientFor("from").PostAsync(
            $"{mount}/admin/chat/{session.Id}/transfer",
            new StringContent("{\"agent_id\":\"to\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("to", (await SessionAsync(host, session.Id)).AgentId);
        Assert.Single(await DeliveriesAsync(host, "chat.transferred"));

        using var empty = await host.ClientFor("from").PostAsync(
            $"{mount}/admin/chat/{session.Id}/transfer", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        using var customer = await host.ClientFor("customer-9").PostAsync(
            $"{mount}/admin/chat/{session.Id}/transfer", new StringContent("{\"agent_id\":\"from\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
    }

    [Fact]
    public void ChatTransferred_IsOfferedToWorkflows()
    {
        Assert.Contains("chat.transferred", WorkflowEngine.TriggerEvents);
    }
}
