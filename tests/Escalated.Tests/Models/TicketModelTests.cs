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
}
