using System.Text.Json;
using Escalated.Controllers.Agent;
using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Escalated.Tests.Controllers;

public class AgentMentionControllerTests
{
    private static async Task<(AgentMentionController Ctrl, EscalatedDbContext Db, Reply Reply)> SeedAsync()
    {
        var db = TestHelpers.CreateInMemoryDb();

        var ticket = new Ticket
        {
            Reference = "ESC-00001",
            Subject = "Need help",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var reply = new Reply
        {
            TicketId = ticket.Id,
            Body = "cc @grace",
            AuthorId = "1",
            IsInternalNote = true,
            Type = "note",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();

        return (new AgentMentionController(db), db, reply);
    }

    private static int DataCount(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetArrayLength();
    }

    [Fact]
    public async Task Index_ListsOnlyTheAgentsMentions()
    {
        var (ctrl, db, reply) = await SeedAsync();
        db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "7" });
        db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "9" });
        await db.SaveChangesAsync();

        Assert.Equal(1, DataCount(await ctrl.Index("7")));
    }

    [Fact]
    public async Task Index_UnreadOnly_FiltersReadMentions()
    {
        var (ctrl, db, reply) = await SeedAsync();
        db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "7", ReadAt = DateTime.UtcNow });
        db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "7", CreatedAt = DateTime.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();

        Assert.Equal(2, DataCount(await ctrl.Index("7")));
        Assert.Equal(1, DataCount(await ctrl.Index("7", unreadOnly: true)));
    }

    [Fact]
    public async Task MarkRead_SetsReadAt()
    {
        var (ctrl, db, reply) = await SeedAsync();
        var mention = new Mention { ReplyId = reply.Id, UserId = "7" };
        db.Mentions.Add(mention);
        await db.SaveChangesAsync();

        var result = await ctrl.MarkRead(mention.Id, "7");

        Assert.IsType<OkObjectResult>(result);
        var stored = Assert.Single(db.Mentions);
        Assert.NotNull(stored.ReadAt);
    }

    [Fact]
    public async Task MarkRead_ForeignUser_ReturnsNotFound()
    {
        var (ctrl, db, reply) = await SeedAsync();
        var mention = new Mention { ReplyId = reply.Id, UserId = "7" };
        db.Mentions.Add(mention);
        await db.SaveChangesAsync();

        var result = await ctrl.MarkRead(mention.Id, "999");

        Assert.IsType<NotFoundResult>(result);
    }
}
