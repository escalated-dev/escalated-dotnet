using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The Inertia protocol over the real pipeline: the HTML first visit, the JSON
/// page object for Inertia visits, the asset version check, partial reloads,
/// redirects after a form post, flash and validation errors, and the shared
/// props, plus <c>Escalated:Ui:Enabled</c> off leaving the JSON API as it was.
/// </summary>
public class InertiaProtocolTests
{
    private const string Agent = "agent-1";

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync(
        bool enabled = true, string? version = null, Action<IServiceCollection>? services = null)
    {
        var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = enabled;
            options.Ui.Version = version;
            options.Ui.HeadHtml = "<script type=\"module\" src=\"/build/app.js\"></script>";
        }, services);
        await host.GrantRoleAsync(Agent, "escalated-agent");
        return (host, host.ClientFor(Agent, ("name", "Avery Agent"), ("email", "avery@example.com")));
    }

    private static async Task<int> SeedTicketAsync(EscalatedTestHost host, string reference = "ESC-00001", string subject = "Printer on fire")
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket { Reference = reference, Subject = subject, GuestName = "Gina", GuestEmail = "gina@example.com" };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            id = ticket.Id;
        });
        return id;
    }

    [Fact]
    public async Task WithTheUiOff_PageRoutesDoNotExist_AndTheApiIsUnchanged()
    {
        var (host, client) = await StartAsync(enabled: false);
        await using var hostScope = host;
        await SeedTicketAsync(host);

        var page = await client.SendAsync(Visit(HttpMethod.Get, "/support/agent"));
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);

        var show = await client.SendAsync(Visit(HttpMethod.Get, "/support/agent/tickets/ESC-00001"));
        Assert.Equal(HttpStatusCode.NotFound, show.StatusCode);

        // Even an Inertia visit reaches the JSON API while the UI is off.
        var index = await client.SendAsync(Visit(HttpMethod.Get, "/support/agent/tickets"));
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        using var json = JsonDocument.Parse(await index.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("perPage", out _));
        Assert.False(index.Headers.Contains("X-Inertia"));
    }

    [Fact]
    public async Task AFirstVisit_GetsTheRootViewWithThePageObject()
    {
        var (host, client) = await StartAsync(version: "build-1");
        await using var hostScope = host;
        await SeedTicketAsync(host, subject: "Breaks </script> tags");

        var request = new HttpRequestMessage(HttpMethod.Get, "/support/agent");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<script type=\"module\" src=\"/build/app.js\"></script>", html);
        Assert.Contains("window.route=function", html);
        Assert.Contains("\"escalated.agent.tickets.show\":\"/support/agent/tickets/{ticket}\"", html);
        Assert.Contains("<div id=\"app\" data-page=\"", html);
        Assert.DoesNotContain("Breaks </script>", html);

        // Inertia 3 boots from the script element; check it holds the whole page.
        var start = html.IndexOf("<script data-page=\"app\" type=\"application/json\">", StringComparison.Ordinal);
        Assert.True(start >= 0, html);
        start = html.IndexOf('>', start) + 1;
        var json = html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)];
        using var page = JsonDocument.Parse(json);
        Assert.Equal("Escalated/Agent/Dashboard", page.RootElement.GetProperty("component").GetString());
        Assert.Equal("/support/agent", page.RootElement.GetProperty("url").GetString());
        Assert.Equal("build-1", page.RootElement.GetProperty("version").GetString());
        Assert.Equal("Breaks </script> tags",
            page.RootElement.GetProperty("props").GetProperty("recentTickets")[0].GetProperty("subject").GetString());
    }

    [Fact]
    public async Task AnInertiaVisit_GetsThePageObjectAsJson()
    {
        var (host, client) = await StartAsync(version: "build-1");
        await using var hostScope = host;

        var request = Visit(HttpMethod.Get, "/support/agent?tab=1");
        request.Headers.Add("X-Inertia-Version", "build-1");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("X-Inertia").Single());
        Assert.Contains("X-Inertia", response.Headers.Vary);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Escalated/Agent/Dashboard", page.RootElement.GetProperty("component").GetString());
        Assert.Equal("/support/agent?tab=1", page.RootElement.GetProperty("url").GetString());
        Assert.Equal("build-1", page.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AStaleAssetVersion_Gets409WithTheLocation()
    {
        var (host, client) = await StartAsync(version: "build-2");
        await using var hostScope = host;

        var request = Visit(HttpMethod.Get, "/support/agent");
        request.Headers.Add("X-Inertia-Version", "build-1");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("/support/agent", response.Headers.GetValues("X-Inertia-Location").Single());
    }

    [Fact]
    public async Task APartialReload_GetsOnlyTheNamedProps()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var request = Visit(HttpMethod.Get, "/support/agent/tickets");
        request.Headers.Add("X-Inertia-Partial-Component", "Escalated/Agent/TicketIndex");
        request.Headers.Add("X-Inertia-Partial-Data", "tickets");
        var response = await client.SendAsync(request);

        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = page.RootElement.GetProperty("props").EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "errors", "tickets" }, names);
    }

    [Fact]
    public async Task ThePageSharesAuthEscalatedFlashAndErrors()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var page = await PageAsync(client, "/support/agent");
        var props = page.GetProperty("props");

        var user = props.GetProperty("auth").GetProperty("user");
        Assert.Equal(Agent, user.GetProperty("id").GetString());
        Assert.Equal("Avery Agent", user.GetProperty("name").GetString());
        Assert.Equal("avery@example.com", user.GetProperty("email").GetString());

        var escalated = Keys(props.GetProperty("escalated"), "prefix", "is_agent", "is_admin", "permissions", "features",
            "guest_tickets_enabled", "show_powered_by", "knowledge_base_enabled", "knowledge_base_public", "agent_type", "broadcasting");
        Assert.Equal("support", escalated.GetProperty("prefix").GetString());
        Assert.True(escalated.GetProperty("is_agent").GetBoolean());
        Assert.False(escalated.GetProperty("is_admin").GetBoolean());
        Assert.False(escalated.GetProperty("features").GetProperty("newsletters").GetBoolean());

        Keys(props.GetProperty("flash"), "success", "error");
        Assert.Equal(JsonValueKind.Object, props.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task AFormPost_Redirects303BackWithAFlashMessage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        await SeedTicketAsync(host);

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/agent/tickets/ESC-00001/priority",
            new { priority = "urgent" }, referer: "http://localhost/support/agent/tickets/ESC-00001?tab=activity"));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/agent/tickets/ESC-00001?tab=activity", response.Headers.Location!.ToString());

        var cookie = FlashCookie(response);
        Assert.NotNull(cookie);
        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001", cookie);
        Assert.Equal("Priority updated.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal("Priority updated.", page.GetProperty("flash").GetProperty("success").GetString());
    }

    [Fact]
    public async Task AFailedFormPost_RedirectsBackWithTheErrors()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        await SeedTicketAsync(host);

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/agent/tickets/ESC-00001/reply", new { body = "  " }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/agent/tickets/ESC-00001", response.Headers.Location!.ToString());

        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001", FlashCookie(response));
        Assert.Equal("The body field is required.", page.GetProperty("props").GetProperty("errors").GetProperty("body").GetString());

        Assert.Equal(0, await host.QueryAsync(db => Task.FromResult(db.Replies.Count())));
    }

    [Fact]
    public async Task ARedirectToAnotherHostsReferer_GoesToTheFallbackInstead()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        await SeedTicketAsync(host);

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/agent/tickets/ESC-00001/priority",
            new { priority = "low" }, referer: "https://evil.example/phish"));

        Assert.Equal("/support/agent/tickets/ESC-00001", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ARouteTheApiAlsoAnswers_IsNegotiated()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        await SeedTicketAsync(host);

        var api = await client.GetAsync("/support/agent/tickets");
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        using (var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync()))
        {
            Assert.True(json.RootElement.TryGetProperty("perPage", out _), "a plain request should still get the JSON API");
        }

        var browser = new HttpRequestMessage(HttpMethod.Get, "/support/agent/tickets");
        browser.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
        var html = await client.SendAsync(browser);
        Assert.Equal("text/html", html.Content.Headers.ContentType!.MediaType);

        var page = await PageAsync(client, "/support/agent/tickets");
        Assert.Equal("Escalated/Agent/TicketIndex", page.GetProperty("component").GetString());

        // The JSON API's own ticket routes, by id, are untouched.
        var apiShow = await client.GetAsync("/support/agent/tickets/1");
        Assert.Equal(HttpStatusCode.OK, apiShow.StatusCode);
        Assert.Equal("ESC-00001", (await apiShow.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetString());
    }

    private sealed class HostLayout : IEscalatedUiRootView
    {
        public Task<string> RenderAsync(HttpContext context, InertiaPage page, string pageJson, CancellationToken ct = default) =>
            Task.FromResult($"<html><body data-host-layout=\"{page.Component}\"></body></html>");
    }

    [Fact]
    public async Task TheHostCanReplaceTheRootView()
    {
        var (host, client) = await StartAsync(services: s => s.AddScoped<IEscalatedUiRootView, HostLayout>());
        await using var hostScope = host;

        var request = new HttpRequestMessage(HttpMethod.Get, "/support/agent");
        request.Headers.Accept.ParseAdd("text/html");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Equal("<html><body data-host-layout=\"Escalated/Agent/Dashboard\"></body></html>", html);
    }

    [Fact]
    public async Task PagesNeedTheirPolicy()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var customer = host.ClientFor("customer-1");
        var response = await customer.SendAsync(Visit(HttpMethod.Get, "/support/agent"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
