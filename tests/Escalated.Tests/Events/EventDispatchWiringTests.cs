using System.Net;
using Escalated.Data;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParameterlessRegistration = Escalated.Extensions.ServiceCollectionExtensions;

namespace Escalated.Tests.Events;

/// <summary>
/// What a domain event reaches once <c>AddEscalated()</c> has registered
/// everything, driven through the real mutation sites.
///
/// <para>Two things were wrong. Nothing called <see cref="WebhookDispatcher"/>,
/// so a webhook an admin configured never received a delivery. And the Workflow
/// bridge was registered with <c>TryAddSingleton</c> as the one
/// <see cref="IEscalatedEventDispatcher"/>, so a host that registered its own
/// dispatcher to listen for events, as the README tells it to, silently switched
/// Workflows off.</para>
/// </summary>
public class EventDispatchWiringTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
            {
                Requests.Add((request, body));
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private sealed class HostDispatcher : IEscalatedEventDispatcher
    {
        public List<object> Received { get; } = new();

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
        {
            lock (Received)
            {
                Received.Add(@event);
            }

            return Task.CompletedTask;
        }
    }

    public static IEnumerable<object[]> RegistrationOrders() => new[]
    {
        new object[] { "before AddEscalated" },
        new object[] { "after AddEscalated" },
    };

    private static ServiceProvider Build(RecordingHandler handler, HostDispatcher? host = null, string order = "after AddEscalated")
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var name = Guid.NewGuid().ToString();
        services.AddDbContext<EscalatedDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        if (host is not null && order == "before AddEscalated")
        {
            services.AddSingleton<IEscalatedEventDispatcher>(host);
        }

        ParameterlessRegistration.AddEscalated(services);

        if (host is not null && order == "after AddEscalated")
        {
            services.AddSingleton<IEscalatedEventDispatcher>(host);
        }

        // Webhook deliveries go out through this client; the handler stands in
        // for the subscriber's server.
        services.AddHttpClient("EscalatedWebhook").ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static async Task SeedWebhookAsync(IServiceProvider sp, string events)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        db.Webhooks.Add(new Webhook
        {
            Url = "https://hooks.example.com/escalated",
            Events = events,
            Secret = "s3cret",
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedWorkflowAsync(IServiceProvider sp, string trigger, string actions)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        db.Workflows.Add(new Workflow
        {
            Name = "Triage",
            TriggerEvent = trigger,
            Conditions = "{}",
            Actions = actions,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Ticket> CreateTicketAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
        return await tickets.CreateAsync("Printer on fire", "It is actually on fire", requesterId: "7");
    }

    private static List<WebhookDelivery> Deliveries(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EscalatedDbContext>().WebhookDeliveries.ToList();
    }

    [Fact]
    public async Task TicketCreated_IsDeliveredToASubscribedWebhook()
    {
        var handler = new RecordingHandler();
        await using var sp = Build(handler);
        await SeedWebhookAsync(sp, """["ticket.created"]""");

        var ticket = await CreateTicketAsync(sp);

        var delivery = Assert.Single(Deliveries(sp));
        Assert.Equal("ticket.created", delivery.Event);
        Assert.Equal(200, delivery.ResponseCode);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://hooks.example.com/escalated", request.RequestUri!.ToString());
        Assert.Equal("ticket.created", request.Headers.GetValues("X-Escalated-Event").Single());
        Assert.True(request.Headers.Contains("X-Escalated-Signature"));
        Assert.Contains(ticket.Reference, body);
    }

    [Fact]
    public async Task EventTheWebhookDidNotSubscribeTo_IsNotDelivered()
    {
        var handler = new RecordingHandler();
        await using var sp = Build(handler);
        await SeedWebhookAsync(sp, """["ticket.closed"]""");

        await CreateTicketAsync(sp);

        Assert.Empty(Deliveries(sp));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [MemberData(nameof(RegistrationOrders))]
    public async Task HostDispatcher_DoesNotSwitchOffWorkflows(string order)
    {
        var host = new HostDispatcher();
        await using var sp = Build(new RecordingHandler(), host, order);
        await SeedWorkflowAsync(sp, "ticket.created", """[{"type":"add_note","value":"auto-triage"}]""");

        await CreateTicketAsync(sp);

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var log = Assert.Single(db.WorkflowLogs.ToList());
        Assert.Equal("ticket.created", log.TriggerEvent);
        Assert.Contains(db.Replies.ToList(), r => r.IsInternalNote && r.Body == "auto-triage");

        // And the host still hears about it.
        Assert.Contains(host.Received, e => e is TicketCreatedEvent);
    }

    [Theory]
    [MemberData(nameof(RegistrationOrders))]
    public async Task HostDispatcher_DoesNotSwitchOffWebhooks(string order)
    {
        var handler = new RecordingHandler();
        var host = new HostDispatcher();
        await using var sp = Build(handler, host, order);
        await SeedWebhookAsync(sp, """["ticket.created"]""");

        await CreateTicketAsync(sp);

        Assert.Single(Deliveries(sp));
        Assert.Contains(host.Received, e => e is TicketCreatedEvent);
    }
}
