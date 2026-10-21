using System.Net;
using System.Text.Json;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The admin pages Settings, Departments, Tags and Workflows, against
/// escalated-laravel's <c>SettingsController</c>, <c>DepartmentController</c>,
/// <c>TagController</c> and <c>WorkflowController</c>, plus the JSON admin API on
/// the same paths staying as it was.
/// </summary>
public class AdminPagesTests
{
    private const string Admin = "admin-1";

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(options => options.Ui.Enabled = true);
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.SeedAsync(async db =>
        {
            var billing = new Department { Name = "Billing", Slug = "billing" };
            var vip = new Tag { Name = "VIP", Slug = "vip", Color = "#ff0000" };
            db.AddRange(billing, vip);
            await db.SaveChangesAsync();
            var ticket = new Ticket { Reference = "ESC-00001", Subject = "Card declined", DepartmentId = billing.Id };
            db.Tickets.Add(ticket);
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = billing.Id, AgentId = "agent-1" });
            await db.SaveChangesAsync();
            db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = vip.Id });
            db.Workflows.Add(new Workflow
            {
                Name = "Route refunds",
                TriggerEvent = "ticket.created",
                Conditions = """{"all":[]}""",
                Actions = """[{"type":"change_priority","value":"high"}]""",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        });
        return (host, host.ClientFor(Admin));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var response = await client.SendAsync(Visit(method, url, body ?? new { }));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    // --- Settings ---

    [Fact]
    public async Task Settings_RendersOnlyTheSettingsThisBackendStores()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var page = await PageAsync(client, "/support/admin/settings");
        AssertPage(page, "Escalated/Admin/Settings");
        var props = page.GetProperty("props");

        var supported = props.GetProperty("supported_settings").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("guest_tickets_enabled", supported);
        Assert.Contains("widget_allowed_origins", supported);
        Assert.DoesNotContain("max_attachment_size_kb", supported);
        var settings = props.GetProperty("settings");
        foreach (var key in supported) Assert.True(settings.TryGetProperty(key!, out _), key);
        Assert.True(settings.GetProperty("guest_tickets_enabled").GetBoolean());
        Assert.Equal("/support/admin/settings", props.GetProperty("update_url").GetString());
        Keys(props.GetProperty("departments")[0], "id", "name");
    }

    [Fact]
    public async Task Settings_SavesValidValuesAndRejectsBadOnes()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/settings", new
        {
            guest_tickets_enabled = false,
            widget_color = "#123456",
            widget_allowed_origins = "https://Shop.Example.com, https://*.example.org",
            max_attachment_size_kb = 1,
        });

        var stored = await host.QueryAsync(db => db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value));
        Assert.Equal("0", stored["guest_tickets_enabled"]);
        Assert.Equal("#123456", stored["widget_color"]);
        Assert.Equal("https://shop.example.com\nhttps://*.example.org", stored["widget_allowed_origins"]);
        Assert.False(stored.ContainsKey("max_attachment_size_kb"));

        var bad = await SendAsync(client, HttpMethod.Post, "/support/admin/settings", new { widget_color = "red", widget_allowed_origins = "*" });
        var page = await PageAsync(client, "/support/admin/settings", FlashCookie(bad));
        Keys(page.GetProperty("props").GetProperty("errors"), "widget_color", "widget_allowed_origins");
        Assert.False(page.GetProperty("props").GetProperty("settings").GetProperty("guest_tickets_enabled").GetBoolean());
    }

    [Fact]
    public async Task Settings_JsonApiIsUnchanged()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var get = await client.GetAsync("/support/admin/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(JsonValueKind.Object, (await JsonDocument.ParseAsync(await get.Content.ReadAsStreamAsync())).RootElement.ValueKind);
        Assert.False(get.Headers.Contains("X-Inertia"));

        var post = await client.PostAsync("/support/admin/settings", System.Net.Http.Json.JsonContent.Create(new Dictionary<string, string> { ["widget_color"] = "#000000" }));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    // --- Departments ---

    [Fact]
    public async Task Departments_IndexAndFormRender()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var index = await PageAsync(client, "/support/admin/departments");
        AssertPage(index, "Escalated/Admin/Departments/Index");
        var row = Keys(index.GetProperty("props").GetProperty("departments")[0], "id", "name", "slug", "is_active", "tickets_count", "agents_count");
        Assert.Equal(1, row.GetProperty("tickets_count").GetInt32());
        Assert.Equal(1, row.GetProperty("agents_count").GetInt32());

        var create = await PageAsync(client, "/support/admin/departments/create");
        AssertPage(create, "Escalated/Admin/Departments/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("department").ValueKind);

        var edit = await PageAsync(client, $"/support/admin/departments/{row.GetProperty("id").GetInt32()}/edit");
        var department = Keys(edit.GetProperty("props").GetProperty("department"), "id", "name", "slug", "description", "is_active", "agents");
        Assert.Equal("Billing", department.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Departments_CreateUpdateDelete()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/departments", new { name = "Sales Team", description = "Leads", is_active = true });
        Assert.Equal("/support/admin/departments", created.Headers.Location!.ToString());
        var sales = await host.QueryAsync(db => db.Departments.AsNoTracking().SingleAsync(d => d.Name == "Sales Team"));
        Assert.Equal("sales-team", sales.Slug);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/departments/{sales.Id}", new { name = "Sales", is_active = false });
        sales = await host.QueryAsync(db => db.Departments.AsNoTracking().SingleAsync(d => d.Id == sales.Id));
        Assert.Equal("Sales", sales.Name);
        Assert.False(sales.IsActive);

        var billingId = await host.QueryAsync(db => db.Departments.Where(d => d.Slug == "billing").Select(d => d.Id).SingleAsync());
        await SendAsync(client, HttpMethod.Delete, $"/support/admin/departments/{billingId}");
        Assert.False(await host.QueryAsync(db => db.Departments.AnyAsync(d => d.Id == billingId)));
        Assert.Null((await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync())).DepartmentId);

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/departments", new { name = "" });
        Assert.Equal("/support/admin/departments/create", invalid.Headers.Location!.ToString());
        var form = await PageAsync(client, "/support/admin/departments/create", FlashCookie(invalid));
        Assert.True(form.GetProperty("props").GetProperty("errors").TryGetProperty("name", out _));
    }

    // --- Tags ---

    [Fact]
    public async Task Tags_IndexCreateUpdateDelete()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var index = await PageAsync(client, "/support/admin/tags");
        AssertPage(index, "Escalated/Admin/Tags/Index");
        var vip = Keys(index.GetProperty("props").GetProperty("tags")[0], "id", "name", "color", "tickets_count");
        Assert.Equal(1, vip.GetProperty("tickets_count").GetInt32());

        await SendAsync(client, HttpMethod.Post, "/support/admin/tags", new { name = "Refund", color = "#06b6d4" });
        var refund = await host.QueryAsync(db => db.Tags.AsNoTracking().SingleAsync(t => t.Name == "Refund"));
        Assert.Equal("#06b6d4", refund.Color);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/tags/{refund.Id}", new { name = "Refunds", color = "#000000" });
        Assert.Equal("Refunds", (await host.QueryAsync(db => db.Tags.AsNoTracking().SingleAsync(t => t.Id == refund.Id))).Name);

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/tags/{vip.GetProperty("id").GetInt32()}");
        Assert.Equal(1, await host.QueryAsync(db => db.Tags.CountAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.TicketTags.CountAsync()));

        // The JSON API's tag list still answers JSON callers.
        var api = await client.GetAsync("/support/admin/tags");
        Assert.Equal(JsonValueKind.Array, (await JsonDocument.ParseAsync(await api.Content.ReadAsStreamAsync())).RootElement.ValueKind);
    }

    // --- Workflows ---

    [Fact]
    public async Task Workflows_IndexAndFormRender()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var index = await PageAsync(client, "/support/admin/workflows");
        AssertPage(index, "Escalated/Admin/Workflows/Index");
        var row = Keys(index.GetProperty("props").GetProperty("workflows")[0],
            "id", "name", "description", "trigger_event", "conditions", "actions", "is_active", "trigger_count", "last_triggered_at");
        Assert.Equal("Route refunds", row.GetProperty("name").GetString());

        var create = await PageAsync(client, "/support/admin/workflows/create");
        AssertPage(create, "Escalated/Admin/Workflows/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("workflow").ValueKind);
        Keys(create.GetProperty("props").GetProperty("trigger_events")[0], "value", "label");

        var edit = await PageAsync(client, $"/support/admin/workflows/{row.GetProperty("id").GetInt32()}");
        AssertPage(edit, "Escalated/Admin/Workflows/Form");
        Assert.Equal("high", edit.GetProperty("props").GetProperty("workflow").GetProperty("actions")[0].GetProperty("value").GetString());

        // JSON callers still get the contract's JSON.
        var api = await client.GetAsync("/support/admin/workflows");
        Assert.True((await JsonDocument.ParseAsync(await api.Content.ReadAsStreamAsync())).RootElement.TryGetProperty("workflows", out _));
        Assert.False(api.Headers.Contains("X-Inertia"));
    }

    [Fact]
    public async Task Workflows_PagePostsRedirectToTheList()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var body = new
        {
            name = "Escalate urgent",
            description = (string?)null,
            trigger_event = "ticket.created",
            conditions = new { all = new[] { new { field = "priority", @operator = "equals", value = "urgent" } } },
            actions = new[] { new { type = "change_priority", value = "critical" } },
            is_active = true,
        };
        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/workflows", body);
        Assert.Equal("/support/admin/workflows", created.Headers.Location!.ToString());
        var workflow = await host.QueryAsync(db => db.Workflows.AsNoTracking().SingleAsync(w => w.Name == "Escalate urgent"));

        await SendAsync(client, HttpMethod.Put, $"/support/admin/workflows/{workflow.Id}", body with { });
        await SendAsync(client, HttpMethod.Post, $"/support/admin/workflows/{workflow.Id}/toggle");
        Assert.False((await host.QueryAsync(db => db.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflow.Id))).IsActive);

        var ids = await host.QueryAsync(db => db.Workflows.OrderByDescending(w => w.Id).Select(w => w.Id).ToListAsync());
        await SendAsync(client, HttpMethod.Post, "/support/admin/workflows/reorder", new { workflow_ids = ids });
        Assert.Equal(0, (await host.QueryAsync(db => db.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflow.Id))).Position);

        var invalid = await SendAsync(client, HttpMethod.Put, $"/support/admin/workflows/{workflow.Id}", new { name = "", actions = Array.Empty<object>() });
        Assert.Equal($"/support/admin/workflows/{workflow.Id}", invalid.Headers.Location!.ToString());
        var form = await PageAsync(client, $"/support/admin/workflows/{workflow.Id}", FlashCookie(invalid));
        Keys(form.GetProperty("props").GetProperty("errors"), "name", "trigger_event", "actions");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/workflows/{workflow.Id}");
        Assert.False(await host.QueryAsync(db => db.Workflows.AnyAsync(w => w.Id == workflow.Id)));
    }

    [Fact]
    public async Task AdminPagesNeedAnAdmin()
    {
        var (host, _) = await StartAsync();
        await using var hostScope = host;
        await host.GrantRoleAsync("agent-1", "escalated-agent");

        var response = await host.ClientFor("agent-1").SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
