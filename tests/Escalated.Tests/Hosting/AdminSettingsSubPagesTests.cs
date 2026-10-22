using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The admin Settings sub-pages against escalated-laravel's
/// <c>CsatSettingsController</c>, <c>DataRetentionController</c>,
/// <c>DatabaseConnectionController</c>, <c>EmailSettingsController</c> and
/// <c>PublicTicketsSettingsController</c>: component, every prop, the saves with
/// their flash, a refused save per page, and the admin gate.
/// <c>Admin/Settings/Sandbox</c> stays unrendered, as in Laravel.
/// </summary>
public class AdminSettingsSubPagesTests
{
    private const string Admin = "admin-1";

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(options => options.Ui.Enabled = true);
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.SeedAsync(async db =>
        {
            db.Tickets.Add(new Ticket { Reference = "ESC-00001", Subject = "Old", Status = TicketStatus.Closed, ClosedAt = DateTime.UtcNow.AddYears(-2) });
            db.Tickets.Add(new Ticket { Reference = "ESC-00002", Subject = "Open" });
            await db.SaveChangesAsync();
        });
        return (host, host.ClientFor(Admin));
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, HttpMethod method, string url, object body, string back)
    {
        var response = await client.SendAsync(Visit(method, url, body, referer: "http://localhost" + back));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(back, response.Headers.Location?.OriginalString);
        return response;
    }

    private static async Task<JsonElement> AfterAsync(HttpClient client, string url, HttpResponseMessage redirect) =>
        (await PageAsync(client, url, FlashCookie(redirect))).GetProperty("props");

    private static Task<Dictionary<string, string?>> StoredAsync(EscalatedTestHost host) =>
        host.QueryAsync(db => db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value));

    [Fact]
    public async Task Csat_RendersDefaults_SavesAndValidates()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        const string url = "/support/admin/settings/csat";

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Settings/CsatSettings");
        var settings = Keys(page.GetProperty("props").GetProperty("settings"), "csat_question_text", "csat_scale", "csat_delivery_trigger", "csat_delay_hours");
        Assert.Equal("How would you rate your support experience?", settings.GetProperty("csat_question_text").GetString());
        Assert.Equal("1-5", settings.GetProperty("csat_scale").GetString());
        Assert.Equal("on_resolve", settings.GetProperty("csat_delivery_trigger").GetString());
        Assert.Equal(0, settings.GetProperty("csat_delay_hours").GetInt32());

        var saved = await SaveAsync(client, HttpMethod.Post, url,
            new { csat_question_text = "How did we do?", csat_scale = "1-3", csat_delivery_trigger = "delayed", csat_delay_hours = 24 }, url);
        var after = await AfterAsync(client, url, saved);
        Assert.Equal("CSAT settings updated.", after.GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal(24, after.GetProperty("settings").GetProperty("csat_delay_hours").GetInt32());
        var stored = await StoredAsync(host);
        Assert.Equal("How did we do?", stored["csat_question_text"]);
        Assert.Equal("1-3", stored["csat_scale"]);
        Assert.Equal("delayed", stored["csat_delivery_trigger"]);
        Assert.Equal("24", stored["csat_delay_hours"]);

        var bad = await SaveAsync(client, HttpMethod.Post, url,
            new { csat_question_text = "", csat_scale = "1-10", csat_delivery_trigger = "sometimes", csat_delay_hours = 169 }, url);
        Keys((await AfterAsync(client, url, bad)).GetProperty("errors"), "csat_question_text", "csat_scale", "csat_delivery_trigger", "csat_delay_hours");
        Assert.Equal("1-3", (await StoredAsync(host))["csat_scale"]);
    }

    [Fact]
    public async Task DataRetention_RendersSettingsAndPreview_SavesAndValidates()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        const string url = "/support/admin/settings/data-retention";

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Settings/DataRetention");
        var props = page.GetProperty("props");
        var settings = Keys(props.GetProperty("settings"), "retention_closed_tickets", "retention_attachments", "retention_audit_logs", "retention_user_data_gdpr");
        Assert.Equal("never", settings.GetProperty("retention_closed_tickets").GetString());
        Assert.Equal("never", settings.GetProperty("retention_attachments").GetString());
        Assert.Equal("never", settings.GetProperty("retention_audit_logs").GetString());
        Assert.False(settings.GetProperty("retention_user_data_gdpr").GetBoolean());
        var preview = Keys(props.GetProperty("purgePreview"), "tickets", "attachments", "audit_logs");
        Assert.Equal(0, preview.GetProperty("tickets").GetInt32());

        var saved = await SaveAsync(client, HttpMethod.Post, url, new
        {
            retention_closed_tickets = "1_year",
            retention_attachments = "2_years",
            retention_audit_logs = "90_days",
            retention_user_data_gdpr = true,
        }, url);
        var after = await AfterAsync(client, url, saved);
        Assert.Equal("Data retention settings updated.", after.GetProperty("flash").GetProperty("success").GetString());
        Assert.True(after.GetProperty("settings").GetProperty("retention_user_data_gdpr").GetBoolean());
        Assert.Equal(1, after.GetProperty("purgePreview").GetProperty("tickets").GetInt32());
        var stored = await StoredAsync(host);
        Assert.Equal("1_year", stored["retention_closed_tickets"]);
        Assert.Equal("2_years", stored["retention_attachments"]);
        Assert.Equal("90_days", stored["retention_audit_logs"]);
        Assert.Equal("1", stored["retention_user_data_gdpr"]);

        var bad = await SaveAsync(client, HttpMethod.Post, url, new
        {
            retention_closed_tickets = "90_days",
            retention_attachments = "forever",
            retention_audit_logs = "1_year",
        }, url);
        Keys((await AfterAsync(client, url, bad)).GetProperty("errors"),
            "retention_closed_tickets", "retention_attachments", "retention_audit_logs", "retention_user_data_gdpr");
        Assert.Equal("1_year", (await StoredAsync(host))["retention_closed_tickets"]);
    }

    [Fact]
    public async Task DatabaseConnection_DescribesTheConnection_AndRefusesASwitch()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        const string url = "/support/admin/settings/database";

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Settings/DatabaseConnection");
        var props = page.GetProperty("props");
        Assert.Equal("default", props.GetProperty("current").GetString());
        Assert.Equal("default", props.GetProperty("hostDefault").GetString());
        Assert.True(props.GetProperty("pinnedByConfig").GetBoolean());
        Assert.Equal("escalated_", props.GetProperty("tablePrefix").GetString());
        var connection = Assert.Single(props.GetProperty("connections").EnumerateArray().ToList());
        Keys(connection, "name", "driver", "database", "is_current", "is_host_default", "reachable", "migrated", "ticket_count", "error");
        Assert.Equal("default", connection.GetProperty("name").GetString());
        Assert.True(connection.GetProperty("is_current").GetBoolean());
        Assert.True(connection.GetProperty("reachable").GetBoolean());
        Assert.True(connection.GetProperty("migrated").GetBoolean());
        Assert.Equal(2, connection.GetProperty("ticket_count").GetInt32());
        Assert.Equal(JsonValueKind.Null, connection.GetProperty("error").ValueKind);
        Assert.False(string.IsNullOrEmpty(connection.GetProperty("driver").GetString()));
        Assert.DoesNotContain("Password", connection.GetProperty("database").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

        var refused = await SaveAsync(client, HttpMethod.Post, url, new { connection = "default" }, url);
        Assert.StartsWith("The database connection is set in configuration",
            (await AfterAsync(client, url, refused)).GetProperty("flash").GetProperty("error").GetString());

        // An API client gets the description; an unknown or missing name is a 422.
        var test = await client.PostAsJsonAsync(url + "/test", new { connection = "default" });
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var described = (await JsonDocument.ParseAsync(await test.Content.ReadAsStreamAsync())).RootElement;
        Assert.True(described.GetProperty("reachable").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(url + "/test", new { connection = "reporting" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(url + "/test", new { })).StatusCode);

        // A page visit goes back to the page, which probes again.
        var visit = await SaveAsync(client, HttpMethod.Post, url + "/test", new { connection = "default" }, url);
        Assert.Equal("Connected to [default].", (await AfterAsync(client, url, visit)).GetProperty("flash").GetProperty("success").GetString());
        var unknown = await SaveAsync(client, HttpMethod.Post, url + "/test", new { connection = "reporting" }, url);
        Keys((await AfterAsync(client, url, unknown)).GetProperty("errors"), "connection");
    }

    [Fact]
    public async Task EmailSettings_StoresNormalisedAddresses_AndValidates()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        const string url = "/support/admin/settings/email";

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Settings/EmailSettings");
        Assert.Empty(page.GetProperty("props").GetProperty("addresses").EnumerateArray());
        Assert.Equal("", page.GetProperty("props").GetProperty("defaultReplyAddress").GetString());

        var saved = await SaveAsync(client, HttpMethod.Post, url, new
        {
            addresses = new object[]
            {
                new { email = "support@example.com", display_name = "Support", department_id = 3, dkim_status = "verified", extra = "x" },
                new { email = "billing@example.com" },
            },
            default_reply_address = "support@example.com",
        }, url);
        var after = await AfterAsync(client, url, saved);
        Assert.Equal("Email settings updated.", after.GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal("support@example.com", after.GetProperty("defaultReplyAddress").GetString());
        var addresses = after.GetProperty("addresses").EnumerateArray().ToList();
        Assert.Equal(2, addresses.Count);
        Assert.Equal(new[] { "department_id", "display_name", "dkim_status", "email" },
            addresses[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, addresses[0].GetProperty("department_id").GetInt32());
        Assert.Equal("verified", addresses[0].GetProperty("dkim_status").GetString());
        Assert.Equal("unknown", addresses[1].GetProperty("dkim_status").GetString());
        Assert.Equal(JsonValueKind.Null, addresses[1].GetProperty("display_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, addresses[1].GetProperty("department_id").ValueKind);

        // Leaving the reply address out keeps it.
        await SaveAsync(client, HttpMethod.Post, url, new { addresses = new[] { new { email = "help@example.com" } } }, url);
        Assert.Equal("support@example.com", (await StoredAsync(host))["default_reply_address"]);

        var bad = await SaveAsync(client, HttpMethod.Post, url, new
        {
            addresses = new object[] { new { email = "not-an-email", dkim_status = "great" } },
            default_reply_address = "nope",
        }, url);
        Keys((await AfterAsync(client, url, bad)).GetProperty("errors"), "addresses.0.email", "addresses.0.dkim_status", "default_reply_address");
        Assert.Contains("help@example.com", (await StoredAsync(host))["email_addresses"]);
    }

    [Fact]
    public async Task PublicTickets_RendersThePage_AcceptsPostAndPut_AndValidates()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        const string url = "/support/admin/settings/public-tickets";

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Settings/PublicTickets");
        var settings = Keys(page.GetProperty("props").GetProperty("settings"), "guest_policy_mode", "guest_policy_user_id", "guest_policy_signup_url_template");
        Assert.Equal("unassigned", settings.GetProperty("guest_policy_mode").GetString());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("guest_policy_user_id").ValueKind);

        var saved = await SaveAsync(client, HttpMethod.Post, url,
            new { guest_policy_mode = "guest_user", guest_policy_user_id = "42", guest_policy_signup_url_template = "ignored" }, url);
        var after = await AfterAsync(client, url, saved);
        Assert.Equal("Guest policy updated.", after.GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal("42", after.GetProperty("settings").GetProperty("guest_policy_user_id").GetString());
        Assert.Equal("", (await StoredAsync(host))["guest_policy_signup_url_template"]);

        await SaveAsync(client, HttpMethod.Put, url,
            new { guest_policy_mode = "prompt_signup", guest_policy_signup_url_template = "https://example.com/join" }, url);
        var stored = await StoredAsync(host);
        Assert.Equal("prompt_signup", stored["guest_policy_mode"]);
        Assert.Equal("", stored["guest_policy_user_id"]);

        var bad = await SaveAsync(client, HttpMethod.Post, url, new { guest_policy_mode = "guest_user", guest_policy_user_id = "0" }, url);
        Keys((await AfterAsync(client, url, bad)).GetProperty("errors"), "guest_policy_user_id");
        var unknown = await SaveAsync(client, HttpMethod.Post, url,
            new { guest_policy_mode = "everyone", guest_policy_signup_url_template = new string('a', 501) }, url);
        Keys((await AfterAsync(client, url, unknown)).GetProperty("errors"), "guest_policy_mode", "guest_policy_signup_url_template");
        Assert.Equal("prompt_signup", (await StoredAsync(host))["guest_policy_mode"]);

        // The JSON API on the same path is unchanged.
        var api = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.False(api.Headers.Contains("X-Inertia"));
    }

    [Fact]
    public async Task SubPages_AreAdminOnly()
    {
        var (host, _) = await StartAsync();
        await using var hostScope = host;
        var agent = host.ClientFor("agent-1");

        foreach (var path in new[] { "csat", "data-retention", "database", "email", "public-tickets" })
        {
            var url = "/support/admin/settings/" + path;
            Assert.Equal(HttpStatusCode.Forbidden, (await agent.SendAsync(Visit(HttpMethod.Get, url))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await agent.SendAsync(Visit(HttpMethod.Post, url, new { }))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await agent.SendAsync(Visit(HttpMethod.Post, "/support/admin/settings/database/test", new { connection = "default" }))).StatusCode);
    }

    [Fact]
    public async Task SubPages_HaveLaravelsRouteNames()
    {
        foreach (var (name, path) in new Dictionary<string, string>
        {
            ["escalated.admin.settings.csat"] = "{prefix}/admin/settings/csat",
            ["escalated.admin.settings.csat.update"] = "{prefix}/admin/settings/csat",
            ["escalated.admin.settings.data-retention"] = "{prefix}/admin/settings/data-retention",
            ["escalated.admin.settings.data-retention.update"] = "{prefix}/admin/settings/data-retention",
            ["escalated.admin.settings.database"] = "{prefix}/admin/settings/database",
            ["escalated.admin.settings.database.test"] = "{prefix}/admin/settings/database/test",
            ["escalated.admin.settings.database.update"] = "{prefix}/admin/settings/database",
            ["escalated.admin.settings.email"] = "{prefix}/admin/settings/email",
            ["escalated.admin.settings.email.update"] = "{prefix}/admin/settings/email",
            ["escalated.admin.settings.public-tickets"] = "{prefix}/admin/settings/public-tickets",
            ["escalated.admin.settings.public-tickets.update"] = "{prefix}/admin/settings/public-tickets",
        })
        {
            Assert.Equal(path, UiRoutes.Defaults[name]);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetentionPurge_RunsTheSettingsSavedOnThePage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        await SaveAsync(client, HttpMethod.Post, "/support/admin/settings/data-retention", new
        {
            retention_closed_tickets = "1_year",
            retention_attachments = "never",
            retention_audit_logs = "never",
            retention_user_data_gdpr = false,
        }, "/support/admin/settings/data-retention");

        var purger = new RetentionPurgeBackgroundService(host.Services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RetentionPurgeBackgroundService>.Instance);
        var report = await purger.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.ClosedTickets);
        var deleted = await host.QueryAsync(db => db.Tickets.IgnoreQueryFilters().Where(t => t.DeletedAt != null).Select(t => t.Reference).ToListAsync());
        Assert.Equal(new[] { "ESC-00001" }, deleted);
    }
}
