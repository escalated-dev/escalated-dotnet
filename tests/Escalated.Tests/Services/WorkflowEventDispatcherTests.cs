using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkflowEventDispatcher"/>. Verifies the
/// event → trigger mapping and that the decorator both forwards to the
/// host's inner dispatcher and kicks the runner.
/// </summary>
public class WorkflowEventDispatcherTests
{
    private (WorkflowEventDispatcher dispatcher, Mock<IEscalatedEventDispatcher> inner, EscalatedDbContext db)
        Create()
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

        var inner = new Mock<IEscalatedEventDispatcher>();
        inner.Setup(x => x.DispatchAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var dispatcher = new WorkflowEventDispatcher(
            inner.Object, runner,
            NullLogger<WorkflowEventDispatcher>.Instance);

        return (dispatcher, inner, db);
    }

    private static Ticket NewTicket() => new()
    {
        Id = 1,
        Subject = "Help",
        Status = TicketStatus.Open,
        Priority = TicketPriority.Low,
    };

    [Theory]
    [InlineData("ticket.created", typeof(TicketCreatedEvent))]
    [InlineData("ticket.updated", typeof(TicketUpdatedEvent))]
    [InlineData("ticket.status_changed", typeof(TicketStatusChangedEvent))]
    [InlineData("ticket.status_changed", typeof(TicketResolvedEvent))]
    [InlineData("ticket.status_changed", typeof(TicketClosedEvent))]
    [InlineData("ticket.reopened", typeof(TicketReopenedEvent))]
    [InlineData("ticket.assigned", typeof(TicketAssignedEvent))]
    [InlineData("ticket.priority_changed", typeof(TicketPriorityChangedEvent))]
    [InlineData("ticket.department_changed", typeof(DepartmentChangedEvent))]
    [InlineData("ticket.tagged", typeof(TagAddedEvent))]
    [InlineData("ticket.tagged", typeof(TagRemovedEvent))]
    public void Resolve_MapsEventTypeToExpectedTrigger(string expectedTrigger, Type eventType)
    {
        var ticket = NewTicket();
        object @event = eventType switch
        {
            _ when eventType == typeof(TicketCreatedEvent) => new TicketCreatedEvent(ticket),
            _ when eventType == typeof(TicketUpdatedEvent) => new TicketUpdatedEvent(ticket),
            _ when eventType == typeof(TicketStatusChangedEvent) =>
                new TicketStatusChangedEvent(ticket, TicketStatus.Open, TicketStatus.InProgress, null),
            _ when eventType == typeof(TicketResolvedEvent) => new TicketResolvedEvent(ticket, null),
            _ when eventType == typeof(TicketClosedEvent) => new TicketClosedEvent(ticket, null),
            _ when eventType == typeof(TicketReopenedEvent) => new TicketReopenedEvent(ticket, null),
            _ when eventType == typeof(TicketAssignedEvent) => new TicketAssignedEvent(ticket, 7, null),
            _ when eventType == typeof(TicketPriorityChangedEvent) =>
                new TicketPriorityChangedEvent(ticket, TicketPriority.Low, TicketPriority.High, null),
            _ when eventType == typeof(DepartmentChangedEvent) =>
                new DepartmentChangedEvent(ticket, null, 2, null),
            _ when eventType == typeof(TagAddedEvent) => new TagAddedEvent(ticket, 5, null),
            _ when eventType == typeof(TagRemovedEvent) => new TagRemovedEvent(ticket, 5, null),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };

        var (trigger, resolvedTicket) = WorkflowEventDispatcher.Resolve(@event);

        Assert.Equal(expectedTrigger, trigger);
        Assert.Same(ticket, resolvedTicket);
    }

    [Fact]
    public void Resolve_ReplyCreatedEvent_ReturnsReplyTicket()
    {
        var ticket = NewTicket();
        var reply = new Reply { Id = 1, Ticket = ticket };

        var (trigger, resolvedTicket) = WorkflowEventDispatcher.Resolve(new ReplyCreatedEvent(reply));

        Assert.Equal("reply.created", trigger);
        Assert.Same(ticket, resolvedTicket);
    }

    [Fact]
    public void Resolve_SlaEvents_ReturnSlaTriggers()
    {
        var ticket = NewTicket();

        var (breachTrigger, _) = WorkflowEventDispatcher.Resolve(
            new SlaBreachedEvent(ticket, "first_response"));
        var (warningTrigger, _) = WorkflowEventDispatcher.Resolve(
            new SlaWarningEvent(ticket, "first_response", 10));

        Assert.Equal("sla.breached", breachTrigger);
        Assert.Equal("sla.warning", warningTrigger);
    }

    [Fact]
    public void Resolve_UnknownEvent_ReturnsNulls()
    {
        var (trigger, ticket) = WorkflowEventDispatcher.Resolve(
            new InternalNoteAddedEvent(new Reply()));

        Assert.Null(trigger);
        Assert.Null(ticket);
    }

    [Fact]
    public async Task DispatchAsync_AlwaysForwardsToInnerDispatcher()
    {
        var (dispatcher, inner, _) = Create();
        var evt = new InternalNoteAddedEvent(new Reply());

        await dispatcher.DispatchAsync(evt);

        inner.Verify(x => x.DispatchAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_KnownEventWithMatchingWorkflow_FiresRunner()
    {
        var (dispatcher, _, db) = Create();
        db.Workflows.Add(new Workflow
        {
            Name = "auto",
            TriggerEvent = "ticket.created",
            Conditions = "{}",
            Actions = "[{\"type\":\"add_note\",\"value\":\"fired\"}]",
            IsActive = true,
        });
        var ticket = NewTicket();
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await dispatcher.DispatchAsync(new TicketCreatedEvent(ticket));

        Assert.Single(db.WorkflowLogs);
        Assert.Single(db.Replies);
    }

    [Fact]
    public async Task DispatchAsync_NoMatchingWorkflow_DoesNotCreateLogs()
    {
        var (dispatcher, _, db) = Create();
        var ticket = NewTicket();
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await dispatcher.DispatchAsync(new TicketCreatedEvent(ticket));

        Assert.Empty(db.WorkflowLogs);
    }

    [Fact]
    public async Task DispatchAsync_UnknownEvent_DoesNotTouchRunner()
    {
        var (dispatcher, _, db) = Create();
        // Add a workflow on ticket.created — should NOT fire for an
        // unrelated InternalNoteAddedEvent.
        db.Workflows.Add(new Workflow
        {
            Name = "auto",
            TriggerEvent = "ticket.created",
            Conditions = "{}",
            Actions = "[]",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        await dispatcher.DispatchAsync(new InternalNoteAddedEvent(new Reply()));

        Assert.Empty(db.WorkflowLogs);
    }
}
