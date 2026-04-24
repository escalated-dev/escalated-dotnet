using System.Text;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Controllers;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Controllers;

/// <summary>
/// HTTP-level tests for <see cref="InboundEmailController"/>.
/// Exercises signature verification, adapter dispatch, and the
/// full response shape produced by
/// <see cref="InboundEmailService.ProcessAsync"/>.
/// </summary>
public class InboundEmailControllerTests
{
    private const string Secret = "test-inbound-secret";

    private static (InboundEmailController controller, Data.EscalatedDbContext db)
        CreateController()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var options = Options.Create(new EscalatedOptions
        {
            Email = new EmailOptions
            {
                Domain = "support.example.com",
                InboundSecret = Secret,
            },
        });
        var events = TestHelpers.MockEventDispatcher();
        var tickets = new TicketService(db, events.Object, options);
        var router = new InboundEmailRouter(db, options.Value);
        var service = new InboundEmailService(
            db, tickets, router,
            NullLogger<InboundEmailService>.Instance);

        var parsers = new IInboundEmailParser[]
        {
            new PostmarkInboundParser(),
            new MailgunInboundParser(),
        };

        var controller = new InboundEmailController(db, options, service, parsers)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return (controller, db);
    }

    private static void SetBody(InboundEmailController controller, string json)
    {
        var ctx = controller.ControllerContext.HttpContext;
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        ctx.Request.ContentType = "application/json";
        ctx.Features.Set<IHttpBodyControlFeature>(new TestBodyControl());
    }

    private static void SetSecret(InboundEmailController controller, string? value)
    {
        if (value is null) return;
        controller.ControllerContext.HttpContext.Request.Headers["X-Escalated-Inbound-Secret"] = value;
    }

    [Fact]
    public async Task Inbound_NewTicket_ReturnsCreatedOutcome()
    {
        var (controller, db) = CreateController();
        SetSecret(controller, Secret);
        SetBody(controller, """
            {
                "From": "alice@example.com",
                "FromName": "Alice",
                "To": "support@example.com",
                "Subject": "Help with invoice",
                "TextBody": "The PDF is unreadable."
            }
            """);

        var result = await controller.Inbound("postmark", CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var json = JsonSerializer.Serialize(accepted.Value);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("creatednew", doc.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("created", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("ticketId").GetInt32() > 0);
        Assert.Equal(1, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Inbound_MatchedReply_ReturnsRepliedToExisting()
    {
        var (controller, db) = CreateController();

        // Seed a ticket with id 55 that the canonical In-Reply-To will hit.
        var ticket = new Models.Ticket
        {
            Reference = "ESC-00055",
            Subject = "Existing",
            Status = Enums.TicketStatus.Open,
            Priority = Enums.TicketPriority.Medium,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var ticketId = ticket.Id;

        SetSecret(controller, Secret);
        SetBody(controller, $$"""
            {
                "From": "alice@example.com",
                "To": "support@example.com",
                "Subject": "Re: Existing",
                "TextBody": "Here's more detail.",
                "Headers": [
                    { "Name": "In-Reply-To", "Value": "<ticket-{{ticketId}}@support.example.com>" }
                ]
            }
            """);

        var result = await controller.Inbound("postmark", CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var json = JsonSerializer.Serialize(accepted.Value);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("repliedtoexisting", doc.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(ticketId, doc.RootElement.GetProperty("ticketId").GetInt32());
        Assert.True(doc.RootElement.GetProperty("replyId").GetInt32() > 0);
        // Didn't create a second ticket.
        Assert.Equal(1, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Inbound_Skipped_ReturnsSkipped()
    {
        var (controller, db) = CreateController();
        SetSecret(controller, Secret);
        SetBody(controller, """
            {
                "From": "no-reply@sns.amazonaws.com",
                "To": "support@example.com",
                "Subject": "SubscriptionConfirmation",
                "TextBody": ""
            }
            """);

        var result = await controller.Inbound("postmark", CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var json = JsonSerializer.Serialize(accepted.Value);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("skipped", doc.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Inbound_MissingSecret_Returns401()
    {
        var (controller, _) = CreateController();
        SetBody(controller, """{"From":"a@b.com","To":"s@c.com","Subject":"x","TextBody":"y"}""");

        var result = await controller.Inbound("postmark", CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Inbound_BadSecret_Returns401()
    {
        var (controller, _) = CreateController();
        SetSecret(controller, "wrong");
        SetBody(controller, """{"From":"a@b.com","To":"s@c.com","Subject":"x","TextBody":"y"}""");

        var result = await controller.Inbound("postmark", CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Inbound_UnknownAdapter_Returns400()
    {
        var (controller, _) = CreateController();
        SetSecret(controller, Secret);
        SetBody(controller, """{"From":"a@b.com"}""");

        var result = await controller.Inbound("nonesuch", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>
    /// DefaultHttpContext doesn't attach an IHttpBodyControlFeature, so
    /// Request.EnableBuffering() (called inside the controller) fails.
    /// This stub is enough for the controller to read the body via a
    /// StreamReader without crashing on buffering setup.
    /// </summary>
    private class TestBodyControl : IHttpBodyControlFeature
    {
        public bool AllowSynchronousIO { get; set; } = true;
    }
}

