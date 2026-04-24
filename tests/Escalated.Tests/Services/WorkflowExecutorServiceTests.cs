using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkflowExecutorService"/>.
/// Uses an in-memory EF Core DB + real TicketService / AssignmentService
/// so the dispatch path is exercised end-to-end. Mirrors the test
/// coverage of the NestJS reference workflow-executor.service.ts.
/// </summary>
public class WorkflowExecutorServiceTests
{
    private (WorkflowExecutorService executor, EscalatedDbContext db, Mock<IEscalatedEventDispatcher> events)
        CreateExecutor()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var options = TestHelpers.DefaultOptions();
        var tickets = new TicketService(db, events.Object, options);
        var assignments = new AssignmentService(db, events.Object);
        var executor = new WorkflowExecutorService(db, tickets, assignments,
            NullLogger<WorkflowExecutorService>.Instance);
        return (executor, db, events);
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
            GuestName = "Alice",
            GuestEmail = "alice@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task Execute_ChangePriority_UpdatesTicket()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"change_priority\",\"value\":\"high\"}]");

        Assert.Equal(TicketPriority.High, ticket.Priority);
    }

    [Fact]
    public async Task Execute_ChangeStatus_UpdatesTicket()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"change_status\",\"value\":\"resolved\"}]");

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
    }

    [Fact]
    public async Task Execute_AssignAgent_AttachesAgentId()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"assign_agent\",\"value\":\"42\"}]");

        Assert.Equal(42, ticket.AssignedTo);
    }

    [Fact]
    public async Task Execute_AssignAgent_NonNumericValueIsNoop()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"assign_agent\",\"value\":\"not-a-number\"}]");

        Assert.Null(ticket.AssignedTo);
    }

    [Fact]
    public async Task Execute_SetDepartment_AttachesId()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"set_department\",\"value\":\"7\"}]");

        Assert.Equal(7, ticket.DepartmentId);
    }

    [Fact]
    public async Task Execute_AddTag_BySlug_AttachesTagViaPivot()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);
        db.Tags.Add(new Tag { Id = 5, Name = "Urgent", Slug = "urgent" });
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"add_tag\",\"value\":\"urgent\"}]");

        Assert.Contains(db.TicketTags, tt => tt.TicketId == ticket.Id && tt.TagId == 5);
    }

    [Fact]
    public async Task Execute_AddTag_ById_FallbackWhenSlugMisses()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);
        db.Tags.Add(new Tag { Id = 5, Name = "Urgent", Slug = "urgent" });
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"add_tag\",\"value\":\"5\"}]");

        Assert.Contains(db.TicketTags, tt => tt.TicketId == ticket.Id && tt.TagId == 5);
    }

    [Fact]
    public async Task Execute_AddTag_UnknownTagSkipped()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"add_tag\",\"value\":\"missing\"}]");

        Assert.Empty(db.TicketTags);
    }

    [Fact]
    public async Task Execute_RemoveTag_DetachesFromPivot()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);
        db.Tags.Add(new Tag { Id = 5, Name = "Urgent", Slug = "urgent" });
        db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = 5 });
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"remove_tag\",\"value\":\"urgent\"}]");

        Assert.Empty(db.TicketTags);
    }

    [Fact]
    public async Task Execute_AddNote_PersistsInternalReply()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"add_note\",\"value\":\"Triaged\"}]");

        var reply = db.Replies.Single(r => r.TicketId == ticket.Id);
        Assert.Equal("Triaged", reply.Body);
        Assert.True(reply.IsInternalNote);
        Assert.Equal("note", reply.Type);
    }

    [Fact]
    public async Task Execute_AddNote_BlankValueSkipped()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"add_note\",\"value\":\"   \"}]");

        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task Execute_InsertCannedReply_InterpolatesAndPersistsPublicReply()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"insert_canned_reply\",\"value\":\"Hi {{guest_name}} ref {{reference}}\"}]");

        var reply = db.Replies.Single(r => r.TicketId == ticket.Id);
        Assert.Equal("Hi Alice ref ESC-1", reply.Body);
        Assert.False(reply.IsInternalNote);
        Assert.Equal("reply", reply.Type);
    }

    [Fact]
    public async Task Execute_InsertCannedReply_UnknownVariableLeftLiteral()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"insert_canned_reply\",\"value\":\"Hi {{not_a_field}}\"}]");

        var reply = db.Replies.Single(r => r.TicketId == ticket.Id);
        Assert.Equal("Hi {{not_a_field}}", reply.Body);
    }

    [Fact]
    public async Task Execute_UnknownActionTypeSkipped()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        var result = await executor.ExecuteAsync(ticket,
            "[{\"type\":\"future_action\",\"value\":\"x\"}]");

        Assert.Single(result);
        Assert.Equal(TicketPriority.Low, ticket.Priority); // unchanged
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task Execute_MalformedJsonReturnsEmptyActions()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        var result = await executor.ExecuteAsync(ticket, "not json");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Execute_NullReturnsEmptyActions()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        var result = await executor.ExecuteAsync(ticket, null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Execute_EmptyStringReturnsEmptyActions()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        var result = await executor.ExecuteAsync(ticket, "");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Execute_OneFailedActionDoesNotStopOthers()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        // add_tag for a missing tag is a no-op (returns), so drive a
        // real failure: change_status to an invalid string becomes
        // TicketStatus.Open via Parse — which IS a transition from Open,
        // but CanTransitionTo(Open → Open) is false → throws. The next
        // action (change_priority) should still run.
        await executor.ExecuteAsync(ticket,
            "[{\"type\":\"change_status\",\"value\":\"open\"},"
            + "{\"type\":\"change_priority\",\"value\":\"urgent\"}]");

        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
    }

    [Fact]
    public async Task Execute_ReturnsParsedActionList()
    {
        var (executor, db, _) = CreateExecutor();
        var ticket = await SeedTicketAsync(db);

        var result = await executor.ExecuteAsync(ticket,
            "[{\"type\":\"change_priority\",\"value\":\"high\"},"
            + "{\"type\":\"add_note\",\"value\":\"go\"}]");

        Assert.Equal(2, result.Count);
        Assert.Equal("change_priority", result[0]["type"]?.ToString());
        Assert.Equal("add_note", result[1]["type"]?.ToString());
    }
}
