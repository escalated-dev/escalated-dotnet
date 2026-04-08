using Escalated.Enums;
using Escalated.Events;
using Escalated.Services;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class ChatSessionServiceTests
{
    private ChatSessionService CreateService(out Mock<IEscalatedEventDispatcher> events, string? dbName = null)
    {
        var db = TestHelpers.CreateInMemoryDb(dbName);
        events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var routingService = new ChatRoutingService(db);
        return new ChatSessionService(db, ticketService, routingService, events.Object, TestHelpers.DefaultOptions());
    }

    [Fact]
    public async Task StartAsync_CreatesSessionAndTicket()
    {
        var service = CreateService(out var events);

        var session = await service.StartAsync("Test Visitor", "visitor@test.com", "Hello!");

        Assert.NotNull(session);
        Assert.Equal("Test Visitor", session.VisitorName);
        Assert.Equal("visitor@test.com", session.VisitorEmail);
        Assert.Equal("waiting", session.Status);
        Assert.True(session.TicketId > 0);
    }

    [Fact]
    public async Task AcceptAsync_TransitionsToActive()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor", initialMessage: "Hi");

        var accepted = await service.AcceptAsync(session.Id, agentId: 42);

        Assert.Equal("active", accepted.Status);
        Assert.Equal(42, accepted.AgentId);
        Assert.NotNull(accepted.AcceptedAt);
    }

    [Fact]
    public async Task AcceptAsync_ThrowsForNonWaiting()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor");
        await service.AcceptAsync(session.Id, agentId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptAsync(session.Id, agentId: 2));
    }

    [Fact]
    public async Task SendMessageAsync_CreatesReply()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor");

        var reply = await service.SendMessageAsync(session.Id, "Hello from visitor");

        Assert.NotNull(reply);
        Assert.Equal("Hello from visitor", reply.Body);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsForEndedSession()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor");
        await service.EndAsync(session.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendMessageAsync(session.Id, "too late"));
    }

    [Fact]
    public async Task EndAsync_TransitionsToEnded()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor");

        var ended = await service.EndAsync(session.Id);

        Assert.Equal("ended", ended.Status);
        Assert.NotNull(ended.EndedAt);
    }

    [Fact]
    public async Task EndAsync_ThrowsForAlreadyEnded()
    {
        var service = CreateService(out _);
        var session = await service.StartAsync("Visitor");
        await service.EndAsync(session.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EndAsync(session.Id));
    }

    [Fact]
    public async Task GetWaitingSessionsAsync_ReturnsOnlyWaiting()
    {
        var service = CreateService(out _);
        await service.StartAsync("Visitor1");
        var s2 = await service.StartAsync("Visitor2");
        await service.AcceptAsync(s2.Id, agentId: 1);

        var waiting = await service.GetWaitingSessionsAsync();

        Assert.Single(waiting);
        Assert.Equal("Visitor1", waiting[0].VisitorName);
    }

    [Fact]
    public async Task GetActiveSessionsForAgentAsync_FiltersCorrectly()
    {
        var service = CreateService(out _);
        var s1 = await service.StartAsync("Visitor1");
        var s2 = await service.StartAsync("Visitor2");
        await service.AcceptAsync(s1.Id, agentId: 10);
        await service.AcceptAsync(s2.Id, agentId: 20);

        var agentSessions = await service.GetActiveSessionsForAgentAsync(10);

        Assert.Single(agentSessions);
        Assert.Equal("Visitor1", agentSessions[0].VisitorName);
    }
}
