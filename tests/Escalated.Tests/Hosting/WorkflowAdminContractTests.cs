using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The Workflow admin endpoints against escalated-developer-context
/// <c>domain-model/workflow-admin-contract.md</c>, over the real HTTP pipeline.
/// Requests use the builder's body verbatim, never this backend's own field names.
/// </summary>
public class WorkflowAdminContractTests
{
    private const string Admin = "admin-1";
    private const string Base = "/support/admin/workflows";

    private sealed class Receiver : HttpMessageHandler
    {
        public List<(Uri Url, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
            {
                Requests.Add((request.RequestUri!, body));
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync(Receiver? receiver = null)
    {
        var host = await EscalatedTestHost.StartAsync(configureServices: services =>
        {
            if (receiver is not null)
            {
                services.AddHttpClient(WebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver);
            }
        });
        await host.GrantRoleAsync(Admin, "escalated-admin");
        return (host, host.ClientFor(Admin));
    }

    private static async Task<int> SeedDepartmentAsync(EscalatedTestHost host)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var department = new Department { Name = "Billing", Slug = "billing" };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            id = department.Id;
        });
        return id;
    }

    /// <summary>The contract's example request body, with a real department id.</summary>
    private static string ContractBody(int departmentId) => $$"""
        {
          "name": "Route refunds to billing",
          "description": null,
          "trigger_event": "ticket.created",
          "conditions": {
            "all": [{ "field": "subject", "operator": "contains", "value": "refund" }]
          },
          "actions": [
            { "type": "change_priority", "value": "high" },
            { "type": "set_department", "value": "{{departmentId}}" }
          ],
          "is_active": true
        }
        """;

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<int> CreateAsync(HttpClient client, string body)
    {
        var response = await client.PostAsync(Base, Json(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("workflow").GetProperty("id").GetInt32();
    }

    private static string WorkflowBody(string trigger, string actions, string conditions = """{"all":[]}""") => $$"""
        {"name":"Test","trigger_event":"{{trigger}}","conditions":{{conditions}},"actions":{{actions}},"is_active":true}
        """;

    private static async Task<Ticket> CreateTicketAsync(EscalatedTestHost host, string subject)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TicketService>().CreateAsync(subject, "body", requesterId: "7");
    }

    private static Task<Ticket> TicketAsync(EscalatedTestHost host, int id) =>
        host.QueryAsync(db => db.Tickets.AsNoTracking().Include(t => t.Tags).FirstAsync(t => t.Id == id));

    [Fact]
    public async Task Create_StoresTheBuilderBodyAsSent()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var departmentId = await SeedDepartmentAsync(host);

        var id = await CreateAsync(client, ContractBody(departmentId));

        var stored = await host.QueryAsync(db => db.Workflows.AsNoTracking().FirstAsync(w => w.Id == id));
        Assert.Equal("Route refunds to billing", stored.Name);
        Assert.Equal("ticket.created", stored.TriggerEvent);
        Assert.True(stored.IsActive);
        using var conditions = JsonDocument.Parse(stored.Conditions);
        var rule = Assert.Single(conditions.RootElement.GetProperty("all").EnumerateArray());
        Assert.Equal("subject", rule.GetProperty("field").GetString());
        Assert.Equal("contains", rule.GetProperty("operator").GetString());
        Assert.Equal("refund", rule.GetProperty("value").GetString());
        using var actions = JsonDocument.Parse(stored.Actions);
        Assert.Equal(new[] { "change_priority", "set_department" },
            actions.RootElement.EnumerateArray().Select(a => a.GetProperty("type").GetString()));
        Assert.Equal(new[] { "high", departmentId.ToString() },
            actions.RootElement.EnumerateArray().Select(a => a.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task StoredWorkflow_RunsOnItsTrigger()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var departmentId = await SeedDepartmentAsync(host);
        await CreateAsync(client, ContractBody(departmentId));

        var refund = await CreateTicketAsync(host, "Please refund my order");
        var other = await CreateTicketAsync(host, "Login trouble");

        var matched = await TicketAsync(host, refund.Id);
        Assert.Equal(TicketPriority.High, matched.Priority);
        Assert.Equal(departmentId, matched.DepartmentId);
        Assert.NotEqual(departmentId, (await TicketAsync(host, other.Id)).DepartmentId);
    }

    [Fact]
    public async Task Index_ListsWorkflowObjectsInPositionOrder()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var first = await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"add_note","value":"a"}]"""));
        var second = await CreateAsync(client, WorkflowBody("reply.created", """[{"type":"add_note","value":"b"}]"""));

        var body = await ReadAsync(await client.GetAsync(Base));

        var workflows = body.GetProperty("workflows").EnumerateArray().ToList();
        Assert.Equal(new[] { first, second }, workflows.Select(w => w.GetProperty("id").GetInt32()));
        var w = workflows[0];
        foreach (var key in new[] { "name", "description", "trigger_event", "trigger", "conditions", "actions", "is_active", "position" })
        {
            Assert.True(w.TryGetProperty(key, out var _), $"missing {key}");
        }
        Assert.Equal(JsonValueKind.Object, w.GetProperty("conditions").ValueKind);
        Assert.Equal(JsonValueKind.Array, w.GetProperty("actions").ValueKind);
        Assert.Equal("a", w.GetProperty("actions")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Options_ListWhatFiresAndWhatTheExecutorRuns()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var body = await ReadAsync(await client.GetAsync($"{Base}/options"));

        var triggers = body.GetProperty("trigger_events").EnumerateArray().Select(o => o.GetProperty("value").GetString()).ToList();
        var actions = body.GetProperty("action_types").EnumerateArray().Select(o => o.GetProperty("value").GetString()).ToList();
        var operators = body.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("value").GetString()).ToList();
        Assert.Contains("reply.created", triggers);
        Assert.Contains("note.created", triggers);
        Assert.DoesNotContain("reply.agent_reply", triggers);
        Assert.DoesNotContain("ticket.tagged", triggers);
        foreach (var core in new[] { "change_status", "change_priority", "add_tag", "remove_tag", "set_department", "assign_agent", "add_note", "insert_canned_reply", "add_follower", "delay", "send_webhook", "set_type" })
        {
            Assert.Contains(core, actions);
        }
        Assert.DoesNotContain("send_notification", actions);
        Assert.Contains("contains", operators);
        Assert.All(body.GetProperty("trigger_events").EnumerateArray(), o => Assert.False(string.IsNullOrEmpty(o.GetProperty("label").GetString())));
    }

    public static IEnumerable<object[]> InvalidBodies() => new[]
    {
        new object[] { """{"trigger_event":"ticket.created","actions":[{"type":"add_note","value":"x"}]}""", "name" },
        new object[] { """{"name":"x","actions":[{"type":"add_note","value":"x"}]}""", "trigger_event" },
        new object[] { """{"name":"x","trigger_event":"ticket.created"}""", "actions" },
        new object[] { """{"name":"x","trigger_event":"ticket.created","actions":[]}""", "actions" },
        new object[] { """{"name":"x","trigger_event":"ticket.created","actions":[{"type":"send_notification","value":"x"}]}""", "actions.0.type" },
        new object[] { """{"name":"x","trigger_event":"ticket.nope","actions":[{"type":"add_note","value":"x"}]}""", "trigger_event" },
        new object[] { """{"name":"x","trigger_event":"ticket.created","conditions":{"all":[],"any":[]},"actions":[{"type":"add_note","value":"x"}]}""", "conditions" },
        new object[] { """{"name":"x","trigger_event":"ticket.created","actions":[{"type":"add_note","value":{"nested":true}}]}""", "actions.0.value" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_RejectsAnInvalidBodyWith422(string body, string field)
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var response = await client.PostAsync(Base, Json(body));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out var _), $"no error for {field}: {json}");
        Assert.Equal(0, await host.QueryAsync(db => db.Workflows.CountAsync()));
    }

    [Fact]
    public async Task Create_WithoutConditions_StoresAllEmpty_AndALegacyTriggerCanonically()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var id = await CreateAsync(client, """{"name":"x","trigger_event":"ticket.replied","actions":[{"type":"add_note","value":"x"}]}""");

        var stored = await host.QueryAsync(db => db.Workflows.AsNoTracking().FirstAsync(w => w.Id == id));
        Assert.Equal("reply.created", stored.TriggerEvent);
        Assert.Equal("""{"all":[]}""", stored.Conditions);
    }

    [Fact]
    public async Task Update_Toggle_Reorder_AndDelete()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var a = await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"add_note","value":"a"}]"""));
        var b = await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"add_note","value":"b"}]"""));

        var update = await client.PutAsync($"{Base}/{a}", Json(WorkflowBody("ticket.updated", """[{"type":"change_priority","value":"low"}]""")));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await host.QueryAsync(db => db.Workflows.AsNoTracking().FirstAsync(w => w.Id == a));
        Assert.Equal("ticket.updated", updated.TriggerEvent);
        Assert.Contains("change_priority", updated.Actions);

        var toggle = await client.PostAsync($"{Base}/{a}/toggle", null);
        Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
        Assert.False((await ReadAsync(toggle)).GetProperty("workflow").GetProperty("is_active").GetBoolean());

        var reorder = await client.PostAsJsonAsync($"{Base}/reorder", new { workflow_ids = new[] { b, a } });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var order = (await ReadAsync(await client.GetAsync(Base))).GetProperty("workflows").EnumerateArray()
            .Select(w => w.GetProperty("id").GetInt32());
        Assert.Equal(new[] { b, a }, order);

        var delete = await client.DeleteAsync($"{Base}/{a}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(new[] { b }, await host.QueryAsync(db => db.Workflows.Select(w => w.Id).ToListAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"{Base}/{a}", Json(WorkflowBody("ticket.created", """[{"type":"add_note","value":"a"}]""")))).StatusCode);
    }

    [Fact]
    public async Task NonAdmins_AreRefused()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        await host.GrantRoleAsync("agent-2", "escalated-agent");

        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor("agent-2").GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor("agent-2").PostAsync(Base, Json(WorkflowBody("ticket.created", """[{"type":"add_note","value":"a"}]""")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Base)).StatusCode);
    }

    [Fact]
    public async Task Logs_ListExecutionsWithThePageFields()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var id = await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"add_note","value":"a"}]"""));
        var ticket = await CreateTicketAsync(host, "Hello");

        var all = await ReadAsync(await client.GetAsync($"{Base}/logs"));
        var log = Assert.Single(all.GetProperty("logs").EnumerateArray());
        Assert.Equal("ticket.created", log.GetProperty("event").GetString());
        Assert.Equal("Test", log.GetProperty("workflow_name").GetString());
        Assert.Equal(ticket.Reference, log.GetProperty("ticket_reference").GetString());
        Assert.True(log.GetProperty("matched").GetBoolean());
        Assert.Equal(1, log.GetProperty("actions_executed").GetInt32());
        Assert.Equal("success", log.GetProperty("status").GetString());
        Assert.Equal(id, Assert.Single(all.GetProperty("workflows").EnumerateArray()).GetProperty("id").GetInt32());

        var filtered = await ReadAsync(await client.GetAsync($"{Base}/{id}/logs"));
        Assert.Single(filtered.GetProperty("logs").EnumerateArray());
        var none = await ReadAsync(await client.GetAsync($"{Base}/logs?workflow={id + 100}"));
        Assert.Empty(none.GetProperty("logs").EnumerateArray());
    }

    [Fact]
    public async Task AddTag_FindsTheTagByName_AndSetTypeSetsTheTicketType()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        await host.SeedAsync(async db =>
        {
            db.Tags.Add(new Tag { Name = "Needs Review", Slug = "needs-review" });
            await db.SaveChangesAsync();
        });
        await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"add_tag","value":"Needs Review"},{"type":"set_type","value":"incident"}]"""));

        var ticket = await CreateTicketAsync(host, "Something broke");

        var stored = await TicketAsync(host, ticket.Id);
        Assert.Equal("incident", stored.TicketType);
        Assert.Equal("Needs Review", Assert.Single(stored.Tags).Name);
    }

    [Fact]
    public async Task TagsCondition_MatchesATagTheTicketHas()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var tagId = 0;
        await host.SeedAsync(async db =>
        {
            var tag = new Tag { Name = "VIP", Slug = "vip" };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        });
        await CreateAsync(client, WorkflowBody("ticket.tag_added", """[{"type":"change_priority","value":"urgent"}]""",
            """{"all":[{"field":"tags","operator":"contains","value":"vip"}]}"""));
        var ticket = await CreateTicketAsync(host, "Big customer");

        using (var scope = host.Services.CreateScope())
        {
            var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
            await tickets.AddTagsAsync((await tickets.FindByIdAsync(ticket.Id))!, new[] { tagId }, Admin);
        }

        Assert.Equal(TicketPriority.Urgent, (await TicketAsync(host, ticket.Id)).Priority);
    }

    [Theory]
    [InlineData("""{"match":"all","rules":[{"field":"subject","operator":"contains","value":"refund"}]}""")]
    [InlineData("""[{"field":"subject","operator":"contains","value":"refund"}]""")]
    public async Task StoredLegacyConditionShapes_StillEvaluate(string conditions)
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Workflows.Add(new Workflow
            {
                Name = "Legacy",
                TriggerEvent = "ticket.created",
                Conditions = conditions,
                Actions = """[{"type":"change_priority","value":"high"}]""",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        });

        var refund = await CreateTicketAsync(host, "refund please");
        var other = await CreateTicketAsync(host, "hello");

        Assert.Equal(TicketPriority.High, (await TicketAsync(host, refund.Id)).Priority);
        Assert.NotEqual(TicketPriority.High, (await TicketAsync(host, other.Id)).Priority);
    }

    [Fact]
    public async Task SendWebhook_PostsTheTicketToTheUrl()
    {
        var receiver = new Receiver();
        var (host, client) = await StartAsync(receiver);
        await using var _ = host;
        await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"send_webhook","value":"https://hooks.example.com/flow"}]"""));

        var ticket = await CreateTicketAsync(host, "Ping the CRM");

        var (url, body) = Assert.Single(receiver.Requests);
        Assert.Equal("https://hooks.example.com/flow", url.ToString());
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(ticket.Id, doc.RootElement.GetProperty("ticket").GetProperty("id").GetInt32());
        Assert.Equal(ticket.Reference, doc.RootElement.GetProperty("ticket").GetProperty("reference").GetString());
    }

    [Fact]
    public async Task SendWebhook_ToAPrivateAddress_IsNotSent()
    {
        var receiver = new Receiver();
        var (host, client) = await StartAsync(receiver);
        await using var _ = host;
        await CreateAsync(client, WorkflowBody("ticket.created", """[{"type":"send_webhook","value":"http://169.254.169.254/latest"}]"""));

        await CreateTicketAsync(host, "Ping");

        Assert.Empty(receiver.Requests);
    }

    [Fact]
    public async Task Delay_HoldsTheRemainingActionsUntilTheyAreDue()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        await CreateAsync(client, WorkflowBody("ticket.created",
            """[{"type":"add_note","value":"now"},{"type":"delay","value":"30"},{"type":"change_priority","value":"urgent"}]"""));

        var ticket = await CreateTicketAsync(host, "Slow burn");

        Assert.NotEqual(TicketPriority.Urgent, (await TicketAsync(host, ticket.Id)).Priority);
        Assert.Contains("now", await host.QueryAsync(db => db.Replies.Where(r => r.TicketId == ticket.Id).Select(r => r.Body).ToListAsync()));
        var pending = await host.QueryAsync(db => db.WorkflowDelayedActions.AsNoTracking().SingleAsync());
        Assert.InRange(pending.ExecuteAt, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));

        using (var scope = host.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<WorkflowDelayedActionProcessor>();
            Assert.Equal(0, await processor.ProcessDueAsync(DateTime.UtcNow.AddMinutes(10)));
            Assert.Equal(1, await processor.ProcessDueAsync(DateTime.UtcNow.AddMinutes(31)));
        }

        Assert.Equal(TicketPriority.Urgent, (await TicketAsync(host, ticket.Id)).Priority);
        Assert.True((await host.QueryAsync(db => db.WorkflowDelayedActions.AsNoTracking().SingleAsync())).Executed);
    }

    [Fact]
    public async Task Delay_IsCancelledWhenTheTicketIsClosedFirst()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        await CreateAsync(client, WorkflowBody("ticket.created",
            """[{"type":"delay","value":"5"},{"type":"change_priority","value":"urgent"}]"""));
        var ticket = await CreateTicketAsync(host, "Closed early");

        using (var scope = host.Services.CreateScope())
        {
            var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
            var t = await tickets.FindByIdAsync(ticket.Id);
            await tickets.ChangeStatusAsync(t!, TicketStatus.Closed, Admin);
        }

        using (var scope = host.Services.CreateScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<WorkflowDelayedActionProcessor>().ProcessDueAsync(DateTime.UtcNow.AddMinutes(6)));
        }

        Assert.NotEqual(TicketPriority.Urgent, (await TicketAsync(host, ticket.Id)).Priority);
        Assert.True((await host.QueryAsync(db => db.WorkflowDelayedActions.AsNoTracking().SingleAsync())).Cancelled);
    }
}
