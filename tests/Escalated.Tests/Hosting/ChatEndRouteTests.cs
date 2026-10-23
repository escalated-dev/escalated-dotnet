using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The agent ticket page's End Chat button: the page resolves
/// <c>route('escalated.admin.chat.end', ticket.chat_session_id)</c> from the
/// routes the root view embeds, and posts <c>{}</c> there as JSON from the agent's
/// signed-in browser session. escalated-laravel answers
/// <c>POST {prefix}/admin/chat/{session}/end</c>.
/// </summary>
public partial class ChatEndRouteTests
{
    private const string Agent = "agent-1";

    private static async Task<(EscalatedTestHost Host, int SessionId, int TicketId)> StartAsync(string? prefix = null)
    {
        var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            if (prefix is not null) options.RoutePrefix = prefix;
        });
        await host.GrantRoleAsync(Agent, "escalated-agent");

        int sessionId = 0, ticketId = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket
            {
                Reference = "ESC-00001",
                Subject = "Chat with Vic",
                Description = "Hello",
                AssignedTo = Agent,
                Status = TicketStatus.Open,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();

            var session = new ChatSession { TicketId = ticket.Id, VisitorName = "Vic", AgentId = Agent, Status = "active", AcceptedAt = DateTime.UtcNow };
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
            ticketId = ticket.Id;
        });

        return (host, sessionId, ticketId);
    }

    [GeneratedRegex(@"var routes=(\{.*?\});if\(")]
    private static partial Regex RoutesScript();

    /// <summary>The routes the root view hands <c>window.route</c>, read off a full page load.</summary>
    private static async Task<Dictionary<string, string>> PageRoutesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/support/agent/tickets/ESC-00001");
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {html}");
        var match = RoutesScript().Match(html);
        Assert.True(match.Success, "The page embeds no route map.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(match.Groups[1].Value)!;
    }

    /// <summary>The request <c>ChatActionBar</c> sends.</summary>
    private static HttpRequestMessage EndChat(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("X-CSRF-TOKEN", "page-token");
        return request;
    }

    private static string Fill(string template, int session) => template.Replace("{session}", session.ToString());

    [Fact]
    public async Task TheNamedRoute_EndsTheChatFromTheAgentsPage()
    {
        var (host, sessionId, ticketId) = await StartAsync();
        await using var hostScope = host;
        var client = host.ClientFor(Agent);

        var routes = await PageRoutesAsync(client);
        Assert.Equal("/support/admin/chat/{session}/end", routes["escalated.admin.chat.end"]);

        var response = await client.SendAsync(EndChat(Fill(routes["escalated.admin.chat.end"], sessionId)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Chat ended.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());

        var session = await host.QueryAsync(db => db.ChatSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));
        Assert.Equal("ended", session.Status);
        Assert.NotNull(session.EndedAt);
        var ticket = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId));
        Assert.Equal(TicketStatus.Resolved, ticket.Status);

        var again = await client.SendAsync(EndChat(Fill(routes["escalated.admin.chat.end"], sessionId)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal("Chat is already ended.", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task TheNamedRoute_FollowsTheConfiguredRoutePrefix()
    {
        var (host, sessionId, _) = await StartAsync(prefix: "helpdesk");
        await using var hostScope = host;
        var client = host.ClientFor(Agent);

        var routes = await PageRoutesAsync(client);
        Assert.Equal("/helpdesk/admin/chat/{session}/end", routes["escalated.admin.chat.end"]);

        var response = await client.SendAsync(EndChat(Fill(routes["escalated.admin.chat.end"], sessionId)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ended", await host.QueryAsync(db => db.ChatSessions.Where(s => s.Id == sessionId).Select(s => s.Status).SingleAsync()));
    }

    [Fact]
    public async Task TheNamedRoute_IsForStaffOnly_AndAnswers404ForAnUnknownSession()
    {
        var (host, sessionId, _) = await StartAsync();
        await using var hostScope = host;
        var url = $"/support/admin/chat/{sessionId}/end";

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(EndChat(url))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor("customer-1").SendAsync(EndChat(url))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.ClientFor(Agent).SendAsync(EndChat($"/support/admin/chat/{sessionId + 999}/end"))).StatusCode);
        Assert.Equal("active", await host.QueryAsync(db => db.ChatSessions.Where(s => s.Id == sessionId).Select(s => s.Status).SingleAsync()));
    }

    [Fact]
    public async Task TheAgentChatApi_StillEndsChats_WithItsOwnResponses()
    {
        var (host, sessionId, _) = await StartAsync();
        await using var hostScope = host;
        var client = host.ClientFor(Agent);

        var response = await client.SendAsync(EndChat($"/support/agent/chat/{sessionId}/end"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ended", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var again = await client.SendAsync(EndChat($"/support/agent/chat/{sessionId}/end"));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }
}
