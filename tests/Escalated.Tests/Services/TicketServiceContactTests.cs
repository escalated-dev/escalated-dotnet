using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Integration-style tests for the Contact dedupe wire-up in
/// TicketService.CreateAsync. Uses the in-memory EF provider.
/// </summary>
public class TicketServiceContactTests
{
    [Fact]
    public async Task CreateAsync_WithGuestEmail_CreatesContactAndLinksTicket()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = await service.CreateAsync(
            subject: "Help",
            description: "d",
            guestName: "Alice",
            guestEmail: "alice@example.com");

        Assert.NotNull(ticket.ContactId);
        var contact = db.Contacts.Single();
        Assert.Equal("alice@example.com", contact.Email);
        Assert.Equal("Alice", contact.Name);
        Assert.Equal(contact.Id, ticket.ContactId);
    }

    [Fact]
    public async Task CreateAsync_RepeatGuestEmail_DedupesOntoSameContact()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketService(db, events.Object, TestHelpers.DefaultOptions());

        var t1 = await service.CreateAsync(
            subject: "First", description: "d",
            guestName: "Alice", guestEmail: "alice@example.com");

        // Casing variant → same Contact
        var t2 = await service.CreateAsync(
            subject: "Second", description: "d",
            guestName: "Alice", guestEmail: "ALICE@Example.COM");

        Assert.Single(db.Contacts);
        Assert.Equal(t1.ContactId, t2.ContactId);
    }

    [Fact]
    public async Task CreateAsync_NoGuestEmail_LeavesContactIdNull()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = await service.CreateAsync(
            subject: "Help", description: "d",
            requesterId: 42, requesterType: "User");

        Assert.Null(ticket.ContactId);
        Assert.Empty(db.Contacts);
    }

    [Fact]
    public async Task CreateAsync_FillsBlankNameOnExistingContact()
    {
        var db = TestHelpers.CreateInMemoryDb();
        db.Contacts.Add(new Contact
        {
            Email = "alice@example.com",
            Name = null,
            Metadata = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var events = TestHelpers.MockEventDispatcher();
        var service = new TicketService(db, events.Object, TestHelpers.DefaultOptions());

        await service.CreateAsync(
            subject: "Help", description: "d",
            guestName: "Alice", guestEmail: "alice@example.com");

        var contact = db.Contacts.Single();
        Assert.Equal("Alice", contact.Name);
    }
}
