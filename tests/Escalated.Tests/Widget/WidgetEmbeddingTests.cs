using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Widget;

/// <summary>
/// Embedding the widget on another site: CORS for listed origins on every
/// widget route, guest forcing, host metadata, <c>/config</c>, <c>embed.js</c>
/// and the admin setting. Mirrors escalated-laravel's WidgetEmbeddingTest.
/// The test server answers as <c>http://localhost</c>.
/// </summary>
public class WidgetEmbeddingTests
{
    private const string Shop = "https://shop.example.com";

    [Fact]
    public async Task AllowedPreflight_GetsTheGrant()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Preflight("/support/widget/tickets", Shop));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("GET, POST, OPTIONS", Header(response, "Access-Control-Allow-Methods"));
        Assert.Equal("Accept, Authorization, Content-Type, X-Requested-With", Header(response, "Access-Control-Allow-Headers"));
        Assert.Equal("600", Header(response, "Access-Control-Max-Age"));
        Assert.Contains("Origin", Header(response, "Vary"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task AllowedPreflight_CoversTheChatRoutes()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Preflight("/support/widget/chat/start", Shop));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task UnlistedPreflight_GetsNoGrant()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Preflight("/support/widget/tickets", "https://evil.example.net"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task AllowedTicketPost_WorksAndCarriesCorsHeaders()
    {
        await using var host = await StartAsync();

        var response = await PostTicketAsync(host, Shop);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("Retry-After", Header(response, "Access-Control-Expose-Headers"));
        Assert.Contains("Origin", Header(response, "Vary"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task UnlistedOrigin_IsHandledAsBeforeWithoutAGrant()
    {
        await using var host = await StartAsync();

        var response = await PostTicketAsync(host, "https://evil.example.net");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SameOriginAndNoOriginRequests_AreUntouched()
    {
        await using var host = await StartAsync(o => o.Widget.AllowedOrigins.Add("http://localhost"));

        var same = await PostTicketAsync(host, "http://localhost");
        var none = await PostTicketAsync(host, null);

        Assert.Equal(HttpStatusCode.Created, same.StatusCode);
        Assert.Null(Header(same, "Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.Created, none.StatusCode);
        Assert.Null(Header(none, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OriginsFromTheAdminSetting_AreAllowed()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await SetAsync(host, "widget_allowed_origins", "https://blog.example.com\nhttps://*.example.org");

        var listed = await PostTicketAsync(host, "https://blog.example.com");
        var wildcard = await PostTicketAsync(host, "https://help.eu.example.org");
        var apex = await PostTicketAsync(host, "https://example.org");

        Assert.Equal("https://blog.example.com", Header(listed, "Access-Control-Allow-Origin"));
        Assert.Equal("https://help.eu.example.org", Header(wildcard, "Access-Control-Allow-Origin"));
        Assert.Null(Header(apex, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task WildcardOriginsFromOptions_AreAllowed()
    {
        await using var host = await StartAsync(o => o.Widget.AllowedOrigins.Add("https://*.brand.test"));

        var response = await host.Client.SendAsync(Get("/support/widget/config", "https://help.brand.test"));

        Assert.Equal("https://help.brand.test", Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ErrorResponses_CarryTheGrant()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 1);

        var notFound = await host.Client.SendAsync(Get("/support/widget/tickets/no-such-token", Shop));
        // Counted by the guest limit: the limiter runs before the body is read.
        var invalid = await PostTicketAsync(host, Shop, metadata: "not an object");
        var throttled = await PostTicketAsync(host, Shop);

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(Shop, Header(notFound, "Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(Shop, Header(invalid, "Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(Shop, Header(throttled, "Access-Control-Allow-Origin"));
        Assert.Equal("Retry-After", Header(throttled, "Access-Control-Expose-Headers"));
        Assert.NotNull(Header(throttled, "Retry-After"));
    }

    [Fact]
    public async Task AllowedCrossOriginRequests_NeverActAsTheSignedInUser()
    {
        await using var host = await StartAsync(configureServices: AddProbe);
        var client = host.ClientFor("42");

        var sameSite = await client.SendAsync(Get("/support/widget/probe-user", null));
        var embedded = await client.SendAsync(Get("/support/widget/probe-user", Shop));
        var unlisted = await client.SendAsync(Get("/support/widget/probe-user", "https://evil.example.net"));

        Assert.Equal("42", await sameSite.Content.ReadAsStringAsync());
        Assert.Equal("guest", await embedded.Content.ReadAsStringAsync());
        Assert.Equal("42", await unlisted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VaryOrigin_IsAppendedToAnExistingVary()
    {
        await using var host = await StartAsync(configureServices: AddProbe);

        var response = await host.Client.SendAsync(Get("/support/widget/probe-vary", Shop));

        Assert.Equal("Accept-Encoding, Origin", Header(response, "Vary"));
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RoutesOutsideTheWidget_GetNoGrant()
    {
        await using var host = await StartAsync();

        var preflight = await host.Client.SendAsync(Preflight("/support/admin/settings", Shop));

        Assert.Null(Header(preflight, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task TicketMetadata_IsSanitisedAndStampedWithTheOrigin()
    {
        await using var host = await StartAsync();
        var metadata = new Dictionary<string, object?>
        {
            ["order_number"] = "  A-1001  ",
            ["plan"] = new string('p', 600),
            ["seats"] = 5,
            ["ratio"] = 1.5,
            ["trial"] = true,
            ["page_url"] = "https://shop.example.com/checkout?step=2",
            ["embedded_on"] = "https://spoofed.example",
            ["nested"] = new { a = 1 },
            ["list"] = new[] { 1, 2 },
            ["empty"] = null,
            ["bad key!"] = "x",
        };

        var response = await PostTicketAsync(host, Shop, metadata);
        response.EnsureSuccessStatusCode();

        using var stored = await StoredTicketMetadataAsync(host);
        var root = stored.RootElement;
        Assert.Equal("A-1001", root.GetProperty("order_number").GetString());
        Assert.Equal(500, root.GetProperty("plan").GetString()!.Length);
        Assert.Equal(5, root.GetProperty("seats").GetInt32());
        Assert.Equal(1.5, root.GetProperty("ratio").GetDouble());
        Assert.True(root.GetProperty("trial").GetBoolean());
        Assert.Equal("https://shop.example.com/checkout?step=2", root.GetProperty("page_url").GetString());
        Assert.Equal(Shop, root.GetProperty("embedded_on").GetString());
        Assert.False(root.TryGetProperty("nested", out _));
        Assert.False(root.TryGetProperty("list", out _));
        Assert.False(root.TryGetProperty("empty", out _));
        Assert.False(root.TryGetProperty("bad key!", out _));
    }

    [Fact]
    public async Task TicketMetadata_DropsNonWebPageUrlsAndClientEmbeddedOn()
    {
        await using var host = await StartAsync();

        var response = await PostTicketAsync(host, null, new Dictionary<string, object?>
        {
            ["page_url"] = "javascript:alert(1)",
            ["embedded_on"] = "https://spoofed.example",
            ["ref"] = "x",
        });
        response.EnsureSuccessStatusCode();

        using var stored = await StoredTicketMetadataAsync(host);
        Assert.False(stored.RootElement.TryGetProperty("page_url", out _));
        Assert.False(stored.RootElement.TryGetProperty("embedded_on", out _));
        Assert.Equal("x", stored.RootElement.GetProperty("ref").GetString());
    }

    [Fact]
    public async Task TicketMetadata_KeepsAtMost25Entries()
    {
        await using var host = await StartAsync();
        var metadata = Enumerable.Range(1, 40).ToDictionary(i => $"k{i}", i => (object?)i);

        (await PostTicketAsync(host, null, metadata)).EnsureSuccessStatusCode();

        using var stored = await StoredTicketMetadataAsync(host);
        Assert.Equal(25, stored.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task TicketMetadata_RejectsNonObjectsAndMoreThan50Keys()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.Enabled = false);

        var list = await PostTicketAsync(host, null, new[] { 1, 2 });
        var tooMany = await PostTicketAsync(host, null, Enumerable.Range(1, 51).ToDictionary(i => $"k{i}", i => (object?)i));
        var none = await PostTicketAsync(host, null);

        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.Created, none.StatusCode);
        Assert.Equal(1, await host.QueryAsync(db => db.Tickets.CountAsync()));
        Assert.Null(await host.QueryAsync(db => db.Tickets.Select(t => t.Metadata).SingleAsync()));
    }

    [Fact]
    public async Task ChatStart_StoresMetadataOnTheChat()
    {
        await using var host = await StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/support/widget/chat/start")
        {
            Content = JsonContent.Create(await GuestProof.ProvedAsync(host, "/support/widget/chat/start", new
            {
                name = "Visitor",
                email = "v@example.com",
                message = "hello",
                metadata = new { plan = "pro", embedded_on = "https://spoofed.example" },
            })),
        };
        request.Headers.Add("Origin", Shop);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
        var stored = await host.QueryAsync(db => db.ChatSessions.Select(s => s.Metadata).SingleAsync());
        using var json = JsonDocument.Parse(stored!);
        Assert.Equal("pro", json.RootElement.GetProperty("plan").GetString());
        Assert.Equal(Shop, json.RootElement.GetProperty("embedded_on").GetString());
    }

    [Fact]
    public async Task ChatStart_RejectsMetadataThatIsNotAnObject()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync("/support/widget/chat/start", new { name = "V", metadata = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.ChatSessions.CountAsync()));
    }

    [Fact]
    public async Task Config_AdvertisesMetadataAndTheWidgetSettings()
    {
        await using var host = await StartAsync();
        await host.SeedAsync(async db =>
        {
            db.Departments.Add(new Department { Name = "Sales", Slug = "sales", IsActive = true });
            db.Departments.Add(new Department { Name = "Archived", Slug = "archived", IsActive = false });
            await db.SaveChangesAsync();
        });
        await SetAsync(host, "widget_color", "#111111");

        var response = await host.Client.SendAsync(Get("/support/widget/config", Shop));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Shop, Header(response, "Access-Control-Allow-Origin"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("accepts_metadata").GetBoolean());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal("#111111", root.GetProperty("color").GetString());
        Assert.Equal("bottom-right", root.GetProperty("position").GetString());
        Assert.Equal("Hi there! How can we help?", root.GetProperty("greeting").GetString());
        Assert.True(root.GetProperty("kb_enabled").GetBoolean());
        Assert.True(root.GetProperty("guest_tickets_enabled").GetBoolean());
        Assert.True(root.GetProperty("guest_verification_required").GetBoolean());
        var department = Assert.Single(root.GetProperty("departments").EnumerateArray());
        Assert.Equal("Sales", department.GetProperty("name").GetString());
        Assert.True(department.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Config_FollowsTheKnowledgeBaseOption()
    {
        await using var host = await StartAsync(o => o.KnowledgeBase.Enabled = false);

        using var json = JsonDocument.Parse(await host.Client.GetStringAsync("/support/widget/config"));

        Assert.False(json.RootElement.GetProperty("kb_enabled").GetBoolean());
    }

    [Fact]
    public async Task EmbedScript_IsServedForAnySite()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Get("/support/widget/embed.js", "https://evil.example.net"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("public, max-age=3600", Header(response, "Cache-Control"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("cross-origin", Header(response, "Cross-Origin-Resource-Policy"));
        Assert.Equal("*", Header(response, "Access-Control-Allow-Origin"));
        Assert.NotNull(response.Headers.ETag);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(body.Length > 10_000);
    }

    [Fact]
    public async Task EmbedScript_KeepsItsWildcardGrantForListedOrigins()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Get("/support/widget/embed.js", Shop));

        Assert.Equal("*", Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task EmbedScript_AnswersAMatchingETagWith304()
    {
        await using var host = await StartAsync();
        var first = await host.Client.GetAsync("/support/widget/embed.js");
        var etag = first.Headers.ETag!;

        var request = Get("/support/widget/embed.js", null);
        request.Headers.IfNoneMatch.Add(etag);
        var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(etag, second.Headers.ETag);
        Assert.Equal("public, max-age=3600", Header(second, "Cache-Control"));
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AdminSettings_SaveAndReturnTheAllowedOrigins()
    {
        await using var host = await StartAsync();
        await host.GrantRoleAsync("1", "escalated-admin");
        var admin = host.ClientFor("1");

        var save = await admin.PostAsJsonAsync("/support/admin/settings", new Dictionary<string, string?>
        {
            ["widget_allowed_origins"] = "https://Blog.Example.com/\nhttps://*.example.org, http://localhost:5173",
        });
        var settings = await admin.GetFromJsonAsync<Dictionary<string, string?>>("/support/admin/settings");

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("https://blog.example.com\nhttps://*.example.org\nhttp://localhost:5173", settings!["widget_allowed_origins"]);
        var embedded = await PostTicketAsync(host, "https://blog.example.com");
        Assert.Equal("https://blog.example.com", Header(embedded, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://shop.example.com/checkout")]
    [InlineData("https://shop.example.com\nshop.example.org")]
    public async Task AdminSettings_RejectInvalidOrigins(string value)
    {
        await using var host = await StartAsync();
        await host.GrantRoleAsync("1", "escalated-admin");
        var admin = host.ClientFor("1");

        var save = await admin.PostAsJsonAsync("/support/admin/settings", new Dictionary<string, string?>
        {
            ["widget_greeting"] = "Hello",
            ["widget_allowed_origins"] = value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        using var problem = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("widget_allowed_origins", out _));
        Assert.Equal(0, await host.QueryAsync(db => db.Settings.CountAsync()));
    }

    private static Task<EscalatedTestHost> StartAsync(
        Action<EscalatedOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null) =>
        EscalatedTestHost.StartAsync(o =>
        {
            o.Widget.AllowedOrigins.Add(Shop);
            configure?.Invoke(o);
        }, configureServices);

    private static void AddProbe(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(WidgetUserProbeController).Assembly);

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    private static HttpRequestMessage Get(string path, string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    private static async Task<HttpResponseMessage> PostTicketAsync(EscalatedTestHost host, string? origin, object? metadata = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/support/widget/tickets")
        {
            Content = JsonContent.Create(await GuestProof.ProvedAsync(host, "/support/widget/tickets", new
            {
                subject = "Help",
                description = "d",
                name = "Guest",
                email = "guest@example.com",
                metadata,
            })),
        };
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await host.Client.SendAsync(request);
    }

    private static async Task<JsonDocument> StoredTicketMetadataAsync(EscalatedTestHost host)
    {
        var stored = await host.QueryAsync(db => db.Tickets.Select(t => t.Metadata).SingleAsync());
        Assert.NotNull(stored);
        return JsonDocument.Parse(stored!);
    }

    private static Task SetAsync(EscalatedTestHost host, string key, string value) =>
        host.SeedAsync(db => new SettingsService(db).SetAsync(key, value));

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
        {
            return string.Join(", ", values);
        }

        return null;
    }
}

/// <summary>Reports who a widget request acts as.</summary>
[ApiController]
[Route("support/widget/probe-user")]
[AllowAnonymous]
public sealed class WidgetUserProbeController : ControllerBase
{
    [HttpGet("/support/widget/probe-vary")]
    public string Vary()
    {
        Response.Headers.Vary = "Accept-Encoding";
        return "ok";
    }

    [HttpGet]
    public string Get() => User.Identity?.IsAuthenticated == true
        ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "?"
        : "guest";
}
