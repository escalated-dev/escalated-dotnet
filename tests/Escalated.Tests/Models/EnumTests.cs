using Escalated.Enums;
using Xunit;

namespace Escalated.Tests.Models;

public class EnumTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Reopened, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.Reopened, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, false)]
    public void CanTransitionTo_ReturnsExpected(TicketStatus from, TicketStatus to, bool expected)
    {
        Assert.Equal(expected, from.CanTransitionTo(to));
    }

    [Fact]
    public void TicketPriority_Parse_ReturnsCorrectValue()
    {
        Assert.Equal(TicketPriority.High, TicketPriorityExtensions.Parse("high"));
        Assert.Equal(TicketPriority.Low, TicketPriorityExtensions.Parse("low"));
        Assert.Equal(TicketPriority.Medium, TicketPriorityExtensions.Parse("unknown"));
    }

    [Fact]
    public void TicketStatus_Parse_ReturnsCorrectValue()
    {
        Assert.Equal(TicketStatus.InProgress, TicketStatusExtensions.Parse("in_progress"));
        Assert.Equal(TicketStatus.WaitingOnCustomer, TicketStatusExtensions.Parse("waiting_on_customer"));
        Assert.Equal(TicketStatus.Open, TicketStatusExtensions.Parse("unknown"));
    }

    [Fact]
    public void TicketPriority_NumericWeight_Ascending()
    {
        Assert.True(TicketPriority.Low.NumericWeight() < TicketPriority.Medium.NumericWeight());
        Assert.True(TicketPriority.Medium.NumericWeight() < TicketPriority.High.NumericWeight());
        Assert.True(TicketPriority.High.NumericWeight() < TicketPriority.Urgent.NumericWeight());
        Assert.True(TicketPriority.Urgent.NumericWeight() < TicketPriority.Critical.NumericWeight());
    }

    [Fact]
    public void TicketStatus_IsOpen_CorrectForAllStatuses()
    {
        Assert.True(TicketStatus.Open.IsOpen());
        Assert.True(TicketStatus.InProgress.IsOpen());
        Assert.True(TicketStatus.WaitingOnCustomer.IsOpen());
        Assert.True(TicketStatus.WaitingOnAgent.IsOpen());
        Assert.True(TicketStatus.Escalated.IsOpen());
        Assert.True(TicketStatus.Reopened.IsOpen());
        Assert.False(TicketStatus.Resolved.IsOpen());
        Assert.False(TicketStatus.Closed.IsOpen());
    }
}
