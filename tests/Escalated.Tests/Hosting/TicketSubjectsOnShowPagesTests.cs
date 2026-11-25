using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Contracts;
using Escalated.Models;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The agent and admin ticket pages send <c>ticket.subjects</c> (the sidebar's Related card)
/// exactly as the JSON API's ticket resource serialises them.
/// </summary>
public class TicketSubjectsOnShowPagesTests
{
    private sealed class Order : ITicketSubject
    {
        public string TicketSubjectTitle() => "Order #1042";
        public string? TicketSubjectSubtitle() => "Paid";
        public string? TicketSubjectUrl() => "/orders/1042";
        public string? TicketSubjectColor() => "#16a34a";
        public string? TicketSubjectIcon() => "shopping-cart";
    }

    private sealed class Resolver : ITicketSubjectResolver
    {
        public Task<ITicketSubject?> ResolveAsync(string subjectType, string subjectId, CancellationToken ct = default) =>
            Task.FromResult<ITicketSubject?>(subjectType == "Order" && subjectId == "1042" ? new Order() : null);
    }

    [Fact]
    public async Task AgentAndAdminShowPages_CarryTheSubjectsAsTheApiDoes()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<ITicketSubjectResolver, Resolver>());
        await host.GrantRoleAsync("admin-1", "escalated-admin");
        var client = host.ClientFor("admin-1");

        var ticketId = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket { Reference = "ESC-00001", Subject = "Where is my order?" };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            db.TicketSubjectLinks.AddRange(
                new TicketSubjectLink { TicketId = ticket.Id, SubjectType = "Order", SubjectId = "1042", Role = "order", Position = 0 },
                new TicketSubjectLink { TicketId = ticket.Id, SubjectType = "Customer", SubjectId = "7", Position = 1 });
            await db.SaveChangesAsync();
            ticketId = ticket.Id;
        });

        var api = (await client.GetFromJsonAsync<JsonElement>($"/support/agent/tickets/{ticketId}")).GetProperty("subjects");
        Assert.Equal(2, api.GetArrayLength());
        Assert.Equal("Order #1042", api[0].GetProperty("title").GetString());
        Assert.Equal("/orders/1042", api[0].GetProperty("url").GetString());
        Assert.True(api[1].GetProperty("missing").GetBoolean());

        var adminApi = (await client.GetFromJsonAsync<JsonElement>($"/support/admin/tickets/{ticketId}")).GetProperty("subjects");
        Assert.Equal(api.GetRawText(), adminApi.GetRawText());

        foreach (var url in new[] { "/support/agent/tickets/ESC-00001", "/support/admin/tickets/ESC-00001" })
        {
            var subjects = (await PageAsync(client, url)).GetProperty("props").GetProperty("ticket").GetProperty("subjects");
            Assert.Equal(api.GetRawText(), subjects.GetRawText());
        }
    }
}
