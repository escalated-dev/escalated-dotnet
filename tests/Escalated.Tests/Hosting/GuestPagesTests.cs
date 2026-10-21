using System.Net;
using System.Net.Http.Headers;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The guest pages (<c>Guest/Create</c>, <c>Guest/Show</c>) on the verified guest
/// flow, against escalated-laravel's <c>Guest/TicketController</c> and
/// <c>CustomerTicketPayload::guestWeb</c>.
/// </summary>
public class GuestPagesTests
{
    private const string Agent = "agent-1";
    private const string Email = "gina@example.com";

    private sealed class MemoryStorage : IAttachmentStorage
    {
        public string Name => "local";

        public async Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        {
            var path = Path.Combine(Path.GetTempPath(), $"escalated-guest-{Guid.NewGuid():N}-{filename}");
            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct);
            return path;
        }
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Guest, int DepartmentId)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IAttachmentStorage, MemoryStorage>());
        var departmentId = 0;
        await host.SeedAsync(async db =>
        {
            var department = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.AddRange(department, new Department { Name = "Archive", Slug = "archive", IsActive = false });
            await db.SaveChangesAsync();
            departmentId = department.Id;
        });
        return (host, host.ClientFor(string.Empty), departmentId);
    }

    private static async Task<HttpResponseMessage> CreateAsync(EscalatedTestHost host, HttpClient guest, object? extra = null, bool prove = true)
    {
        var body = new Dictionary<string, object?>
        {
            ["guest_name"] = "Gina Guest",
            ["guest_email"] = Email,
            ["subject"] = "Card declined",
            ["description"] = "It was declined twice.",
            ["priority"] = "high",
        };
        if (prove)
        {
            var (id, code) = await GuestProof.IssueAsync(host, Email);
            body["verification_id"] = id;
            body["verification_code"] = code;
        }
        foreach (var (key, value) in extra as Dictionary<string, object?> ?? new())
        {
            body[key] = value;
        }

        return await guest.SendAsync(Visit(HttpMethod.Post, "/support/guest", body));
    }

    /// <summary>Creates a ticket through the page and returns the token its redirect names.</summary>
    private static async Task<string> CreatedTokenAsync(EscalatedTestHost host, HttpClient guest)
    {
        var response = await CreateAsync(host, guest);
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("/support/guest/", location);
        return location["/support/guest/".Length..];
    }

    [Fact]
    public async Task Create_RendersTheFormWithTheVerificationEndpoints()
    {
        var (host, guest, departmentId) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(guest, "/support/guest/create");
        AssertPage(page, "Escalated/Guest/Create");
        var props = page.GetProperty("props");
        Assert.Equal(departmentId, Assert.Single(props.GetProperty("departments").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(new[] { "low", "medium", "high", "urgent", "critical" }, props.GetProperty("priorities").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("/support/guest/verification", props.GetProperty("verification_url").GetString());
        Assert.Equal("/support/guest/lookup", props.GetProperty("lookup_url").GetString());
    }

    [Fact]
    public async Task Store_OpensTheTicketAndRedirectsToItsPrivateLink()
    {
        var (host, guest, departmentId) = await StartAsync();
        await using var __ = host;

        var response = await CreateAsync(host, guest, new Dictionary<string, object?> { ["department_id"] = departmentId });
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var token = location["/support/guest/".Length..];

        var ticket = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync());
        Assert.Equal("Gina Guest", ticket.GuestName);
        Assert.Equal(Email, ticket.GuestEmail);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(departmentId, ticket.DepartmentId);
        Assert.Equal(Escalated.Services.GuestAccessService.HashGrant(token), ticket.GuestAccessHash);

        var page = await PageAsync(guest, location, FlashCookie(response));
        Assert.Equal("Ticket created.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
    }

    [Fact]
    public async Task Store_NeedsAValidProofAndFields()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;

        var unproved = await CreateAsync(host, guest, new Dictionary<string, object?> { ["verification_id"] = Guid.NewGuid().ToString(), ["verification_code"] = "00000000" }, prove: false);
        Assert.Equal(HttpStatusCode.SeeOther, unproved.StatusCode);
        Assert.Equal("/support/guest/create", unproved.Headers.Location!.ToString());
        var errors = (await PageAsync(guest, "/support/guest/create", FlashCookie(unproved))).GetProperty("props").GetProperty("errors");
        Keys(errors, "verification_code");

        var empty = await guest.SendAsync(Visit(HttpMethod.Post, "/support/guest", new { guest_email = "nope", priority = "extreme", department_id = 999 }));
        var fields = (await PageAsync(guest, "/support/guest/create", FlashCookie(empty))).GetProperty("props").GetProperty("errors");
        Keys(fields, "guest_name", "guest_email", "subject", "description", "verification_id", "verification_code", "priority", "department_id");

        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Show_RendersTheGuestPayloadForAValidToken()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        var token = await CreatedTokenAsync(host, guest);
        await host.SeedAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync();
            db.Replies.AddRange(
                new Reply { TicketId = ticket.Id, Body = "We are on it.", AuthorId = Agent, CreatedAt = DateTime.UtcNow.AddMinutes(1) },
                new Reply { TicketId = ticket.Id, Body = "Secret note", AuthorId = Agent, IsInternalNote = true, CreatedAt = DateTime.UtcNow.AddMinutes(2) });
            await db.SaveChangesAsync();
        });

        var page = await PageAsync(guest, $"/support/guest/{token}");
        AssertPage(page, "Escalated/Guest/Show");
        var props = page.GetProperty("props");
        Assert.Equal(token, props.GetProperty("token").GetString());
        var ticket = Keys(props.GetProperty("ticket"), "reference", "subject", "description", "status", "priority", "channel",
            "department", "attachments", "replies", "satisfaction_rating", "guest_access_expires_at", "resolved_at", "closed_at",
            "created_at", "updated_at");
        Assert.False(ticket.TryGetProperty("id", out _));
        Assert.False(ticket.TryGetProperty("requester", out _));
        var reply = Assert.Single(ticket.GetProperty("replies").EnumerateArray());
        Keys(reply, "id", "body", "is_internal_note", "is_pinned", "author", "attachments", "created_at");
        Assert.Equal("We are on it.", reply.GetProperty("body").GetString());
        Assert.Equal("Support", reply.GetProperty("author").GetProperty("name").GetString());
        Assert.False(reply.GetProperty("author").TryGetProperty("email", out _));

        var bad = await guest.SendAsync(Visit(HttpMethod.Get, "/support/guest/not-a-real-token"));
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }

    [Fact]
    public async Task Show_IsA404OnceTheGrantExpires()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        var token = await CreatedTokenAsync(host, guest);
        await host.SeedAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync();
            ticket.GuestAccessExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        var response = await guest.SendAsync(Visit(HttpMethod.Get, $"/support/guest/{token}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reply_AddsAGuestReplyWithFilesThatTheGuestCanDownload()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        var token = await CreatedTokenAsync(host, guest);

        var form = new MultipartFormDataContent { { new StringContent("Here is the receipt."), "body" } };
        var file = new ByteArrayContent("receipt"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "attachments[0]", "receipt.txt");
        var request = Visit(HttpMethod.Post, $"/support/guest/{token}/reply");
        request.Content = form;
        var response = await guest.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal($"/support/guest/{token}", response.Headers.Location!.ToString());
        var reply = await host.QueryAsync(db => db.Replies.AsNoTracking().SingleAsync());
        Assert.Equal("Here is the receipt.", reply.Body);
        Assert.Null(reply.AuthorId);
        Assert.False(reply.IsInternalNote);

        var page = await PageAsync(guest, $"/support/guest/{token}");
        var shown = page.GetProperty("props").GetProperty("ticket").GetProperty("replies")[0];
        Assert.Equal("Gina Guest", shown.GetProperty("author").GetProperty("name").GetString());
        var url = shown.GetProperty("attachments")[0].GetProperty("url").GetString()!;
        Assert.Contains($"/support/guest/{token}/attachments/", url);

        var download = await guest.GetAsync(new Uri(url).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("receipt", await download.Content.ReadAsStringAsync());

        var otherToken = await CreatedTokenAsync(host, guest);
        var attachmentId = await host.QueryAsync(db => db.Attachments.Select(a => a.Id).SingleAsync());
        var foreign = await guest.GetAsync($"/support/guest/{otherToken}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Reply_IsRefusedOnAClosedTicketAndNeedsABody()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        var token = await CreatedTokenAsync(host, guest);

        var empty = await guest.SendAsync(Visit(HttpMethod.Post, $"/support/guest/{token}/reply", new { body = "" }));
        Keys((await PageAsync(guest, $"/support/guest/{token}", FlashCookie(empty))).GetProperty("props").GetProperty("errors"), "body");

        await host.SeedAsync(async db =>
        {
            (await db.Tickets.SingleAsync()).Status = TicketStatus.Closed;
            await db.SaveChangesAsync();
        });
        var closed = await guest.SendAsync(Visit(HttpMethod.Post, $"/support/guest/{token}/reply", new { body = "Hello?" }));
        Assert.Equal(HttpStatusCode.SeeOther, closed.StatusCode);
        var flash = (await PageAsync(guest, $"/support/guest/{token}", FlashCookie(closed))).GetProperty("props").GetProperty("flash");
        Assert.Equal("This ticket is closed.", flash.GetProperty("error").GetString());
        Assert.Equal(0, await host.QueryAsync(db => db.Replies.CountAsync()));
    }

    [Fact]
    public async Task Rate_RecordsOneRatingOnAResolvedTicket()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        var token = await CreatedTokenAsync(host, guest);

        var open = await guest.SendAsync(Visit(HttpMethod.Post, $"/support/guest/{token}/rate", new { rating = 5 }));
        Assert.Equal(HttpStatusCode.SeeOther, open.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.SatisfactionRatings.CountAsync()));

        await host.SeedAsync(async db =>
        {
            (await db.Tickets.SingleAsync()).Status = TicketStatus.Resolved;
            await db.SaveChangesAsync();
        });
        var rated = await guest.SendAsync(Visit(HttpMethod.Post, $"/support/guest/{token}/rate", new { rating = 4, comment = "Quick" }));
        Assert.Equal(HttpStatusCode.SeeOther, rated.StatusCode);
        var again = await guest.SendAsync(Visit(HttpMethod.Post, $"/support/guest/{token}/rate", new { rating = 1 }));
        Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);

        var rating = await host.QueryAsync(db => db.SatisfactionRatings.AsNoTracking().SingleAsync());
        Assert.Equal(4, rating.Rating);
        Assert.Equal("Quick", rating.Comment);
        var page = await PageAsync(guest, $"/support/guest/{token}");
        Assert.Equal(4, page.GetProperty("props").GetProperty("ticket").GetProperty("satisfaction_rating").GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task ThePages_Are404WhileGuestTicketsAreOff()
    {
        var (host, guest, _) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "guest_tickets_enabled", Value = "0" });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NotFound, (await guest.SendAsync(Visit(HttpMethod.Get, "/support/guest/create"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateAsync(host, guest)).StatusCode);
    }
}
