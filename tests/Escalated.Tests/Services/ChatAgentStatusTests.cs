using Escalated.Models;
using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Live chat goes only to agents whose own status is <c>online</c>
/// (<see cref="AgentProfile.ChatStatus"/>, ADR 2026-11-05-agent-panel-data),
/// as escalated-laravel's chat routing reads it. Away and offline agents, and
/// agents with no profile, keep chat unavailable and get no chats.
/// </summary>
public class ChatAgentStatusTests
{
    [Theory]
    [InlineData("online", "routed")]
    [InlineData("away", null)]
    [InlineData("offline", null)]
    [InlineData(null, null)]
    public async Task RoutingGivesTheRuleAgentTheChatOnlyWhileOnline(string? status, string? expected)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var billing = new Department { Name = "Billing", Slug = "billing" };
        db.Departments.Add(billing);
        await db.SaveChangesAsync();
        db.ChatRoutingRules.Add(new ChatRoutingRule { Name = "Billing", DepartmentId = billing.Id, AgentId = "routed", IsActive = true });
        if (status != null)
        {
            db.AgentProfiles.Add(new AgentProfile { UserId = "routed", ChatStatus = status });
        }
        await db.SaveChangesAsync();

        var route = await new ChatRoutingService(db).ResolveAsync(billing.Id);

        Assert.Equal(billing.Id, route.DepartmentId);
        Assert.Equal(expected, route.AgentId);
    }

    [Fact]
    public async Task AChatStartedWhileTheAgentIsAwayWaitsInTheQueue()
    {
        var db = TestHelpers.CreateInMemoryDb();
        db.ChatRoutingRules.Add(new ChatRoutingRule { Name = "All", AgentId = "routed", IsActive = true });
        db.AgentProfiles.Add(new AgentProfile { UserId = "routed", ChatStatus = "away" });
        await db.SaveChangesAsync();
        var events = TestHelpers.MockEventDispatcher();
        var tickets = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var chats = new ChatSessionService(db, tickets, new ChatRoutingService(db), events.Object, TestHelpers.DefaultOptions());

        var waiting = await chats.StartAsync("Ada", "ada@example.com", "Hello");
        Assert.Equal("waiting", waiting.Status);
        Assert.Null(waiting.AgentId);

        db.AgentProfiles.Single(p => p.UserId == "routed").ChatStatus = "online";
        await db.SaveChangesAsync();
        var routed = await chats.StartAsync("Bea", "bea@example.com", "Hello");
        Assert.Equal("routed", routed.AgentId);
    }

    [Fact]
    public async Task ChatIsAvailableOnlyWhileAnAgentWithCapacityIsOnline()
    {
        var db = TestHelpers.CreateInMemoryDb();
        db.AgentCapacities.Add(new AgentCapacity { UserId = "a", Channel = "chat", MaxConcurrent = 3, CurrentCount = 0 });
        db.AgentCapacities.Add(new AgentCapacity { UserId = "b", Channel = "chat", MaxConcurrent = 3, CurrentCount = 0 });
        db.AgentProfiles.Add(new AgentProfile { UserId = "a", ChatStatus = "away" });
        await db.SaveChangesAsync();
        var availability = new ChatAvailabilityService(db, new BusinessHoursCalculator(db));

        Assert.False(await availability.IsAvailableAsync());

        db.AgentProfiles.Add(new AgentProfile { UserId = "b", ChatStatus = "online" });
        await db.SaveChangesAsync();
        Assert.True(await availability.IsAvailableAsync());
    }
}
