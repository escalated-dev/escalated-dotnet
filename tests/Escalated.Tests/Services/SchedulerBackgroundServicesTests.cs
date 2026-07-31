using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Wiring tests for the time-based schedulers. Each hosted service exposes a
/// <c>RunOnceAsync</c> that performs a single sweep in its own DI scope; these
/// tests drive that directly (no timer loop) to prove the scheduler resolves
/// and invokes its runner. A registration test proves the services are
/// actually added to the container by <c>AddEscalated</c> — the gap that left
/// automations, SLA breaches, and escalation silently inert.
/// </summary>
public class SchedulerBackgroundServicesTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<EscalatedOptions>>(Options.Create(new EscalatedOptions()));
        services.AddDbContext<EscalatedDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        // No-op dispatcher: these tests assert the runners execute, not the
        // downstream Workflow bridge.
        services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();
        services.AddScoped<TicketService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<AutomationRunner>();
        services.AddScoped<SlaService>();
        services.AddScoped<EscalationService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AutomationScheduler_RunsAutomationsAgainstOpenTickets()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        int ticketId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            db.Automations.Add(new Automation
            {
                Name = "Bump open tickets",
                Active = true,
                Conditions = "[{\"field\":\"status\",\"operator\":\"equals\",\"value\":\"open\"}]",
                Actions = "[{\"type\":\"change_priority\",\"value\":\"high\"}]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            var ticket = new Ticket
            {
                Subject = "Idle",
                Reference = "ESC-AUTO",
                Status = TicketStatus.Open,
                Priority = TicketPriority.Low,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            ticketId = ticket.Id;
        }

        var svc = new AutomationBackgroundService(sp, NullLogger<AutomationBackgroundService>.Instance);
        await svc.RunOnceAsync(CancellationToken.None);

        using (var verify = sp.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var ticket = await db.Tickets.FirstAsync(t => t.Id == ticketId);
            Assert.Equal(TicketPriority.High, ticket.Priority);
            var automation = await db.Automations.FirstAsync();
            Assert.NotNull(automation.LastRunAt);
        }
    }

    [Fact]
    public async Task SlaScheduler_FlagsOverdueTicketsAsBreached()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        int ticketId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var ticket = new Ticket
            {
                Subject = "Overdue",
                Reference = "ESC-SLA",
                Status = TicketStatus.Open,
                Priority = TicketPriority.High,
                FirstResponseDueAt = DateTime.UtcNow.AddHours(-1),
                SlaFirstResponseBreached = false,
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                UpdatedAt = DateTime.UtcNow,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            ticketId = ticket.Id;
        }

        var svc = new SlaMonitorBackgroundService(sp, NullLogger<SlaMonitorBackgroundService>.Instance);
        await svc.RunOnceAsync(CancellationToken.None);

        using (var verify = sp.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var ticket = await db.Tickets.FirstAsync(t => t.Id == ticketId);
            Assert.True(ticket.SlaFirstResponseBreached);
        }
    }

    [Fact]
    public async Task EscalationScheduler_EscalatesMatchingTickets()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        int ticketId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            db.EscalationRules.Add(new EscalationRule
            {
                Name = "Escalate urgent",
                IsActive = true,
                Conditions = "[{\"field\":\"priority\",\"value\":\"urgent\"}]",
                Actions = "[{\"type\":\"escalate\"}]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            var ticket = new Ticket
            {
                Subject = "Angry customer",
                Reference = "ESC-ESC",
                Status = TicketStatus.Open,
                Priority = TicketPriority.Urgent,
                CreatedAt = DateTime.UtcNow.AddHours(-6),
                UpdatedAt = DateTime.UtcNow,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            ticketId = ticket.Id;
        }

        var svc = new EscalationBackgroundService(sp, NullLogger<EscalationBackgroundService>.Instance);
        await svc.RunOnceAsync(CancellationToken.None);

        using (var verify = sp.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var ticket = await db.Tickets.FirstAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.Escalated, ticket.Status);
        }
    }

    [Fact]
    public void AddEscalated_RegistersDispatcherRunnerAndSchedulers()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        EscalatedServiceCollectionExtensions.AddEscalated(
            services, configuration,
            configureDb: o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        // The real dispatcher is the default (not the no-op).
        var dispatcher = services.Single(d => d.ServiceType == typeof(IEscalatedEventDispatcher));
        Assert.Equal(typeof(WorkflowEventDispatcher), dispatcher.ImplementationType);

        // The workflow runner + executor are resolvable.
        Assert.Contains(services, d => d.ServiceType == typeof(WorkflowRunnerService));
        Assert.Contains(services, d => d.ServiceType == typeof(WorkflowExecutorService));

        // All three schedulers are registered as hosted services.
        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();
        Assert.Contains(typeof(AutomationBackgroundService), hosted);
        Assert.Contains(typeof(SlaMonitorBackgroundService), hosted);
        Assert.Contains(typeof(EscalationBackgroundService), hosted);
    }
}
