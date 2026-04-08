using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class TicketSplitServiceTests
{
    [Fact]
    public async Task SplitAsync_CreatesNewTicketFromReply()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketSplitService(db, events.Object, TestHelpers.DefaultOptions());

        // Create source ticket
        var source = new Ticket
        {
            Subject = "Original Ticket",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            DepartmentId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(source);
        await db.SaveChangesAsync();

        // Create a reply on the source
        var reply = new Reply
        {
            TicketId = source.Id,
            Body = "This should be a separate issue",
            IsInternalNote = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();

        var newTicket = await service.SplitAsync(source, reply, "Split: Separate Issue");

        Assert.NotNull(newTicket);
        Assert.StartsWith("ESC-", newTicket.Reference);
        Assert.Equal("Split: Separate Issue", newTicket.Subject);
        Assert.Equal(TicketStatus.Open, newTicket.Status);
        Assert.Equal(source.Priority, newTicket.Priority);

        // Verify ticket link was created
        var link = await db.TicketLinks.FirstOrDefaultAsync(l => l.ChildTicketId == newTicket.Id);
        Assert.NotNull(link);
        Assert.Equal("split", link!.LinkType);
        Assert.Equal(source.Id, link.ParentTicketId);

        // Verify system notes were added
        var sourceNotes = await db.Replies.Where(r => r.TicketId == source.Id && r.IsInternalNote).ToListAsync();
        Assert.Contains(sourceNotes, n => n.Body.Contains("Split to"));

        var newNotes = await db.Replies.Where(r => r.TicketId == newTicket.Id && r.IsInternalNote).ToListAsync();
        Assert.Contains(newNotes, n => n.Body.Contains("Split from"));
    }

    [Fact]
    public async Task SplitAsync_DefaultSubjectIncludesSourceReference()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketSplitService(db, events.Object, TestHelpers.DefaultOptions());

        var source = new Ticket
        {
            Subject = "Bug Report",
            Reference = "ESC-00010",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(source);
        await db.SaveChangesAsync();

        var reply = new Reply
        {
            TicketId = source.Id,
            Body = "Splitting this",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();

        var newTicket = await service.SplitAsync(source, reply);

        Assert.Contains("ESC-00010", newTicket.Subject);
        Assert.Contains("Bug Report", newTicket.Subject);
    }
}
