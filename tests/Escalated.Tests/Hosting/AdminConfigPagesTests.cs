using System.Net;
using System.Text.Json;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The admin configuration pages (SLA policies, escalation rules, canned
/// responses, macros, statuses, webhooks) against escalated-laravel's
/// controllers for them, plus the JSON admin API on the shared paths staying as it was.
/// </summary>
public class AdminConfigPagesTests
{
    private const string Admin = "admin-1";

    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(id == Admin ? new UserDirectoryEntry(Admin, "Ada Admin", "ada@example.com") : null);
    }

    private sealed class Receiver : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ReceiverClientFactory : IHttpClientFactory
    {
        private readonly Receiver _receiver;

        public ReceiverClientFactory(Receiver receiver) => _receiver = receiver;

        public HttpClient CreateClient(string name) => new(_receiver, disposeHandler: false);
    }

    private sealed record Seeded(int PolicyId, int RuleId, int CannedId, int MacroId, int StatusId, int WebhookId, int DeliveryId);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed, Receiver Receiver)> StartAsync()
    {
        var receiver = new Receiver();
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddSingleton<IHttpClientFactory>(new ReceiverClientFactory(receiver));
            });
        await host.GrantRoleAsync(Admin, "escalated-admin");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var policy = new SlaPolicy
            {
                Name = "Standard",
                Description = "Default targets",
                FirstResponseHours = """{"low":24,"high":4}""",
                ResolutionHours = """{"low":72,"high":24}""",
                IsDefault = true,
            };
            var rule = new EscalationRule
            {
                Name = "Stale urgent",
                Category = "SLA",
                Conditions = """[{"field":"age_hours","value":"4"}]""",
                Actions = """[{"type":"change_priority","value":"urgent"}]""",
                Order = 2,
            };
            var canned = new CannedResponse { Title = "Thanks", Body = "Thanks for writing.", Category = "General", CreatedBy = Admin };
            var macro = new Macro { Name = "Close", Description = "Resolve it", Actions = """[{"type":"status","value":"resolved"}]""", CreatedBy = Admin };
            var status = new TicketStatusModel { Label = "Waiting on vendor", Slug = "waiting-on-vendor", Category = "pending", Color = "#f59e0b", Position = 1 };
            var webhook = new Webhook { Url = "https://hooks.example.com/escalated", Secret = "s3cret", Events = """["ticket.created"]""" };
            db.AddRange(policy, rule, canned, macro, status, webhook);
            await db.SaveChangesAsync();

            db.Tickets.Add(new Ticket { Reference = "ESC-00001", Subject = "Card declined", SlaPolicyId = policy.Id });
            var delivery = new WebhookDelivery { WebhookId = webhook.Id, Event = "ticket.created", Payload = """{"id":1}""", ResponseCode = 500, CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
            db.WebhookDeliveries.AddRange(delivery,
                new WebhookDelivery { WebhookId = webhook.Id, Event = "ticket.created", Payload = "{}", ResponseCode = 200 });
            await db.SaveChangesAsync();

            seeded = new Seeded(policy.Id, rule.Id, canned.Id, macro.Id, status.Id, webhook.Id, delivery.Id);
        });

        return (host, host.ClientFor(Admin), seeded!, receiver);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null, string? expectLocation = null)
    {
        var response = await client.SendAsync(Visit(method, url, body ?? new { }));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        if (expectLocation is not null) Assert.Equal(expectLocation, response.Headers.Location!.ToString());
        return response;
    }

    /// <summary>The errors the next page shows after a failed post, which must have gone back to <paramref name="page"/>.</summary>
    private static async Task<JsonElement> ErrorsAfterAsync(HttpClient client, HttpResponseMessage response, string page)
    {
        Assert.Equal(page, response.Headers.Location!.ToString());
        var next = await PageAsync(client, page, FlashCookie(response));
        return next.GetProperty("props").GetProperty("errors");
    }

    // --- SLA policies ---

    [Fact]
    public async Task SlaPolicies_IndexAndFormRenderThePolicies()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/sla-policies");
        AssertPage(index, "Escalated/Admin/SlaPolicies/Index");
        var policy = Keys(index.GetProperty("props").GetProperty("policies")[0], "id", "name", "description", "is_default",
            "business_hours_only", "is_active", "tickets_count", "first_response_hours", "resolution_hours");
        Assert.Equal(1, policy.GetProperty("tickets_count").GetInt32());
        Assert.Equal(4, policy.GetProperty("first_response_hours").GetProperty("high").GetDouble());

        var create = await PageAsync(client, "/support/admin/sla-policies/create");
        AssertPage(create, "Escalated/Admin/SlaPolicies/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("policy").ValueKind);
        Assert.Equal(new[] { "low", "medium", "high", "urgent", "critical" },
            create.GetProperty("props").GetProperty("priorities").EnumerateArray().Select(p => p.GetString()));

        var edit = await PageAsync(client, $"/support/admin/sla-policies/{seed.PolicyId}/edit");
        AssertPage(edit, "Escalated/Admin/SlaPolicies/Form");
        Assert.Equal(72, edit.GetProperty("props").GetProperty("policy").GetProperty("resolution_hours").GetProperty("low").GetDouble());
    }

    [Fact]
    public async Task SlaPolicies_StoreUpdateAndDestroy()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/sla-policies", new
        {
            name = "Premium",
            description = "",
            is_default = false,
            first_response_hours = new Dictionary<string, object> { ["urgent"] = 1, ["high"] = "2.5", ["low"] = "" },
            resolution_hours = new Dictionary<string, object> { ["urgent"] = 4 },
            business_hours_only = true,
            is_active = true,
        }, "/support/admin/sla-policies");

        var created = await host.QueryAsync(db => db.SlaPolicies.AsNoTracking().SingleAsync(p => p.Name == "Premium"));
        Assert.True(created.BusinessHoursOnly);
        Assert.Equal(1, created.GetFirstResponseHoursFor(Enums.TicketPriority.Urgent));
        Assert.Equal(2.5, created.GetFirstResponseHoursFor(Enums.TicketPriority.High));
        Assert.Null(created.GetFirstResponseHoursFor(Enums.TicketPriority.Low));

        await SendAsync(client, HttpMethod.Put, $"/support/admin/sla-policies/{created.Id}", new
        {
            name = "Premium 2",
            first_response_hours = new { urgent = 2 },
            resolution_hours = new { urgent = 8 },
            is_active = false,
        }, "/support/admin/sla-policies");
        var updated = await host.QueryAsync(db => db.SlaPolicies.AsNoTracking().SingleAsync(p => p.Id == created.Id));
        Assert.Equal("Premium 2", updated.Name);
        Assert.False(updated.IsActive);
        Assert.Equal(8, updated.GetResolutionHoursFor(Enums.TicketPriority.Urgent));

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/sla-policies/{seed.PolicyId}", expectLocation: "/support/admin/sla-policies");
        Assert.False(await host.QueryAsync(db => db.SlaPolicies.AnyAsync(p => p.Id == seed.PolicyId)));
        Assert.Null(await host.QueryAsync(db => db.Tickets.Select(t => t.SlaPolicyId).SingleAsync()));
    }

    [Fact]
    public async Task SlaPolicies_RequireANameAndTargets()
    {
        var (host, client, _, _) = await StartAsync();
        await using var __ = host;

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/sla-policies", new { name = "", first_response_hours = "x" });

        Assert.Equal("/support/admin/sla-policies/create", response.Headers.Location!.ToString());
        var errors = await ErrorsAfterAsync(client, response, "/support/admin/sla-policies/create");
        Keys(errors, "name", "first_response_hours", "resolution_hours");
        Assert.Equal(1, await host.QueryAsync(db => db.SlaPolicies.CountAsync()));
    }

    // --- Escalation rules ---

    [Fact]
    public async Task EscalationRules_IndexAndForm()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/escalation-rules");
        AssertPage(index, "Escalated/Admin/EscalationRules/Index");
        var rule = Keys(index.GetProperty("props").GetProperty("rules")[0], "id", "name", "description", "category",
            "trigger_type", "conditions", "actions", "order", "is_active");
        Assert.Equal("time_based", rule.GetProperty("trigger_type").GetString());
        Assert.Equal(2, rule.GetProperty("order").GetInt32());
        Assert.Equal("age_hours", rule.GetProperty("conditions")[0].GetProperty("field").GetString());

        var create = await PageAsync(client, "/support/admin/escalation-rules/create");
        AssertPage(create, "Escalated/Admin/EscalationRules/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("rule").ValueKind);

        var edit = await PageAsync(client, $"/support/admin/escalation-rules/{seed.RuleId}/edit");
        Assert.Equal("urgent", edit.GetProperty("props").GetProperty("rule").GetProperty("actions")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task EscalationRules_StoreUpdateAndDestroy()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/escalation-rules", new
        {
            name = "Unassigned for a day",
            description = "Bump it",
            trigger_type = "time_based",
            conditions = new[] { new { field = "unassigned_hours", value = "24" } },
            actions = new[] { new { type = "change_priority", value = "high" } },
            order = 5,
            is_active = true,
        }, "/support/admin/escalation-rules");
        var created = await host.QueryAsync(db => db.EscalationRules.AsNoTracking().SingleAsync(r => r.Name == "Unassigned for a day"));
        Assert.Equal(5, created.Order);
        Assert.Equal("unassigned_hours", JsonDocument.Parse(created.Conditions).RootElement[0].GetProperty("field").GetString());
        Assert.Equal("high", JsonDocument.Parse(created.Actions).RootElement[0].GetProperty("value").GetString());

        await SendAsync(client, HttpMethod.Put, $"/support/admin/escalation-rules/{created.Id}", new
        {
            name = "Renamed",
            trigger_type = "time_based",
            conditions = new[] { new { field = "unassigned_hours", value = "48" } },
            actions = new[] { new { type = "escalate", value = "" } },
            order = 1,
            is_active = false,
        }, "/support/admin/escalation-rules");
        var updated = await host.QueryAsync(db => db.EscalationRules.AsNoTracking().SingleAsync(r => r.Id == created.Id));
        Assert.Equal("Renamed", updated.Name);
        Assert.False(updated.IsActive);
        Assert.Equal(1, updated.Order);

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/escalation-rules", new { name = "No actions", trigger_type = "time_based" });
        Keys(await ErrorsAfterAsync(client, invalid, "/support/admin/escalation-rules/create"), "conditions", "actions");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/escalation-rules/{seed.RuleId}", expectLocation: "/support/admin/escalation-rules");
        Assert.False(await host.QueryAsync(db => db.EscalationRules.AnyAsync(r => r.Id == seed.RuleId)));
    }

    // --- Canned responses ---

    [Fact]
    public async Task CannedResponses_IndexStoreUpdateAndDestroy()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/canned-responses");
        AssertPage(index, "Escalated/Admin/CannedResponses/Index");
        var response = Keys(index.GetProperty("props").GetProperty("responses")[0], "id", "title", "body", "category", "is_shared", "created_by", "creator");
        Assert.Equal("Ada Admin", response.GetProperty("creator").GetProperty("name").GetString());

        await SendAsync(client, HttpMethod.Post, "/support/admin/canned-responses", new { title = "Refunds", body = "Refunds take 5 days.", category = "Billing", is_shared = true });
        var created = await host.QueryAsync(db => db.CannedResponses.AsNoTracking().SingleAsync(c => c.Title == "Refunds"));
        Assert.Equal(Admin, created.CreatedBy);
        Assert.True(created.IsShared);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/canned-responses/{created.Id}", new { title = "Refunds", body = "Refunds take 3 days.", is_shared = false });
        var updated = await host.QueryAsync(db => db.CannedResponses.AsNoTracking().SingleAsync(c => c.Id == created.Id));
        Assert.Equal("Refunds take 3 days.", updated.Body);
        Assert.False(updated.IsShared);

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/canned-responses", new { title = "", body = "" });
        Keys(await ErrorsAfterAsync(client, invalid, "/support/admin/canned-responses"), "title", "body");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/canned-responses/{seed.CannedId}");
        Assert.False(await host.QueryAsync(db => db.CannedResponses.AnyAsync(c => c.Id == seed.CannedId)));
    }

    // --- Macros ---

    [Fact]
    public async Task Macros_IndexStoreUpdateAndDestroy()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/macros");
        AssertPage(index, "Escalated/Admin/Macros/Index");
        var macro = Keys(index.GetProperty("props").GetProperty("macros")[0], "id", "name", "description", "actions", "is_shared", "order", "creator");
        Assert.Equal("status", macro.GetProperty("actions")[0].GetProperty("type").GetString());
        Assert.Equal("Ada Admin", macro.GetProperty("creator").GetProperty("name").GetString());

        await SendAsync(client, HttpMethod.Post, "/support/admin/macros", new
        {
            name = "Escalate",
            description = "",
            is_shared = true,
            actions = new object[] { new { type = "priority", value = "urgent", order = 0 }, new { type = "note", value = "Escalated.", order = 1 } },
        });
        var created = await host.QueryAsync(db => db.Macros.AsNoTracking().SingleAsync(m => m.Name == "Escalate"));
        Assert.Equal(Admin, created.CreatedBy);
        var actions = JsonDocument.Parse(created.Actions).RootElement;
        Assert.Equal(2, actions.GetArrayLength());
        Assert.Equal("urgent", actions[0].GetProperty("value").GetString());

        await SendAsync(client, HttpMethod.Put, $"/support/admin/macros/{created.Id}", new
        {
            name = "Escalate now",
            actions = new object[] { new { type = "priority", value = "critical", order = 0 } },
        });
        var updated = await host.QueryAsync(db => db.Macros.AsNoTracking().SingleAsync(m => m.Id == created.Id));
        Assert.Equal("Escalate now", updated.Name);
        Assert.Equal("critical", JsonDocument.Parse(updated.Actions).RootElement[0].GetProperty("value").GetString());

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/macros", new
        {
            name = "Bad",
            actions = new object[] { new { type = "launch_rocket", value = "now" } },
        });
        Keys(await ErrorsAfterAsync(client, invalid, "/support/admin/macros"), "actions");
        var empty = await SendAsync(client, HttpMethod.Post, "/support/admin/macros", new { name = "Empty", actions = Array.Empty<object>() });
        Keys(await ErrorsAfterAsync(client, empty, "/support/admin/macros"), "actions");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/macros/{seed.MacroId}");
        Assert.False(await host.QueryAsync(db => db.Macros.AnyAsync(m => m.Id == seed.MacroId)));
    }

    // --- Statuses ---

    [Fact]
    public async Task Statuses_IndexAndForm()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/statuses");
        AssertPage(index, "Escalated/Admin/Statuses/Index");
        var props = index.GetProperty("props");
        Assert.Equal(new[] { "new", "open", "pending", "on_hold", "solved" }, props.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
        var status = Keys(props.GetProperty("statuses")[0], "id", "label", "slug", "category", "color", "description", "position", "is_default");
        Assert.Equal("pending", status.GetProperty("category").GetString());

        var create = await PageAsync(client, "/support/admin/statuses/create");
        AssertPage(create, "Escalated/Admin/Statuses/Form");
        var edit = await PageAsync(client, $"/support/admin/statuses/{seed.StatusId}/edit");
        Assert.Equal("Waiting on vendor", edit.GetProperty("props").GetProperty("status").GetProperty("label").GetString());
    }

    [Fact]
    public async Task Statuses_StoreUpdateAndDestroy_KeepOneDefaultPerCategory()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/statuses",
            new { label = "Waiting on vendor", category = "pending", color = "#000000", description = "Third party", position = 2, is_default = true },
            "/support/admin/statuses");
        var created = await host.QueryAsync(db => db.TicketStatuses.AsNoTracking().SingleAsync(s => s.Id != seed.StatusId));
        Assert.Equal("waiting-on-vendor-2", created.Slug);
        Assert.True(created.IsDefault);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/statuses/{seed.StatusId}",
            new { label = "Vendor", category = "pending", color = "#111111", position = 0, is_default = true },
            "/support/admin/statuses");
        var statuses = await host.QueryAsync(db => db.TicketStatuses.AsNoTracking().OrderBy(s => s.Id).ToListAsync());
        Assert.Equal("Vendor", statuses[0].Label);
        Assert.Equal(new[] { true, false }, statuses.Select(s => s.IsDefault));

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/statuses", new { label = "", category = "archived", color = "" });
        Keys(await ErrorsAfterAsync(client, invalid, "/support/admin/statuses/create"), "label", "category", "color");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/statuses/{seed.StatusId}", expectLocation: "/support/admin/statuses");
        Assert.Equal(1, await host.QueryAsync(db => db.TicketStatuses.CountAsync()));
    }

    // --- Webhooks ---

    [Fact]
    public async Task Webhooks_IndexFormAndDeliveryLog()
    {
        var (host, client, seed, _) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/webhooks");
        AssertPage(index, "Escalated/Admin/Webhooks/Index");
        var webhook = Keys(index.GetProperty("props").GetProperty("webhooks")[0], "id", "url", "events", "active", "deliveries", "deliveries_count");
        Assert.Equal("ticket.created", webhook.GetProperty("events")[0].GetString());
        Assert.Equal(2, webhook.GetProperty("deliveries_count").GetInt32());
        Assert.Equal(1, webhook.GetProperty("deliveries").GetArrayLength());
        Assert.Equal(200, webhook.GetProperty("deliveries")[0].GetProperty("response_code").GetInt32());

        var create = await PageAsync(client, "/support/admin/webhooks/create");
        AssertPage(create, "Escalated/Admin/Webhooks/Form");
        var events = create.GetProperty("props").GetProperty("availableEvents").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("ticket.created", events);
        Assert.Contains("chat.ended", events);

        var edit = await PageAsync(client, $"/support/admin/webhooks/{seed.WebhookId}/edit");
        Assert.Equal("s3cret", edit.GetProperty("props").GetProperty("webhook").GetProperty("secret").GetString());

        var log = await PageAsync(client, $"/support/admin/webhooks/{seed.WebhookId}/deliveries");
        AssertPage(log, "Escalated/Admin/Webhooks/DeliveryLog");
        var deliveries = Keys(log.GetProperty("props").GetProperty("deliveries"), "data", "current_page", "last_page", "total", "links");
        Assert.Equal(2, deliveries.GetProperty("total").GetInt32());
        var delivery = Keys(deliveries.GetProperty("data")[1], "id", "event", "payload", "response_code", "attempts", "created_at");
        Assert.Equal(1, delivery.GetProperty("payload").GetProperty("id").GetInt32());
        Assert.Equal("https://hooks.example.com/escalated", log.GetProperty("props").GetProperty("webhook").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Webhooks_StoreUpdateRetryAndDestroy()
    {
        var (host, client, seed, receiver) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/webhooks",
            new { url = "https://hooks.example.com/new", events = new[] { "ticket.created", "ticket.tagged" }, secret = "", active = true },
            "/support/admin/webhooks");
        var created = await host.QueryAsync(db => db.Webhooks.AsNoTracking().SingleAsync(w => w.Url == "https://hooks.example.com/new"));
        Assert.Equal("""["ticket.created","ticket.tag_added"]""", created.Events);
        Assert.Null(created.Secret);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/webhooks/{created.Id}",
            new { url = "https://hooks.example.com/changed", events = new[] { "reply.created" }, secret = "abc", active = false },
            "/support/admin/webhooks");
        var updated = await host.QueryAsync(db => db.Webhooks.AsNoTracking().SingleAsync(w => w.Id == created.Id));
        Assert.Equal("https://hooks.example.com/changed", updated.Url);
        Assert.Equal("abc", updated.Secret);
        Assert.False(updated.Active);

        var unsafeUrl = await SendAsync(client, HttpMethod.Post, "/support/admin/webhooks", new { url = "http://127.0.0.1/hook", events = new[] { "ticket.created" } });
        Keys(await ErrorsAfterAsync(client, unsafeUrl, "/support/admin/webhooks/create"), "url");
        var noEvents = await SendAsync(client, HttpMethod.Post, "/support/admin/webhooks", new { url = "https://hooks.example.com/x", events = Array.Empty<string>() });
        Keys(await ErrorsAfterAsync(client, noEvents, "/support/admin/webhooks/create"), "events");

        await SendAsync(client, HttpMethod.Post, $"/support/admin/webhooks/deliveries/{seed.DeliveryId}/retry");
        Assert.Equal(1, receiver.Requests);

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/webhooks/{seed.WebhookId}", expectLocation: "/support/admin/webhooks");
        Assert.False(await host.QueryAsync(db => db.Webhooks.AnyAsync(w => w.Id == seed.WebhookId)));
    }

    // --- The JSON API on the same paths ---

    [Theory]
    [InlineData("/support/admin/sla-policies")]
    [InlineData("/support/admin/escalation-rules")]
    [InlineData("/support/admin/canned-responses")]
    [InlineData("/support/admin/macros")]
    [InlineData("/support/admin/webhooks")]
    public async Task TheJsonApi_StillAnswersApiClients(string url)
    {
        var (host, client, _, _) = await StartAsync();
        await using var __ = host;

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }
}
