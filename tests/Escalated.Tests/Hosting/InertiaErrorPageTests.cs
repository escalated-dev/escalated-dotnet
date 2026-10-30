using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A refusal or a failure inside the panel stays inside the panel.
///
/// <para>An Inertia visit that gets an error body back has nowhere to put it: the
/// client opens it in a modal over the screen the user was on. So an Inertia visit
/// to one of Escalated's UI routes that ends in 403, 404, 419, 429, 500 or 503 gets
/// the shared frontend's <c>Escalated/Error</c> page, with the props Rails and
/// Laravel send: the status and a generic message. Full page loads, JSON clients
/// and the host's own routes are left alone.</para>
/// </summary>
public class InertiaErrorPageTests
{
    private const string Agent = "agent-1";
    private const string ProbeStatus = "X-Probe-Status";
    private const string ProbeThrow = "X-Probe-Throw";

    /// <summary>
    /// The real pipeline, plus a stand-in for failures the suite cannot otherwise
    /// provoke on a real route: after routing has matched an endpoint, a probe header
    /// ends the request with a status (and a body that must not leak) or throws.
    /// </summary>
    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync(
        bool enabled = true, string environment = "Testing")
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = enabled,
            configureApp: app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers.TryGetValue(ProbeStatus, out var status))
                    {
                        context.Response.StatusCode = int.Parse(status.ToString());
                        await context.Response.WriteAsync("upstream detail");
                        return;
                    }

                    if (context.Request.Headers.ContainsKey(ProbeThrow))
                    {
                        throw new InvalidOperationException("relation escalated_secret_table does not exist");
                    }

                    await next();
                });
                app.MapGet("/host/missing", () => Results.NotFound("host page"));
            },
            environment: environment);
        await host.GrantRoleAsync(Agent, "escalated-agent");
        return (host, host.ClientFor(Agent, ("name", "Avery Agent"), ("email", "avery@example.com")));
    }

    private static async Task<JsonElement> AssertErrorPageAsync(HttpResponseMessage response, int status, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"expected {status}, got {(int)response.StatusCode}: {text}");
        Assert.True(response.Headers.TryGetValues("X-Inertia", out var inertia), $"not an Inertia page: {text}");
        Assert.Equal("true", inertia.Single());

        var page = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal("Escalated/Error", page.GetProperty("component").GetString());
        Assert.Equal(status, page.GetProperty("props").GetProperty("status").GetInt32());
        Assert.Equal(message, page.GetProperty("props").GetProperty("message").GetString());
        return page;
    }

    [Fact]
    public async Task ARefusedInertiaVisit_GetsTheErrorPage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"));

        await AssertErrorPageAsync(response, 403, "You are not authorized to perform this action.");
    }

    [Fact]
    public async Task AnInertiaVisitToAMissingTicket_GetsTheErrorPage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/support/agent/tickets/ESC-99999"));

        await AssertErrorPageAsync(response, 404, "The requested resource was not found.");
    }

    [Theory]
    [InlineData(419, "Your session has expired. Refresh the page and try again.")]
    [InlineData(429, "Too many requests. Wait a moment and try again.")]
    [InlineData(500, "Something went wrong on our end.")]
    [InlineData(503, "The service is temporarily unavailable. Try again shortly.")]
    public async Task EachStatusThePageHasWordingFor(int status, string message)
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = Visit(HttpMethod.Get, "/support/agent");
        request.Headers.Add(ProbeStatus, status.ToString());

        var response = await client.SendAsync(request);

        await AssertErrorPageAsync(response, status, message);
        Assert.DoesNotContain("upstream detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnhandledException_GetsTheErrorPage_WithoutItsMessage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = Visit(HttpMethod.Get, "/support/agent");
        request.Headers.Add(ProbeThrow, "1");

        var response = await client.SendAsync(request);

        await AssertErrorPageAsync(response, 500, "Something went wrong on our end.");
        Assert.DoesNotContain("escalated_secret_table", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendsOnlyTheStatusAndTheMessage()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var page = await AssertErrorPageAsync(
            await client.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings")),
            403, "You are not authorized to perform this action.");
        var props = page.GetProperty("props");

        Assert.False(props.TryGetProperty("home", out _));
        Assert.False(props.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task InDevelopment_A500IsLeftToTheDeveloperExceptionPage()
    {
        var (host, client) = await StartAsync(environment: "Development");
        await using var hostScope = host;
        var request = Visit(HttpMethod.Get, "/support/agent");
        request.Headers.Add(ProbeThrow, "1");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
    }

    [Fact]
    public async Task AFullPageLoad_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = new HttpRequestMessage(HttpMethod.Get, "/support/admin/settings");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.DoesNotContain("Escalated/Error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AJsonRequest_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = new HttpRequestMessage(HttpMethod.Get, "/support/agent");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add(ProbeStatus, "403");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("upstream detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AHostRoute_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/host/missing"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.Contains("host page", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnmatchedUrl_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/support/no-such-screen/at-all/here"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
    }

    [Fact]
    public async Task AnApiRoute_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = Visit(HttpMethod.Post, "/support/api/v1/auth/login");
        request.Headers.Add(ProbeStatus, "403");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("upstream detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithTheUiOff_NothingChanges()
    {
        var (host, client) = await StartAsync(enabled: false);
        await using var hostScope = host;
        var request = Visit(HttpMethod.Get, "/support/agent/tickets");
        request.Headers.Add(ProbeStatus, "403");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("upstream detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ARedirect_IsLeftAlone()
    {
        var (host, client) = await StartAsync();
        await using var hostScope = host;
        var request = Visit(HttpMethod.Get, "/support/agent");
        request.Headers.Add(ProbeStatus, "302");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("upstream detail", await response.Content.ReadAsStringAsync());
    }
}
