using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Configuration;
using Escalated.Models;
using Escalated.Notifications;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Verified guest access, as escalated-laravel's <c>docs/guest-access.md</c>:
/// email codes, expiring hashed grants, <c>verification</c> and <c>lookup</c> on the
/// widget and guest prefixes, and the widget's on/off and guest-ticket switches.
/// Everything goes through the real HTTP pipeline; codes are read from the email.
/// </summary>
public class VerifiedGuestAccessTests
{
    private const string Email = "gail@example.com";

    private sealed class Mailbox : IEscalatedMailTransport
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();
        public bool IsConfigured => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public string LastCodeFor(string email) =>
            Regex.Match(Sent.Last(m => m.ToEmail == email && m.Subject.Contains("verification code")).Body, @"\b\d{8}\b").Value;
    }

    private static async Task<(EscalatedTestHost Host, Mailbox Mail)> StartAsync(Action<EscalatedOptions>? configure = null)
    {
        var mail = new Mailbox();
        var host = await EscalatedTestHost.StartAsync(
            o =>
            {
                o.GuestAccess.Secret = "test-guest-secret";
                configure?.Invoke(o);
            },
            services => services.AddSingleton<IEscalatedMailTransport>(mail));
        return (host, mail);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<(string Id, string Code)> RequestCodeAsync(EscalatedTestHost host, Mailbox mail, string purpose, string email = Email, string prefix = "/support/widget")
    {
        var response = await host.Client.PostAsJsonAsync($"{prefix}/verification", new { email, purpose });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("verification_id").GetString()!;
        return (id, mail.LastCodeFor(email));
    }

    private static object TicketBody(string id, string code, string email = Email) => new
    {
        name = "Gail",
        email,
        subject = "Where is my order?",
        description = "It is late",
        verification_id = id,
        verification_code = code,
    };

    private static async Task<(string Reference, string Token)> CreateTicketAsync(EscalatedTestHost host, Mailbox mail, string email = Email)
    {
        var (id, code) = await RequestCodeAsync(host, mail, "ticket", email);
        var response = await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code, email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await JsonAsync(response);
        return (json.GetProperty("reference").GetString()!, json.GetProperty("guest_access_token").GetString()!);
    }

    private static Task<HttpResponseMessage> StatusAsync(EscalatedTestHost host, string reference, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/support/widget/tickets/{Uri.EscapeDataString(reference)}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host.Client.SendAsync(request);
    }

    [Fact]
    public async Task Config_AdvertisesVerificationAndStatusLookup()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;

        var json = await JsonAsync(await host.Client.GetAsync("/support/widget/config"));

        Assert.True(json.GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("guest_verification_required").GetBoolean());
        Assert.True(json.GetProperty("status_lookup_enabled").GetBoolean());
        Assert.True(json.GetProperty("guest_tickets_enabled").GetBoolean());
    }

    [Fact]
    public async Task Verification_EmailsAnEightDigitCode_AndStoresOnlyDigests()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var response = await host.Client.PostAsJsonAsync("/support/widget/verification", new { email = "Gail@Example.com ", purpose = "ticket" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.True(Guid.TryParse(json.GetProperty("verification_id").GetString(), out _));
        Assert.Equal(600, json.GetProperty("expires_in").GetInt32());
        Assert.False(json.TryGetProperty("code", out _));
        var code = mail.LastCodeFor("Gail@Example.com");
        Assert.Matches("^[0-9]{8}$", code);

        var row = await host.QueryAsync(db => db.GuestVerifications.AsNoTracking().SingleAsync());
        Assert.DoesNotContain("gail", row.EmailDigest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(code, row.CodeDigest);
        Assert.Equal("ticket", row.Purpose);
    }

    [Theory]
    [InlineData("not-an-email", "ticket", "email")]
    [InlineData(Email, "admin", "purpose")]
    public async Task Verification_RejectsABadRequestWith422(string email, string purpose, string field)
    {
        var (host, _) = await StartAsync();
        await using var _h = host;

        var response = await host.Client.PostAsJsonAsync("/support/widget/verification", new { email, purpose });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Verification_LimitsCodesPerMailboxFromOneClient()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;

        for (var i = 0; i < 3; i++)
        {
            var ok = await host.Client.PostAsJsonAsync("/support/widget/verification", new { email = Email, purpose = "ticket" });
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        var limited = await host.Client.PostAsJsonAsync("/support/widget/verification", new { email = Email, purpose = "ticket" });
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task CreateTicket_NeedsProof_AndAnswersWithAnExpiringGrant()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var without = await host.Client.PostAsJsonAsync("/support/widget/tickets",
            new { name = "Gail", email = Email, subject = "Hi", description = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, without.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        var response = await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var json = await JsonAsync(response);
        var token = json.GetProperty("guest_access_token").GetString()!;
        Assert.True(token.Length >= 40);
        var expires = json.GetProperty("expires_at").GetDateTime();
        Assert.InRange(expires, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
        Assert.False(json.TryGetProperty("guestToken", out _));

        var ticket = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync());
        Assert.Equal(json.GetProperty("reference").GetString(), ticket.Reference);
        Assert.Equal(GuestAccessService.HashGrant(token), ticket.GuestAccessHash);
        Assert.NotEqual(token, ticket.GuestAccessHash);
    }

    [Fact]
    public async Task Proof_IsSpentOnce_AndBoundToTheEmailAndPurpose()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code, "mallory@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);

        var (chatId, chatCode) = await RequestCodeAsync(host, mail, "chat");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(chatId, chatCode))).StatusCode);
    }

    [Fact]
    public async Task Proof_AllowsFiveGuesses_AndExpires()
    {
        // More submissions than the per-IP ticket limit allows in a minute.
        var (host, mail) = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 50);
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        var wrong = code == "00000000" ? "11111111" : "00000000";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, wrong))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);

        Assert.Equal(GuestAccessService.MaxAttempts, await AttemptsAsync(host, id));

        var (id2, code2) = await RequestCodeAsync(host, mail, "ticket");
        await ExpireCodeAsync(host, id2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id2, code2))).StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Proof_ConcurrentGuesses_CountAtMostFive()
    {
        // Guesses arriving together must not slip past the limit between a read
        // and a write: the count is a single conditional UPDATE.
        var (host, mail) = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 50);
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        var wrong = code == "00000000" ? "11111111" : "00000000";
        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, wrong))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal(GuestAccessService.MaxAttempts, await AttemptsAsync(host, id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    private static Task<int> AttemptsAsync(EscalatedTestHost host, string id) =>
        host.QueryAsync(db => db.GuestVerifications.Where(v => v.Id == id).Select(v => v.Attempts).SingleAsync());

    /// <summary>
    /// Moves the code's expiry a second into the past by the app's clock, which is
    /// the clock the service compares it with. The instant is computed here, not in
    /// the query: inside an EF expression <c>DateTime.UtcNow</c> is translated to the
    /// database server's <c>now()</c>, and a server clock running a second or more
    /// ahead of the app (a Docker VM's often does) left the code unexpired.
    /// </summary>
    private static Task ExpireCodeAsync(EscalatedTestHost host, string id)
    {
        var expired = DateTime.UtcNow.AddSeconds(-1);
        return host.SeedAsync(db => db.GuestVerifications.Where(v => v.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(v => v.ExpiresAt, expired)));
    }

    [Fact]
    public async Task Status_NeedsThisTicketsUnexpiredGrant()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (reference, token) = await CreateTicketAsync(host, mail);
        var (otherReference, otherToken) = await CreateTicketAsync(host, mail, "other@example.com");

        var ok = await StatusAsync(host, reference, token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("no-referrer", ok.Headers.GetValues("Referrer-Policy").Single());
        var json = await JsonAsync(ok);
        Assert.Equal(reference, json.GetProperty("reference").GetString());
        Assert.Equal("Where is my order?", json.GetProperty("subject").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("status_label").GetString()));
        Assert.Equal(JsonValueKind.Array, json.GetProperty("replies").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(host, reference, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(host, otherReference, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(host, reference, otherToken)).StatusCode);

        // Computed by the app's clock, not translated to the server's now(); see ExpireCodeAsync.
        var expired = DateTime.UtcNow.AddMinutes(-1);
        await host.SeedAsync(db => db.Tickets.Where(t => t.Reference == reference)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.GuestAccessExpiresAt, expired)));
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(host, reference, token)).StatusCode);
    }

    [Fact]
    public async Task Reply_WithTheGrant_IsAddedToTheTicket()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (reference, token) = await CreateTicketAsync(host, mail);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/support/widget/tickets/{reference}/reply")
        {
            Content = JsonContent.Create(new { body = "Any news?" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(request)).StatusCode);

        Assert.Contains("Any news?", await host.QueryAsync(db => db.Replies.Select(r => r.Body).ToListAsync()));
        Assert.Equal(HttpStatusCode.NotFound,
            (await host.Client.PostAsJsonAsync($"/support/widget/tickets/{reference}/reply", new { body = "no grant" })).StatusCode);
    }

    [Fact]
    public async Task LegacyPermanentTokens_AreRefusedUnlessTheTransitionSettingIsOn()
    {
        foreach (var legacy in new[] { false, true })
        {
            var (host, _) = await StartAsync(o => o.GuestAccess.LegacyTokens = legacy);
            await using var _h = host;
            await host.SeedAsync(async db =>
            {
                db.Tickets.Add(new Ticket { Reference = "OLD-1", Subject = "Before", GuestEmail = Email, GuestToken = "legacy-permanent-token" });
                await db.SaveChangesAsync();
            });

            var response = await host.Client.GetAsync("/support/widget/tickets/legacy-permanent-token");

            Assert.Equal(legacy ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Lookup_IssuesANewGrant_ForTheVerifiedEmailOnly()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (reference, firstToken) = await CreateTicketAsync(host, mail);

        var (wrongId, wrongCode) = await RequestCodeAsync(host, mail, "lookup", "mallory@example.com");
        var nothing = await host.Client.PostAsJsonAsync("/support/widget/lookup",
            new { email = "mallory@example.com", reference, verification_id = wrongId, verification_code = wrongCode });
        Assert.Equal(HttpStatusCode.OK, nothing.StatusCode);
        Assert.Empty((await JsonAsync(nothing)).GetProperty("data").EnumerateArray());

        var (id, code) = await RequestCodeAsync(host, mail, "lookup");
        var response = await host.Client.PostAsJsonAsync("/support/widget/lookup",
            new { email = Email, reference, verification_id = id, verification_code = code });

        var match = Assert.Single((await JsonAsync(response)).GetProperty("data").EnumerateArray());
        Assert.Equal(reference, match.GetProperty("reference").GetString());
        var newToken = match.GetProperty("guest_access_token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(host, reference, newToken)).StatusCode);
        // The earlier grant is revoked by the new one.
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(host, reference, firstToken)).StatusCode);

        // The proof was spent.
        var again = await host.Client.PostAsJsonAsync("/support/widget/lookup",
            new { email = Email, reference, verification_id = id, verification_code = code });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
    }

    [Fact]
    public async Task GuestPrefix_ServesVerificationAndLookup()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (reference, _) = await CreateTicketAsync(host, mail);

        var (id, code) = await RequestCodeAsync(host, mail, "lookup", prefix: "/support/guest");
        var response = await host.Client.PostAsJsonAsync("/support/guest/lookup",
            new { email = Email, reference, verification_id = id, verification_code = code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await JsonAsync(response)).GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task ChatStart_NeedsAChatCode()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var without = await host.Client.PostAsJsonAsync("/support/widget/chat/start", new { name = "Vee", email = Email, message = "Hi" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, without.StatusCode);

        var (id, code) = await RequestCodeAsync(host, mail, "chat");
        var response = await host.Client.PostAsJsonAsync("/support/widget/chat/start",
            new { name = "Vee", email = Email, message = "Hi", verification_id = id, verification_code = code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LiveUpdates_ReachAGuestHoldingTheGrant_UntilItIsRevoked()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (reference, token) = await CreateTicketAsync(host, mail);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Escalated.Data.EscalatedDbContext>();
        var ticket = await db.Tickets.SingleAsync(t => t.Reference == reference);
        var audience = scope.ServiceProvider.GetRequiredService<Escalated.Hubs.TicketRealtimeAudience>();
        var broadcasts = scope.ServiceProvider.GetRequiredService<Escalated.Hubs.TicketBroadcastService>();
        audience.Add(new Escalated.Hubs.TicketSubscription("guest", $"ticket-{ticket.Id}", null, token, ticket.Id, false));
        audience.Add(new Escalated.Hubs.TicketSubscription("stranger", $"ticket-{ticket.Id}", null, "made-up", ticket.Id, false));

        Assert.Equal(new[] { "guest" }, await broadcasts.RecipientsAsync(ticket.Id, $"ticket-{ticket.Id}"));

        await scope.ServiceProvider.GetRequiredService<GuestAccessService>().RevokeAsync(ticket);
        Assert.Empty(await broadcasts.RecipientsAsync(ticket.Id, $"ticket-{ticket.Id}"));
    }

    [Fact]
    public async Task WidgetSwitchedOff_RefusesEveryWidgetRoute()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "widget_enabled", Value = "0" });
            await db.SaveChangesAsync();
        });

        var config = await host.Client.GetAsync("/support/widget/config");
        Assert.Equal(HttpStatusCode.Forbidden, config.StatusCode);
        Assert.False((await JsonAsync(config)).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/support/widget/verification", new { email = Email, purpose = "ticket" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/support/widget/chat/start", new { name = "V", email = Email })).StatusCode);
    }

    [Fact]
    public async Task GuestTicketsSwitchedOff_AreRefused()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "guest_tickets_enabled", Value = "0" });
            await db.SaveChangesAsync();
        });

        Assert.False((await JsonAsync(await host.Client.GetAsync("/support/widget/config"))).GetProperty("guest_tickets_enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/support/widget/verification", new { email = Email, purpose = "ticket" })).StatusCode);
        var (id, code) = await GuestProof.IssueAsync(host, Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }
}
