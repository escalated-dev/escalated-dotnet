using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Services;

public class MergeServiceTests
{
    [Fact]
    public async Task MergeAsync_MovesRepliesAndClosesSource()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new TicketMergeService(db);

        var source = new Ticket
        {
            Subject = "Source",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var target = new Ticket
        {
            Subject = "Target",
            Reference = "ESC-00002",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.AddRange(source, target);
        await db.SaveChangesAsync();

        db.Replies.Add(new Reply
        {
            TicketId = source.Id,
            Body = "Reply on source",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await service.MergeAsync(source, target, 1);

        await db.Entry(source).ReloadAsync();
        Assert.Equal(TicketStatus.Closed, source.Status);
        Assert.Equal(target.Id, source.MergedIntoId);

        // Verify reply was moved to target
        var targetReplies = await db.Replies.Where(r => r.TicketId == target.Id).ToListAsync();
        Assert.Contains(targetReplies, r => r.Body == "Reply on source");
        Assert.Contains(targetReplies, r => r.Body.Contains("merged into"));
    }
}
