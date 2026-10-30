using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Authorization;
using Escalated.Middleware;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Access;

/// <summary>
/// Two-factor authentication (ADR 2026-10-29-configurable-access-features, port test checklist),
/// through the real pipeline: switched on, and off (the default).
/// </summary>
public class TwoFactorTests
{
    /// <summary>A browser: one user, its cookies kept between requests.</summary>
    private sealed class Browser
    {
        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _cookies = new();

        public Browser(HttpClient client) => _client = client;

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            }

            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    var pair = value.Split(';')[0];
                    var name = pair[..pair.IndexOf('=')];
                    var content = pair[(pair.IndexOf('=') + 1)..];
                    if (content.Length == 0 || value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                    {
                        _cookies.Remove(name);
                    }
                    else
                    {
                        _cookies[name] = content;
                    }
                }
            }

            return response;
        }

        public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(Visit(HttpMethod.Get, url));

        public Task<HttpResponseMessage> PostAsync(string url, object body) => SendAsync(Visit(HttpMethod.Post, url, body));

        public async Task<JsonElement> PageAsync(string url)
        {
            var response = await GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url}: {(int)response.StatusCode} {text}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public void Forget(string name) => _cookies.Remove(name);
    }

    private static async Task<EscalatedTestHost> StartAsync(bool on, bool tokens = false) =>
        await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.Access.TwoFactor = on;
            },
            null,
            tokens ? app => app.UseWhen(c => c.Request.Headers.Authorization.Count > 0, b => b.UseMiddleware<ApiTokenAuthMiddleware>()) : null);

    private static async Task<Browser> StaffAsync(EscalatedTestHost host, string userId, bool admin)
    {
        await host.GrantRoleAsync(userId, admin ? "escalated-admin" : "escalated-agent");
        return new Browser(host.ClientFor(userId));
    }

    /// <summary>A confirmed enrolment; returns its base32 secret.</summary>
    private static async Task<string> EnrolAsync(EscalatedTestHost host, string userId)
    {
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
        var setup = await service.SetupAsync(userId, userId);
        var secret = Regex.Match(setup.QrUri, "secret=([A-Z2-7]+)").Groups[1].Value;
        Assert.NotNull(await service.ConfirmAsync(userId, TwoFactorService.CodeAt(secret, DateTime.UtcNow)));
        return secret;
    }

    /// <summary>A code for the next time step: inside the window, and never used yet.</summary>
    private static string NextCode(string secret) => TwoFactorService.CodeAt(secret, DateTime.UtcNow.AddSeconds(30));

    private static async Task<JsonElement> ErrorsAsync(Browser browser) =>
        (await browser.PageAsync("/support/two-factor/challenge")).GetProperty("props").GetProperty("errors");

    [Fact]
    public async Task On_AnEnrolledUserIsSentToTheChallengeFromAdminAndAgentPages()
    {
        await using var host = await StartAsync(on: true);
        var agent = await StaffAsync(host, "1", admin: false);
        var admin = await StaffAsync(host, "2", admin: true);
        await EnrolAsync(host, "1");
        await EnrolAsync(host, "2");

        var fromAgent = await agent.GetAsync("/support/agent");
        Assert.Equal(HttpStatusCode.Found, fromAgent.StatusCode);
        Assert.Equal("/support/two-factor/challenge", fromAgent.Headers.Location!.ToString());
        var fromAdmin = await admin.GetAsync("/support/admin/settings");
        Assert.Equal(HttpStatusCode.Found, fromAdmin.StatusCode);
        Assert.Equal("/support/two-factor/challenge", fromAdmin.Headers.Location!.ToString());

        // A JSON client is told, not redirected.
        var json = new HttpRequestMessage(HttpMethod.Get, "/support/agent/tickets");
        json.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var answer = await agent.SendAsync(json);
        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.True(JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement.GetProperty("two_factor_required").GetBoolean());

        var challenge = await agent.PageAsync("/support/two-factor/challenge");
        Assert.Equal("Escalated/Auth/TwoFactorChallenge", challenge.GetProperty("component").GetString());
        Assert.Equal("http://localhost/support/two-factor/challenge", challenge.GetProperty("props").GetProperty("action").GetString());
        Assert.Equal(string.Empty, challenge.GetProperty("props").GetProperty("error").GetString());
    }

    [Fact]
    public async Task On_AValidCodeReturnsToTheIntendedPageAndWorksOnce()
    {
        await using var host = await StartAsync(on: true);
        var agent = await StaffAsync(host, "1", admin: false);
        var secret = await EnrolAsync(host, "1");
        Assert.Equal(HttpStatusCode.Found, (await agent.GetAsync("/support/agent/tickets?status=open")).StatusCode);

        Assert.Equal(HttpStatusCode.SeeOther, (await agent.PostAsync("/support/two-factor/challenge", new { code = "000000" })).StatusCode);
        Assert.Equal("That code is not valid.", (await ErrorsAsync(agent)).GetProperty("code").GetString());

        var code = NextCode(secret);
        var passed = await agent.PostAsync("/support/two-factor/challenge", new { code });
        Assert.Equal(HttpStatusCode.SeeOther, passed.StatusCode);
        Assert.Equal("/support/agent/tickets?status=open", passed.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/support/agent")).StatusCode);

        // The same code (the same time step) in another browser is refused.
        var other = new Browser(host.ClientFor("1"));
        await other.PostAsync("/support/two-factor/challenge", new { code });
        Assert.Equal(HttpStatusCode.Found, (await other.GetAsync("/support/agent")).StatusCode);
    }

    [Fact]
    public async Task On_ARecoveryCodePassesOnce()
    {
        await using var host = await StartAsync(on: true);
        await StaffAsync(host, "1", admin: false);
        string recovery;
        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
            var setup = await service.SetupAsync("1", "1");
            var secret = Regex.Match(setup.QrUri, "secret=([A-Z2-7]+)").Groups[1].Value;
            await service.ConfirmAsync("1", TwoFactorService.CodeAt(secret, DateTime.UtcNow));
            recovery = setup.RecoveryCodes[0];
        }

        var first = new Browser(host.ClientFor("1"));
        await first.PostAsync("/support/two-factor/challenge", new { recovery_code = recovery });
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/support/agent")).StatusCode);

        var second = new Browser(host.ClientFor("1"));
        await second.PostAsync("/support/two-factor/challenge", new { recovery_code = recovery });
        Assert.Equal("That recovery code is not valid.", (await ErrorsAsync(second)).GetProperty("recovery_code").GetString());
        Assert.Equal(HttpStatusCode.Found, (await second.GetAsync("/support/agent")).StatusCode);
    }

    [Fact]
    public async Task On_TheVerifyRouteIsThrottled()
    {
        await using var host = await StartAsync(on: true);
        var agent = await StaffAsync(host, "1", admin: false);
        await EnrolAsync(host, "1");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.SeeOther, (await agent.PostAsync("/support/two-factor/challenge", new { code = "000000" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await agent.PostAsync("/support/two-factor/challenge", new { code = "000000" })).StatusCode);
    }

    [Fact]
    public async Task On_ANewLoginIsChallengedAgain()
    {
        await using var host = await StartAsync(on: true);
        var agent = await StaffAsync(host, "1", admin: false);
        var secret = await EnrolAsync(host, "1");
        await agent.PostAsync("/support/two-factor/challenge", new { code = NextCode(secret) });
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/support/agent")).StatusCode);

        // A new browser session (a new login) has no mark, and a pass for one user is not another's.
        Assert.Equal(HttpStatusCode.Found, (await new Browser(host.ClientFor("1")).GetAsync("/support/agent")).StatusCode);
        await StaffAsync(host, "2", admin: false);
        await EnrolAsync(host, "2");
        Assert.Equal(HttpStatusCode.Found, (await new Browser(host.ClientFor("2")).GetAsync("/support/agent")).StatusCode);

        // A host's cookie sign-in (and sign-out) clears the mark.
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get("probe");
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = host.Services };
        await options.Events.OnSignedIn(new Microsoft.AspNetCore.Authentication.Cookies.CookieSignedInContext(
            http, new Microsoft.AspNetCore.Authentication.AuthenticationScheme("probe", null,
                typeof(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationHandler)),
            new System.Security.Claims.ClaimsPrincipal(), new Microsoft.AspNetCore.Authentication.AuthenticationProperties(), options));
        Assert.Contains(http.Response.Headers.SetCookie, c => c!.StartsWith(TwoFactorSession.CookieName + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task On_AnAgentCanEnrol()
    {
        await using var host = await StartAsync(on: true);
        var agent = await StaffAsync(host, "1", admin: false);
        var index = await agent.PageAsync("/support/admin/settings/two-factor");
        Assert.Equal("Escalated/Admin/Settings/TwoFactor", index.GetProperty("component").GetString());
        Assert.False(index.GetProperty("props").GetProperty("enabled").GetBoolean());
        Assert.False(index.GetProperty("props").GetProperty("pending").GetBoolean());
        Assert.False(index.GetProperty("props").GetProperty("required").GetBoolean());

        Assert.Equal(HttpStatusCode.SeeOther, (await agent.PostAsync("/support/admin/settings/two-factor/setup", new { })).StatusCode);
        var pending = await agent.PageAsync("/support/admin/settings/two-factor");
        var setup = pending.GetProperty("props").GetProperty("flash").GetProperty("two_factor_setup");
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("qr_uri").GetString());
        Assert.Equal(8, setup.GetProperty("recovery_codes").GetArrayLength());
        Assert.True(pending.GetProperty("props").GetProperty("pending").GetBoolean());
        var secret = Regex.Match(setup.GetProperty("qr_uri").GetString()!, "secret=([A-Z2-7]+)").Groups[1].Value;
        var stored = await host.QueryAsync(db => db.TwoFactors.AsNoTracking().SingleAsync(t => t.UserId == "1"));
        Assert.DoesNotContain(secret, stored.Secret);

        Assert.Equal(HttpStatusCode.SeeOther, (await agent.PostAsync("/support/admin/settings/two-factor/confirm",
            new { code = TwoFactorService.CodeAt(secret, DateTime.UtcNow) })).StatusCode);
        var confirmed = await agent.PageAsync("/support/admin/settings/two-factor");
        Assert.True(confirmed.GetProperty("props").GetProperty("enabled").GetBoolean());
        Assert.Equal(8, confirmed.GetProperty("props").GetProperty("flash").GetProperty("two_factor_confirmed").GetProperty("recovery_codes").GetArrayLength());

        Assert.Equal(HttpStatusCode.SeeOther, (await agent.PostAsync("/support/admin/settings/two-factor/disable", new { })).StatusCode);
        Assert.False(await host.QueryAsync(db => db.TwoFactors.AnyAsync(t => t.UserId == "1")));
    }

    [Fact]
    public async Task On_WhenRequiredAnUnenrolledUserCanReachOnlyEnrolment()
    {
        await using var host = await StartAsync(on: true);
        var admin = await StaffAsync(host, "1", admin: true);
        var agent = await StaffAsync(host, "2", admin: false);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/support/agent")).StatusCode);
        Assert.False((await agent.PageAsync("/support/agent")).GetProperty("props").GetProperty("escalated").GetProperty("two_factor_required").GetBoolean());

        // The frontend's "require for all staff" switch posts only this key.
        Assert.Equal(HttpStatusCode.SeeOther, (await admin.PostAsync("/support/admin/settings", new { two_factor_required = true })).StatusCode);
        Assert.Equal("1", await host.QueryAsync(db => db.Settings.Where(s => s.Key == "two_factor_required").Select(s => s.Value).SingleAsync()));

        var sent = await agent.GetAsync("/support/agent");
        Assert.Equal(HttpStatusCode.Found, sent.StatusCode);
        Assert.Equal("/support/admin/settings/two-factor", sent.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Found, (await admin.GetAsync("/support/admin/settings")).StatusCode);
        var enrolment = await agent.PageAsync("/support/admin/settings/two-factor");
        Assert.True(enrolment.GetProperty("props").GetProperty("required").GetBoolean());
        Assert.True(enrolment.GetProperty("props").GetProperty("escalated").GetProperty("two_factor_required").GetBoolean());
    }

    [Fact]
    public async Task On_TheTokenApiIsNotChallenged_AndAResetClearsAnEnrolment()
    {
        await using var host = await StartAsync(on: true, tokens: true);
        await host.GrantRoleAsync("7", "escalated-agent");
        await EnrolAsync(host, "7");
        await host.SeedAsync(async db =>
        {
            db.ApiTokens.Add(new ApiToken { TokenableId = 7, TokenableType = "User", Name = "cli", TokenHash = ApiTokenAuthMiddleware.ComputeSha256("2fa-token") });
            await db.SaveChangesAsync();
        });
        var client = host.Client;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "2fa-token");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/support/agent/tickets/dashboard")).StatusCode);

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TwoFactorService>().ResetAsync("7");
        Assert.False(await host.QueryAsync(db => db.TwoFactors.AnyAsync(t => t.UserId == "7")));
    }

    [Fact]
    public async Task Off_NobodyIsChallengedAndTheRoutesAnswer404()
    {
        await using var host = await StartAsync(on: false);
        var agent = await StaffAsync(host, "1", admin: false);
        await EnrolAsync(host, "1");
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "two_factor_required", Value = "1" });
            db.Settings.Add(new EscalatedSettings { Key = "two_factor", Value = "1" });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/support/agent")).StatusCode);
        foreach (var path in new[] { "/support/admin/settings/two-factor", "/support/two-factor/challenge" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(path)).StatusCode);
        }

        foreach (var path in new[] { "/support/admin/settings/two-factor/setup", "/support/admin/settings/two-factor/confirm",
                     "/support/admin/settings/two-factor/disable", "/support/two-factor/challenge" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await agent.PostAsync(path, new { })).StatusCode);
        }

        var escalated = (await agent.PageAsync("/support/agent")).GetProperty("props").GetProperty("escalated");
        Assert.False(escalated.GetProperty("features").GetProperty("two_factor").GetBoolean());
        Assert.False(escalated.GetProperty("two_factor_required").GetBoolean());
        Assert.True(await host.QueryAsync(db => db.TwoFactors.AnyAsync(t => t.UserId == "1" && t.ConfirmedAt != null)));
    }
}
