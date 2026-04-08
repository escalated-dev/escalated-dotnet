using Escalated.Enums;
using Escalated.Events;
using Escalated.Services;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class TicketServiceTests
{
    private TicketService CreateService(out Mock<IEscalatedEventDispatcher> events, string? dbName = null)
    {
        var db = TestHelpers.CreateInMemoryDb(dbName);
        events = TestHelpers.MockEventDispatcher();
        return new TicketService(db, events.Object, TestHelpers.DefaultOptions());
    }

    [Fact]
    public async Task CreateAsync_SetsReferenceAndStatus()
    {
        var service = CreateService(out var events);

        var ticket = await service.CreateAsync("Test Subject", "Description");

        Assert.NotNull(ticket);
        Assert.StartsWith("ESC-", ticket.Reference);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        events.Verify(e => e.DispatchAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithGuestEmail_GeneratesToken()
    {
        var service = CreateService(out _);

        var ticket = await service.CreateAsync("Guest Ticket", "desc", guestEmail: "guest@test.com", guestName: "Test Guest");

        Assert.NotNull(ticket.GuestToken);
        Assert.Equal("guest@test.com", ticket.GuestEmail);
        Assert.Equal("Test Guest", ticket.GuestName);
    }

    [Fact]
    public async Task ChangeStatusAsync_TransitionsCorrectly()
    {
        var service = CreateService(out var events);
        var ticket = await service.CreateAsync("Test", "desc");

        var updated = await service.ChangeStatusAsync(ticket, TicketStatus.InProgress);

        Assert.Equal(TicketStatus.InProgress, updated.Status);
        events.Verify(e => e.DispatchAsync(It.IsAny<TicketStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeStatusAsync_Resolved_SetsTimestamp()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Test", "desc");

        var resolved = await service.ChangeStatusAsync(ticket, TicketStatus.Resolved);

        Assert.Equal(TicketStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task ChangeStatusAsync_Closed_SetsTimestamp()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Test", "desc");

        var closed = await service.ChangeStatusAsync(ticket, TicketStatus.Closed);

        Assert.Equal(TicketStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);
    }

    [Fact]
    public async Task ChangeStatusAsync_Reopened_ClearsTimestamps()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Test", "desc");
        await service.ChangeStatusAsync(ticket, TicketStatus.Resolved);

        var reopened = await service.ChangeStatusAsync(ticket, TicketStatus.Reopened);

        Assert.Equal(TicketStatus.Reopened, reopened.Status);
        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.ClosedAt);
    }

    [Fact]
    public async Task ChangeStatusAsync_InvalidTransition_Throws()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Test", "desc");
        await service.ChangeStatusAsync(ticket, TicketStatus.Closed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ChangeStatusAsync(ticket, TicketStatus.InProgress));
    }

    [Fact]
    public async Task ChangePriorityAsync_UpdatesAndLogs()
    {
        var service = CreateService(out var events);
        var ticket = await service.CreateAsync("Test", "desc");

        var updated = await service.ChangePriorityAsync(ticket, TicketPriority.High);

        Assert.Equal(TicketPriority.High, updated.Priority);
        events.Verify(e => e.DispatchAsync(It.IsAny<TicketPriorityChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddReplyAsync_CreatesReply()
    {
        var service = CreateService(out var events);
        var ticket = await service.CreateAsync("Test", "desc");

        var reply = await service.AddReplyAsync(ticket, "This is a reply", authorId: 1);

        Assert.Equal("This is a reply", reply.Body);
        Assert.False(reply.IsInternalNote);
        events.Verify(e => e.DispatchAsync(It.IsAny<ReplyCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddReplyAsync_InternalNote_SetsFlag()
    {
        var service = CreateService(out var events);
        var ticket = await service.CreateAsync("Test", "desc");

        var note = await service.AddReplyAsync(ticket, "Internal note", authorId: 1, isNote: true);

        Assert.True(note.IsInternalNote);
        Assert.Equal("note", note.Type);
        events.Verify(e => e.DispatchAsync(It.IsAny<InternalNoteAddedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        var service = CreateService(out _);
        await service.CreateAsync("Open 1", "desc");
        await service.CreateAsync("Open 2", "desc");
        var t3 = await service.CreateAsync("Resolved", "desc");
        await service.ChangeStatusAsync(t3, TicketStatus.Resolved);

        var (items, count) = await service.ListAsync(new TicketListFilters { Status = TicketStatus.Open });

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ListAsync_SearchBySubject()
    {
        var service = CreateService(out _);
        await service.CreateAsync("Login issue", "desc");
        await service.CreateAsync("Payment problem", "desc");

        var (items, count) = await service.ListAsync(new TicketListFilters { Search = "Login" });

        Assert.Equal(1, count);
        Assert.Equal("Login issue", items[0].Subject);
    }

    [Fact]
    public async Task FindByReferenceAsync_Works()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Test", "desc");

        var found = await service.FindByReferenceAsync(ticket.Reference);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found!.Id);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var service = CreateService(out _);
        var ticket = await service.CreateAsync("Original", "desc");

        var updated = await service.UpdateAsync(ticket, subject: "Updated Subject");

        Assert.Equal("Updated Subject", updated.Subject);
    }
}
