using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Notifications;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The mobile guest API (<c>support/api/v1/mobile/guest/...</c>), against the
/// canonical contract (escalated-developer-context
/// <c>domain-model/mobile-guest-api.md</c>) and escalated-laravel's
/// <c>MobileGuestTicketController</c>. Everything goes through the real HTTP
/// pipeline; codes are read from the email.
/// </summary>
public class MobileGuestApiTests
{
    private const string Prefix = "/support/api/v1/mobile/guest";
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

    private sealed class MemoryStorage : IAttachmentStorage
    {
        public string Name => "local";

        public async Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        {
            var path = Path.Combine(Path.GetTempPath(), $"escalated-mobile-{Guid.NewGuid():N}-{filename}");
            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct);
            return path;
        }
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
            services =>
            {
                services.AddSingleton<IEscalatedMailTransport>(mail);
                services.AddSingleton<IAttachmentStorage, MemoryStorage>();
            });
        return (host, mail);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static void AssertPrivate(HttpResponseMessage response)
    {
        var cache = string.Join(",", response.Headers.GetValues("Cache-Control"));
        Assert.Contains("private", cache);
        Assert.Contains("no-store", cache);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    private static async Task<(string Id, string Code)> RequestCodeAsync(EscalatedTestHost host, Mailbox mail, string purpose,
        string email = Email, string prefix = Prefix)
    {
        var response = await host.Client.PostAsJsonAsync($"{prefix}/verification", new { email, purpose });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AssertPrivate(response);
        var json = await JsonAsync(response);
        Assert.Equal(600, json.GetProperty("expires_in").GetInt32());
        return (json.GetProperty("verification_id").GetString()!, mail.LastCodeFor(email));
    }

    private static Dictionary<string, object?> TicketBody(string id, string code, string email = Email) => new()
    {
        ["name"] = "Gail",
        ["email"] = email,
        ["subject"] = "Where is my order?",
        ["description"] = "It is late",
        ["verification_id"] = id,
        ["verification_code"] = code,
    };

    private static async Task<(JsonElement Data, string Token)> CreateAsync(EscalatedTestHost host, Mailbox mail, string email = Email)
    {
        var (id, code) = await RequestCodeAsync(host, mail, "ticket", email);
        var response = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(id, code, email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await JsonAsync(response)).GetProperty("data");
        return (data, data.GetProperty("guest_access_token").GetString()!);
    }

    private static Task<HttpResponseMessage> ReplyAsync(EscalatedTestHost host, string token, string email, string body = "Thanks") =>
        host.Client.PostAsJsonAsync($"{Prefix}/tickets/{token}/replies", new { email, body });

    [Fact]
    public async Task Create_WithoutProof_Is422_AndCreatesNothing()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;

        var response = await host.Client.PostAsJsonAsync($"{Prefix}/tickets",
            new { name = "Gail", email = Email, subject = "Hi", description = "x" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertPrivate(response);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("verification_id", out _));
        Assert.True(errors.TryGetProperty("verification_code", out _));

        // A well-formed but wrong proof: still nothing is created.
        var wrong = TicketBody(Guid.NewGuid().ToString(), "12345678");
        var refused = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", wrong);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.True((await JsonAsync(refused)).GetProperty("errors").TryGetProperty("verification_code", out _));

        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.Contacts.CountAsync()));
    }

    [Fact]
    public async Task Create_RefusesBadFields_With422_AndLeavesTheCodeUnspent()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        var body = TicketBody(id, code);
        body["priority"] = "whenever";
        body["department_id"] = 999;
        body["subject"] = new string('s', 256);

        var response = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("priority", out _));
        Assert.True(errors.TryGetProperty("department_id", out _));
        Assert.True(errors.TryGetProperty("subject", out _));

        Assert.Equal(HttpStatusCode.Created, (await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(id, code))).StatusCode);
    }

    [Fact]
    public async Task Verification_ThenCreate_Is201WithAGrant_AndTheCodeIsSpent()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var departmentId = 0;
        await host.SeedAsync(async db =>
        {
            var department = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            departmentId = department.Id;
        });

        var (id, code) = await RequestCodeAsync(host, mail, "ticket", "Gail@Example.com");
        var body = TicketBody(id, code, " Gail@Example.com ");
        body["priority"] = "high";
        body["department_id"] = departmentId;
        body["status"] = "closed";
        body["metadata"] = new { secret = "x" };
        body["external_reference"] = "EXT-1";

        var response = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertPrivate(response);
        var json = await JsonAsync(response);
        Assert.Equal("Ticket created.", json.GetProperty("message").GetString());
        var data = json.GetProperty("data");
        var keys = data.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "activities", "assignee", "channel", "closed_at", "created_at", "department", "description", "followers_count",
            "guest_access_expires_at", "guest_access_token", "id", "is_following", "metadata", "priority", "reference",
            "replies", "requester", "resolved_at", "sla", "status", "subject", "tags", "updated_at",
        }, keys);

        var token = data.GetProperty("guest_access_token").GetString()!;
        Assert.True(token.Length >= 40);
        Assert.InRange(data.GetProperty("guest_access_expires_at").GetDateTime().ToUniversalTime(),
            DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
        Assert.Equal("open", data.GetProperty("status").GetProperty("value").GetString());
        Assert.Equal("Open", data.GetProperty("status").GetProperty("label").GetString());
        Assert.Equal("high", data.GetProperty("priority").GetProperty("value").GetString());
        Assert.Equal("High", data.GetProperty("priority").GetProperty("label").GetString());
        Assert.Equal("web", data.GetProperty("channel").GetString());
        Assert.Equal(JsonValueKind.Object, data.GetProperty("metadata").ValueKind);
        Assert.Empty(data.GetProperty("metadata").EnumerateObject());
        Assert.Equal("Gail", data.GetProperty("requester").GetProperty("name").GetString());
        Assert.Equal("gail@example.com", data.GetProperty("requester").GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("assignee").ValueKind);
        Assert.Equal(departmentId, data.GetProperty("department").GetProperty("id").GetInt32());
        Assert.Equal("Billing", data.GetProperty("department").GetProperty("name").GetString());
        Assert.Empty(data.GetProperty("replies").EnumerateArray());
        Assert.Empty(data.GetProperty("tags").EnumerateArray());
        Assert.Empty(data.GetProperty("activities").EnumerateArray());
        Assert.False(data.GetProperty("sla").GetProperty("first_response_breached").GetBoolean());
        Assert.False(data.GetProperty("is_following").GetBoolean());
        Assert.Equal(0, data.GetProperty("followers_count").GetInt32());

        var ticket = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync());
        Assert.Equal(data.GetProperty("reference").GetString(), ticket.Reference);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.Metadata);
        Assert.Equal("gail@example.com", ticket.GuestEmail);
        Assert.Equal(GuestAccessService.HashGrant(token), ticket.GuestAccessHash);
        Assert.NotNull(ticket.ContactId);

        var again = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(id, code));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.True((await JsonAsync(again)).GetProperty("errors").TryGetProperty("verification_code", out _));
        Assert.Equal(1, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Create_AcceptsMultipart_WithAttachments_AndRefusesAnOtherPurposeCode()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var (chatId, chatCode) = await RequestCodeAsync(host, mail, "chat");
        var refused = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(chatId, chatCode));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        var form = new MultipartFormDataContent();
        foreach (var (key, value) in TicketBody(id, code)) form.Add(new StringContent((string)value!), key);
        var file = new ByteArrayContent("receipt"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "attachments[]", "receipt.txt");

        var response = await host.Client.PostAsync($"{Prefix}/tickets", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticketId = (await JsonAsync(response)).GetProperty("data").GetProperty("id").GetInt32();
        var attachment = await host.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync());
        Assert.Equal(ticketId, attachment.AttachableId);
        Assert.Equal("receipt.txt", attachment.Filename);
    }

    [Fact]
    public async Task Create_IsRefused_WhileGuestTicketsAreOff()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "guest_tickets_enabled", Value = "0" });
            await db.SaveChangesAsync();
        });

        var response = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(id, code));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Guest tickets are not enabled.", (await JsonAsync(response)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await host.Client.PostAsJsonAsync($"{Prefix}/verification", new { email = Email, purpose = "ticket" })).StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task TheGrant_OpensMobileShow_TheWidget_AndTheGuestPage()
    {
        var (host, mail) = await StartAsync(o => o.Ui.Enabled = true);
        await using var _h = host;
        var (created, token) = await CreateAsync(host, mail);
        var reference = created.GetProperty("reference").GetString()!;

        var show = await host.Client.GetAsync($"{Prefix}/tickets/{token}");
        Assert.Equal(HttpStatusCode.OK, show.StatusCode);
        AssertPrivate(show);
        var data = (await JsonAsync(show)).GetProperty("data");
        Assert.Equal(reference, data.GetProperty("reference").GetString());
        Assert.Equal(token, data.GetProperty("guest_access_token").GetString());

        var widget = new HttpRequestMessage(HttpMethod.Get, $"/support/widget/tickets/{reference}");
        widget.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(widget)).StatusCode);

        var page = await PageAsync(host.ClientFor(string.Empty), $"/support/guest/{token}");
        Assert.Equal(reference, page.GetProperty("props").GetProperty("ticket").GetProperty("reference").GetString());
    }

    [Fact]
    public async Task GrantsFromTheWidgetAndFromLookup_OpenTheMobileRoutes_AndLookupRotatesThem()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket", prefix: "/support/widget");
        var widgetCreate = await JsonAsync(await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code)));
        var widgetToken = widgetCreate.GetProperty("guest_access_token").GetString()!;
        var reference = widgetCreate.GetProperty("reference").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Prefix}/tickets/{widgetToken}")).StatusCode);

        var (lookupId, lookupCode) = await RequestCodeAsync(host, mail, "lookup");
        var lookup = await host.Client.PostAsJsonAsync($"{Prefix}/lookup",
            new { email = Email, reference, verification_id = lookupId, verification_code = lookupCode });
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        AssertPrivate(lookup);
        var entry = (await JsonAsync(lookup)).GetProperty("data").EnumerateArray().Single();
        Assert.Equal(reference, entry.GetProperty("reference").GetString());
        var fresh = entry.GetProperty("guest_access_token").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Prefix}/tickets/{fresh}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"{Prefix}/tickets/{widgetToken}")).StatusCode);

        // The code is spent, and a bad code is a 422 on verification_code.
        var reused = await host.Client.PostAsJsonAsync($"{Prefix}/lookup",
            new { email = Email, reference, verification_id = lookupId, verification_code = lookupCode });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.True((await JsonAsync(reused)).GetProperty("errors").TryGetProperty("verification_code", out _));
    }

    [Fact]
    public async Task Reply_ChecksTheEmail_AndTheTicketState()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (created, token) = await CreateAsync(host, mail);
        var ticketId = created.GetProperty("id").GetInt32();

        var ok = await ReplyAsync(host, token, " GAIL@example.COM ");
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        AssertPrivate(ok);
        Assert.Equal("Reply sent.", (await JsonAsync(ok)).GetProperty("message").GetString());
        var reply = await host.QueryAsync(db => db.Replies.AsNoTracking().SingleAsync(r => r.TicketId == ticketId));
        Assert.Equal("Thanks", reply.Body);
        Assert.False(reply.IsInternalNote);
        Assert.Null(reply.AuthorId);

        var mismatch = await ReplyAsync(host, token, "mallory@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        Assert.Equal("The provided email does not match this ticket.", (await JsonAsync(mismatch)).GetProperty("message").GetString());

        var invalid = await host.Client.PostAsJsonAsync($"{Prefix}/tickets/{token}/replies", new { email = "not-an-email", body = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var errors = (await JsonAsync(invalid)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("body", out _));
        Assert.True(errors.TryGetProperty("email", out _));

        await host.SeedAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            ticket.Status = TicketStatus.Closed;
            await db.SaveChangesAsync();
        });
        var closed = await ReplyAsync(host, token, Email);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, closed.StatusCode);
        Assert.Equal("This ticket is closed.", (await JsonAsync(closed)).GetProperty("message").GetString());
        Assert.Equal(1, await host.QueryAsync(db => db.Replies.CountAsync(r => r.TicketId == ticketId)));
    }

    [Fact]
    public async Task Show_ListsPublicRepliesNewestFirst_WithTheirFiles_AndNeverInternalNotes()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (created, token) = await CreateAsync(host, mail);
        var ticketId = created.GetProperty("id").GetInt32();

        var form = new MultipartFormDataContent { { new StringContent("Here is the receipt."), "body" }, { new StringContent(Email), "email" } };
        var file = new ByteArrayContent("receipt"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "attachments[]", "receipt.txt");
        Assert.Equal(HttpStatusCode.Created, (await host.Client.PostAsync($"{Prefix}/tickets/{token}/replies", form)).StatusCode);

        await host.SeedAsync(async db =>
        {
            db.Replies.Add(new Reply
            {
                TicketId = ticketId,
                Body = "Internal: refund approved",
                IsInternalNote = true,
                Type = "note",
                AuthorId = "agent-1",
                CreatedAt = DateTime.UtcNow.AddMinutes(2),
            });
            db.Replies.Add(new Reply
            {
                TicketId = ticketId,
                Body = "We are on it",
                Type = "reply",
                AuthorId = "agent-1",
                CreatedAt = DateTime.UtcNow.AddMinutes(1),
            });
            await db.SaveChangesAsync();
        });

        var data = (await JsonAsync(await host.Client.GetAsync($"{Prefix}/tickets/{token}"))).GetProperty("data");
        var replies = data.GetProperty("replies").EnumerateArray().ToList();

        Assert.Equal(new[] { "We are on it", "Here is the receipt." }, replies.Select(r => r.GetProperty("body").GetString()));
        Assert.DoesNotContain("Internal", data.GetRawText());
        Assert.DoesNotContain("agent-1", data.GetRawText());

        var staff = replies[0];
        Assert.Equal(new[] { "attachments", "author", "body", "created_at", "id", "is_internal_note", "is_pinned" },
            staff.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(staff.GetProperty("is_internal_note").GetBoolean());
        Assert.Equal(0, staff.GetProperty("author").GetProperty("id").GetInt32());
        Assert.Equal("Support", staff.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("", staff.GetProperty("author").GetProperty("email").GetString());

        var own = replies[1];
        Assert.Equal(0, own.GetProperty("author").GetProperty("id").GetInt32());
        Assert.Equal("Gail", own.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal(Email, own.GetProperty("author").GetProperty("email").GetString());
        var attachment = own.GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("receipt.txt", attachment.GetProperty("filename").GetString());
        Assert.Equal("text/plain", attachment.GetProperty("mime_type").GetString());
        Assert.Equal(7, attachment.GetProperty("size").GetInt64());

        var url = new Uri(attachment.GetProperty("url").GetString()!);
        Assert.StartsWith($"{Prefix}/tickets/{token}/attachments/", url.AbsolutePath);
        var download = await host.Client.GetAsync(url.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("receipt", await download.Content.ReadAsStringAsync());

        // Another grant cannot fetch this ticket's file.
        var (_, otherToken) = await CreateAsync(host, mail, "other@example.com");
        Assert.Equal(HttpStatusCode.NotFound,
            (await host.Client.GetAsync(url.AbsolutePath.Replace(token, otherToken))).StatusCode);
    }

    [Fact]
    public async Task ExpiredMalformedOverLongAndChatGrants_Are404()
    {
        var (host, mail) = await StartAsync();
        await using var _h = host;
        var (created, token) = await CreateAsync(host, mail);
        var ticketId = created.GetProperty("id").GetInt32();

        var (chatId, chatCode) = await RequestCodeAsync(host, mail, "chat", prefix: "/support/widget");
        var chat = await host.Client.PostAsJsonAsync("/support/widget/chat/start",
            new { name = "Gail", email = Email, message = "Hello", verification_id = chatId, verification_code = chatCode });
        Assert.Equal(HttpStatusCode.Created, chat.StatusCode);
        var chatToken = (await JsonAsync(chat)).GetProperty("id").GetString()!;

        foreach (var bad in new[] { chatToken, "not-a-grant", "a%2Fb", new string('a', 5000) })
        {
            var show = await host.Client.GetAsync($"{Prefix}/tickets/{bad}");
            Assert.Equal(HttpStatusCode.NotFound, show.StatusCode);
            AssertPrivate(show);
            Assert.Equal(HttpStatusCode.NotFound, (await ReplyAsync(host, bad, Email)).StatusCode);
        }

        await host.SeedAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            ticket.GuestAccessExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"{Prefix}/tickets/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReplyAsync(host, token, Email)).StatusCode);
    }

    [Fact]
    public async Task Submissions_AreThrottled_WithRetryAfter_SharedWithTheWidget()
    {
        var (host, mail) = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 1);
        await using var _h = host;

        var (id, code) = await RequestCodeAsync(host, mail, "ticket");
        Assert.Equal(HttpStatusCode.Created,
            (await host.Client.PostAsJsonAsync("/support/widget/tickets", TicketBody(id, code))).StatusCode);

        var (id2, code2) = await RequestCodeAsync(host, mail, "ticket");
        var limited = await host.Client.PostAsJsonAsync($"{Prefix}/tickets", TicketBody(id2, code2));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        AssertPrivate(limited);
    }

    [Fact]
    public async Task Replies_AndReads_AreThrottled_WithRetryAfter()
    {
        var (host, mail) = await StartAsync(o =>
        {
            o.GuestRateLimit.RepliesPerMinute = 1;
            o.GuestRateLimit.RequestsPerMinute = 6;
        });
        await using var _h = host;
        // verification + create = 2 requests
        var (_, token) = await CreateAsync(host, mail);

        Assert.Equal(HttpStatusCode.Created, (await ReplyAsync(host, token, Email)).StatusCode);
        var limitedReply = await ReplyAsync(host, token, Email);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedReply.StatusCode);
        Assert.True(limitedReply.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // 4 requests so far; the per-IP request budget covers reads too.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Prefix}/tickets/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Prefix}/tickets/{token}")).StatusCode);
        var limitedRead = await host.Client.GetAsync($"{Prefix}/tickets/{token}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedRead.StatusCode);
        Assert.True(limitedRead.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        AssertPrivate(limitedRead);
    }
}
