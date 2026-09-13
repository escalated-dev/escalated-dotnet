using System.Text.Json;
using Escalated.Controllers.Agent;
using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

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

    /// <summary>The inbox belongs to the signed-in agent, as host authentication identifies them.</summary>
    private static AgentMentionController SignedInAs(AgentMentionController ctrl, string userId)
    {
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test")),
            },
        };

        return ctrl;
    }

    private static async Task<Reply> AddReplyAsync(EscalatedDbContext db, int ticketId)
    {
        var reply = new Reply
        {
            TicketId = ticketId,
            Body = "cc @grace again",
            AuthorId = "1",
            IsInternalNote = true,
            Type = "note",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Replies.Add(reply);
        await db.SaveChangesAsync();

        return reply;
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

        Assert.Equal(1, DataCount(await SignedInAs(ctrl, "7").Index()));
    }

    [Fact]
    public async Task Index_UnreadOnly_FiltersReadMentions()
    {
        var (ctrl, db, reply) = await SeedAsync();

        // Two replies, not two mentions on one: (ReplyId, UserId) is unique, so
        // the same agent cannot be mentioned twice on the same reply. The EF
        // Core InMemory provider enforced no such constraint, which is how this
        // used to pass while asserting a row the schema forbids.
        var second = await AddReplyAsync(db, reply.TicketId);

        db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "7", ReadAt = DateTime.UtcNow });
        db.Mentions.Add(new Mention { ReplyId = second.Id, UserId = "7", CreatedAt = DateTime.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();

        Assert.Equal(2, DataCount(await SignedInAs(ctrl, "7").Index()));
        Assert.Equal(1, DataCount(await SignedInAs(ctrl, "7").Index(unreadOnly: true)));
    }

    [Fact]
    public async Task MarkRead_SetsReadAt()
    {
        var (ctrl, db, reply) = await SeedAsync();
        var mention = new Mention { ReplyId = reply.Id, UserId = "7" };
        db.Mentions.Add(mention);
        await db.SaveChangesAsync();

        var result = await SignedInAs(ctrl, "7").MarkRead(mention.Id);

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

        var result = await SignedInAs(ctrl, "999").MarkRead(mention.Id);

        Assert.IsType<NotFoundResult>(result);
    }
}
