using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Events;

/// <summary>
/// End-to-end wiring tests for <see cref="WorkflowEventDispatcher"/>.
///
/// These stand up a real DI container (in-memory EF Core + the workflow
/// engine graph + the dispatcher registered as the default
/// <see cref="IEscalatedEventDispatcher"/>) and drive the actual mutation
/// sites (<see cref="TicketService"/>) to prove that dispatching a domain
/// event causes the configured Workflow to fire — the behaviour that was
/// silently inert while <see cref="NullEventDispatcher"/> was the only
/// registered dispatcher.
/// </summary>
public class WorkflowEventDispatcherTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<EscalatedOptions>>(Options.Create(new EscalatedOptions()));
        services.AddDbContext<EscalatedDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<TicketService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowExecutorService>();
        services.AddScoped<WorkflowRunnerService>();

        // The system under test: the real dispatcher as the default.
        services.AddSingleton<IEscalatedEventDispatcher, WorkflowEventDispatcher>();

        return services.BuildServiceProvider();
    }

    private static async Task<Workflow> SeedWorkflowAsync(
        IServiceProvider sp, string trigger, string actions,
        string conditions = "{}", int position = 0, bool stopOnMatch = false,
        string name = "Test")
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var wf = new Workflow
        {
            Name = name,
            TriggerEvent = trigger,
            Conditions = conditions,
            Actions = actions,
            IsActive = true,
            Position = position,
            StopOnMatch = stopOnMatch,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Workflows.Add(wf);
        await db.SaveChangesAsync();
        return wf;
    }

    private static async Task<Ticket> SeedTicketAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var ticket = new Ticket
        {
            Subject = "Seeded",
            Description = "body",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Low,
            Reference = "ESC-SEED",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task TicketCreated_FiresMatchingWorkflow()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        await SeedWorkflowAsync(sp, "ticket.created",
            actions: "[{\"type\":\"add_note\",\"value\":\"auto-triage\"}]");

        // Drive the real mutation site: creating a ticket dispatches
        // TicketCreatedEvent through the registered dispatcher.
        using (var scope = sp.CreateScope())
        {
            var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
            await tickets.CreateAsync("Help me", "please", requesterId: "1");
        }

        using (var verify = sp.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var log = Assert.Single(db.WorkflowLogs.ToList());
            Assert.Equal("ticket.created", log.TriggerEvent);
            Assert.True(log.ConditionsMatched);
            Assert.NotNull(log.CompletedAt);
            // The add_note action ran, proving the executor was invoked.
            var note = Assert.Single(db.Replies.ToList());
            Assert.Equal("auto-triage", note.Body);
            Assert.True(note.IsInternalNote);
        }
    }

    [Fact]
    public async Task ReplyCreated_FiresReplyWorkflow()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var ticket = await SeedTicketAsync(sp);
        await SeedWorkflowAsync(sp, "reply.created",
            actions: "[{\"type\":\"change_priority\",\"value\":\"high\"}]");

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
            var tracked = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
            await tickets.AddReplyAsync(tracked, "a customer reply", authorType: "customer", isNote: false);
        }

        using (var verify = sp.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var log = Assert.Single(db.WorkflowLogs.ToList());
            Assert.Equal("reply.created", log.TriggerEvent);
            Assert.True(log.ConditionsMatched);
            var refreshed = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
            Assert.Equal(TicketPriority.High, refreshed.Priority);
        }
    }

    [Fact]
    public async Task UnmappedEvent_IsIgnored()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var ticket = await SeedTicketAsync(sp);
        // A workflow exists, but the dispatched event maps to no trigger.
        await SeedWorkflowAsync(sp, "ticket.created",
            actions: "[{\"type\":\"add_note\",\"value\":\"auto\"}]");

        var dispatcher = sp.GetRequiredService<IEscalatedEventDispatcher>();
        await dispatcher.DispatchAsync(new TicketUnassignedEvent(ticket, "5", null));

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        Assert.Empty(db.WorkflowLogs.ToList());
        Assert.Empty(db.Replies.ToList());
    }

    [Fact]
    public async Task WorkflowAction_DoesNotCascadeIntoItself()
    {
        // A ticket.priority_changed workflow whose action itself changes the
        // priority would re-dispatch ticket.priority_changed forever without
        // the re-entrancy guard. It must fire exactly once.
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var ticket = await SeedTicketAsync(sp);
        await SeedWorkflowAsync(sp, "ticket.priority_changed",
            actions: "[{\"type\":\"change_priority\",\"value\":\"high\"}]");

        var dispatcher = sp.GetRequiredService<IEscalatedEventDispatcher>();
        await dispatcher.DispatchAsync(
            new TicketPriorityChangedEvent(ticket, TicketPriority.Low, TicketPriority.High, null));

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        // Exactly one log — the nested re-dispatch was suppressed.
        Assert.Single(db.WorkflowLogs.ToList());
    }
}
