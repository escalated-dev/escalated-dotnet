using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The ticket and reply endpoints return EF entities, and MVC serializes them
/// without a reference handler. A reply's <c>Ticket</c> and the ticket's
/// <c>Replies</c> point at each other, as do an activity and the ticket's
/// <c>Activities</c>, so any response carrying one of those graphs threw
/// <c>JsonException: A possible object cycle was detected</c> after the change
/// had already been saved.
///
/// <para>These requests go through the real pipeline, signed in as a user each
/// endpoint's policy admits, because only MVC's own output formatter shows the
/// failure. The controller unit tests inspect the result object and never
/// serialize it.</para>
/// </summary>
public class TicketResponseSerializationTests
{
    private const string AdminRole = "escalated-admin";
    private const string AgentRole = "escalated-agent";
    private const string Requester = "alice";

    public static IEnumerable<object[]> TicketEndpoints() => new[]
    {
        new object[] { "customer", "GET", "/support/tickets/{id}", null! },
        new object[] { "agent", "GET", "/support/agent/tickets/{id}", null! },
        new object[] { "admin", "GET", "/support/admin/tickets/{id}", null! },
        new object[] { "agent", "POST", "/support/agent/tickets/{id}/priority", new { priority = "high" } },
        new object[] { "admin", "POST", "/support/admin/tickets/{id}/priority", new { priority = "high" } },
    };

    public static IEnumerable<object[]> ReplyEndpoints() => new[]
    {
        new object[] { "customer", "/support/tickets/{id}/reply" },
        new object[] { "agent", "/support/agent/tickets/{id}/reply" },
        new object[] { "agent", "/support/agent/tickets/{id}/note" },
        new object[] { "admin", "/support/admin/tickets/{id}/reply" },
        new object[] { "admin", "/support/admin/tickets/{id}/note" },
    };

    [Theory]
    [MemberData(nameof(ReplyEndpoints))]
    public async Task ReplyEndpoint_ReturnsTheSavedReply(string caller, string path)
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var client = await ClientAsync(host, caller);
        var ticketId = await SeedTicketGraphAsync(host);

        var response = await client.PostAsJsonAsync(path.Replace("{id}", ticketId.ToString()), new { body = "On it." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ticketId, json.RootElement.GetProperty("ticketId").GetInt32());
        Assert.Equal("On it.", json.RootElement.GetProperty("body").GetString());
        Assert.False(json.RootElement.TryGetProperty("ticket", out _), "a reply does not embed its ticket");
    }

    [Theory]
    [MemberData(nameof(TicketEndpoints))]
    public async Task TicketEndpoint_ReturnsTheTicketWithItsReplies(string caller, string method, string path, object? body)
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var client = await ClientAsync(host, caller);
        var ticketId = await SeedTicketGraphAsync(host);
        var url = path.Replace("{id}", ticketId.ToString());

        var response = method == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsJsonAsync(url, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ticket = json.RootElement;
        Assert.Equal(ticketId, ticket.GetProperty("id").GetInt32());

        // The props the shared frontend reads keep their names.
        Assert.True(ticket.TryGetProperty("requester_name", out _));
        Assert.True(ticket.TryGetProperty("last_reply_at", out _));
        Assert.True(ticket.TryGetProperty("is_snoozed", out _));

        if (method == "GET")
        {
            var reply = Assert.Single(ticket.GetProperty("replies").EnumerateArray());
            Assert.Equal("First reply", reply.GetProperty("body").GetString());
            Assert.Equal(JsonValueKind.Array, reply.GetProperty("attachments").ValueKind);
            Assert.False(reply.TryGetProperty("ticket", out _), "a reply inside a ticket does not embed the ticket again");
            Assert.Equal("Billing", ticket.GetProperty("department").GetProperty("name").GetString());
            Assert.False(ticket.GetProperty("department").TryGetProperty("tickets", out _));
            Assert.NotEmpty(ticket.GetProperty("activities").EnumerateArray());
        }
    }

    private static async Task<HttpClient> ClientAsync(EscalatedTestHost host, string caller)
    {
        switch (caller)
        {
            case "admin":
                await host.GrantRoleAsync("admin-1", AdminRole);
                return host.ClientFor("admin-1");
            case "agent":
                await host.GrantRoleAsync("agent-2", AgentRole);
                return host.ClientFor("agent-2");
            default:
                return host.ClientFor(Requester);
        }
    }

    /// <summary>A ticket as the detail pages load it: a department, a tag, a reply, an activity.</summary>
    private static async Task<int> SeedTicketGraphAsync(EscalatedTestHost host)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket
            {
                Subject = "Invoice is wrong",
                Reference = $"ESC-{Guid.NewGuid():N}"[..20],
                RequesterId = Requester,
                Status = TicketStatus.Open,
                Priority = TicketPriority.Medium,
                Department = new Department { Name = "Billing", Slug = "billing" },
            };
            ticket.Tags.Add(new Tag { Name = "Invoices", Slug = "invoices" });
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();

            db.Replies.Add(new Reply { TicketId = ticket.Id, Body = "First reply", AuthorId = Requester, Type = "reply" });
            db.TicketActivities.Add(new TicketActivity { TicketId = ticket.Id, Type = ActivityType.Replied, CauserId = Requester });
            await db.SaveChangesAsync();

            id = ticket.Id;
        });

        return id;
    }
}
