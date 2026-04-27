using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkflowRunnerService"/>.
///
/// Uses an in-memory EF Core DB + real WorkflowEngine + real
/// WorkflowExecutorService so the condition-evaluation / log-writing
/// orchestration path is exercised end-to-end. Assertions inspect the
/// resulting DB state (replies, logs, tag pivots) rather than mocking
/// the executor. Mirrors the NestJS reference
/// <c>workflow-runner.service.ts</c>.
/// </summary>
public class WorkflowRunnerServiceTests
{
    private (WorkflowRunnerService runner, EscalatedDbContext db) CreateRunner()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var options = TestHelpers.DefaultOptions();
        var tickets = new TicketService(db, events.Object, options);
        var assignments = new AssignmentService(db, events.Object, tickets);
        var executor = new WorkflowExecutorService(db, tickets, assignments,
            NullLogger<WorkflowExecutorService>.Instance);
        var runner = new WorkflowRunnerService(
            db, new WorkflowEngine(), executor,
            NullLogger<WorkflowRunnerService>.Instance);
        return (runner, db);
    }

    private async Task<Ticket> SeedTicketAsync(EscalatedDbContext db)
    {
        var ticket = new Ticket
        {
            Subject = "Help",
            Description = "body",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Low,
            Reference = "ESC-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private async Task<Workflow> SeedWorkflowAsync(
        EscalatedDbContext db, string? conditions = null, string actions = "[]",
        bool isActive = true, int position = 0, bool stopOnMatch = false,
        string trigger = "ticket.created", string name = "Test")
    {
        var wf = new Workflow
        {
            Name = name,
            TriggerEvent = trigger,
            Conditions = conditions ?? "{}",
            Actions = actions,
            IsActive = isActive,
            Position = position,
            StopOnMatch = stopOnMatch,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Workflows.Add(wf);
        await db.SaveChangesAsync();
        return wf;
    }

    [Fact]
    public async Task RunForEvent_NoWorkflows_IsNoop()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.Empty(db.WorkflowLogs);
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task RunForEvent_MatchedActiveWorkflow_ExecutesAndLogs()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db,
            actions: "[{\"type\":\"add_note\",\"value\":\"auto\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        var log = db.WorkflowLogs.Single();
        Assert.True(log.ConditionsMatched);
        Assert.NotNull(log.CompletedAt);
        Assert.Null(log.ErrorMessage);
        Assert.Single(db.Replies); // executor ran add_note
    }

    [Fact]
    public async Task RunForEvent_InactiveWorkflow_Skipped()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db, isActive: false,
            actions: "[{\"type\":\"add_note\",\"value\":\"auto\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.Empty(db.WorkflowLogs);
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task RunForEvent_WrongTrigger_Skipped()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db, trigger: "reply.created");

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.Empty(db.WorkflowLogs);
    }

    [Fact]
    public async Task RunForEvent_UnmatchedConditions_LogsButDoesNotExecute()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        // Require status=closed; ticket is open.
        var conditions = "{\"all\":[{\"field\":\"status\",\"operator\":\"equals\",\"value\":\"closed\"}]}";
        await SeedWorkflowAsync(db, conditions: conditions,
            actions: "[{\"type\":\"add_note\",\"value\":\"auto\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        var log = db.WorkflowLogs.Single();
        Assert.False(log.ConditionsMatched);
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task RunForEvent_MalformedConditions_DoesNotMatch()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db, conditions: "{ not valid json");

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.False(db.WorkflowLogs.Single().ConditionsMatched);
    }

    [Fact]
    public async Task RunForEvent_StopOnMatch_HaltsAfterFirstMatch()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db, position: 1, stopOnMatch: true, name: "first",
            actions: "[{\"type\":\"change_priority\",\"value\":\"high\"}]");
        await SeedWorkflowAsync(db, position: 2, name: "second",
            actions: "[{\"type\":\"change_priority\",\"value\":\"urgent\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.Single(db.WorkflowLogs); // only first ran
        Assert.Equal(TicketPriority.High, ticket.Priority); // first won, second didn't override
    }

    [Fact]
    public async Task RunForEvent_StopOnMatch_OnlyAppliesOnMatch()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        var failingCond = "{\"all\":[{\"field\":\"status\",\"operator\":\"equals\",\"value\":\"closed\"}]}";
        await SeedWorkflowAsync(db, position: 1, stopOnMatch: true,
            conditions: failingCond, name: "first");
        await SeedWorkflowAsync(db, position: 2, name: "second",
            actions: "[{\"type\":\"change_priority\",\"value\":\"urgent\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        Assert.Equal(2, db.WorkflowLogs.Count());
        Assert.Equal(TicketPriority.Urgent, ticket.Priority); // second still ran
    }

    [Fact]
    public async Task RunForEvent_ExecutesInPositionOrder()
    {
        var (runner, db) = CreateRunner();
        var ticket = await SeedTicketAsync(db);
        await SeedWorkflowAsync(db, position: 10, name: "later",
            actions: "[{\"type\":\"change_priority\",\"value\":\"urgent\"}]");
        await SeedWorkflowAsync(db, position: 1, name: "earlier",
            actions: "[{\"type\":\"change_priority\",\"value\":\"high\"}]");

        await runner.RunForEventAsync("ticket.created", ticket);

        // Later in position wins because it ran after earlier.
        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
    }
}
