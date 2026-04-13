using Escalated.Enums;
using Escalated.Models;
using Xunit;

namespace Escalated.Tests.Models;

public class TicketModelTests
{
    [Fact]
    public void GenerateReference_FormatsCorrectly()
    {
        var ticket = new Ticket { Id = 42 };
        var reference = ticket.GenerateReference("ESC");
        Assert.Equal("ESC-00042", reference);
    }

    [Fact]
    public void GenerateReference_CustomPrefix()
    {
        var ticket = new Ticket { Id = 1 };
        var reference = ticket.GenerateReference("SUP");
        Assert.Equal("SUP-00001", reference);
    }

    [Fact]
    public void IsGuest_TrueWhenNoRequesterAndHasToken()
    {
        var ticket = new Ticket
        {
            RequesterType = null,
            RequesterId = null,
            GuestToken = "abc123"
        };
        Assert.True(ticket.IsGuest);
    }

    [Fact]
    public void IsGuest_FalseWhenHasRequester()
    {
        var ticket = new Ticket
        {
            RequesterType = "User",
            RequesterId = 1,
            GuestToken = null
        };
        Assert.False(ticket.IsGuest);
    }

    [Fact]
    public void IsOpen_TrueForOpenStatuses()
    {
        Assert.True(new Ticket { Status = TicketStatus.Open }.IsOpen());
        Assert.True(new Ticket { Status = TicketStatus.InProgress }.IsOpen());
        Assert.True(new Ticket { Status = TicketStatus.Escalated }.IsOpen());
    }

    [Fact]
    public void IsOpen_FalseForResolvedAndClosed()
    {
        Assert.False(new Ticket { Status = TicketStatus.Resolved }.IsOpen());
        Assert.False(new Ticket { Status = TicketStatus.Closed }.IsOpen());
    }

    [Fact]
    public void IsSnoozed_TrueWhenSnoozedUntilInFuture()
    {
        var ticket = new Ticket { SnoozedUntil = DateTime.UtcNow.AddHours(1) };
        Assert.True(ticket.IsSnoozed);
    }

    [Fact]
    public void IsSnoozed_FalseWhenSnoozedUntilInPast()
    {
        var ticket = new Ticket { SnoozedUntil = DateTime.UtcNow.AddHours(-1) };
        Assert.False(ticket.IsSnoozed);
    }

    [Fact]
    public void IsSnoozed_FalseWhenNull()
    {
        var ticket = new Ticket { SnoozedUntil = null };
        Assert.False(ticket.IsSnoozed);
    }

    [Fact]
    public void IsLiveChat_TrueWhenActiveChatSessionExists()
    {
        var ticket = new Ticket();
        ticket.ChatSessions.Add(new ChatSession { Status = "active" });
        Assert.True(ticket.IsLiveChat);
    }

    [Fact]
    public void IsLiveChat_FalseWhenAllChatSessionsEnded()
    {
        var ticket = new Ticket();
        ticket.ChatSessions.Add(new ChatSession { Status = "ended" });
        Assert.False(ticket.IsLiveChat);
    }

    [Fact]
    public void IsLiveChat_FalseWhenNoChatSessions()
    {
        var ticket = new Ticket();
        Assert.False(ticket.IsLiveChat);
    }

    [Fact]
    public void LastReplyAt_ReturnsLatestReplyTimestamp()
    {
        var ticket = new Ticket();
        var older = new Reply { Body = "old", CreatedAt = DateTime.UtcNow.AddHours(-2) };
        var newer = new Reply { Body = "new", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        ticket.Replies.Add(older);
        ticket.Replies.Add(newer);
        Assert.Equal(newer.CreatedAt, ticket.LastReplyAt);
    }

    [Fact]
    public void LastReplyAt_NullWhenNoReplies()
    {
        var ticket = new Ticket();
        Assert.Null(ticket.LastReplyAt);
    }

    [Fact]
    public void PopulateComputedFields_SetsRequesterFromGuestFields()
    {
        var ticket = new Ticket
        {
            GuestName = "Alice",
            GuestEmail = "alice@example.com"
        };
        ticket.PopulateComputedFields();
        Assert.Equal("Alice", ticket.RequesterName);
        Assert.Equal("alice@example.com", ticket.RequesterEmail);
    }

    [Fact]
    public void PopulateComputedFields_DoesNotOverrideExistingValues()
    {
        var ticket = new Ticket
        {
            GuestName = "Alice",
            GuestEmail = "alice@example.com",
            RequesterName = "Bob",
            RequesterEmail = "bob@example.com"
        };
        ticket.PopulateComputedFields();
        Assert.Equal("Bob", ticket.RequesterName);
        Assert.Equal("bob@example.com", ticket.RequesterEmail);
    }
}
