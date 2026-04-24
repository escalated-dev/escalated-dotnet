using Escalated.Models;
using Xunit;

namespace Escalated.Tests.Models;

public class ContactModelTests
{
    // ---------------------------------------------------------------------
    // NormalizeEmail
    // ---------------------------------------------------------------------

    [Fact]
    public void NormalizeEmail_Lowercases()
    {
        Assert.Equal("alice@example.com", Contact.NormalizeEmail("ALICE@Example.COM"));
    }

    [Fact]
    public void NormalizeEmail_TrimsWhitespace()
    {
        Assert.Equal("alice@example.com", Contact.NormalizeEmail("  alice@example.com  "));
    }

    [Fact]
    public void NormalizeEmail_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, Contact.NormalizeEmail(null));
        Assert.Equal(string.Empty, Contact.NormalizeEmail(""));
    }

    // ---------------------------------------------------------------------
    // DecideAction
    // ---------------------------------------------------------------------

    [Fact]
    public void DecideAction_CreateWhenNoExisting()
    {
        Assert.Equal("create", Contact.DecideAction(null, "Alice"));
    }

    [Fact]
    public void DecideAction_ReturnExistingWhenExistingHasName()
    {
        var existing = new Contact { Email = "alice@example.com", Name = "Alice" };
        Assert.Equal("return-existing", Contact.DecideAction(existing, "Different"));
    }

    [Fact]
    public void DecideAction_UpdateNameWhenExistingNameIsBlank()
    {
        var existing = new Contact { Email = "alice@example.com", Name = null };
        Assert.Equal("update-name", Contact.DecideAction(existing, "Alice"));

        existing.Name = "";
        Assert.Equal("update-name", Contact.DecideAction(existing, "Alice"));
    }

    [Fact]
    public void DecideAction_ReturnExistingWhenNoIncomingName()
    {
        var existing = new Contact { Email = "alice@example.com", Name = null };
        Assert.Equal("return-existing", Contact.DecideAction(existing, null));
        Assert.Equal("return-existing", Contact.DecideAction(existing, ""));
    }

    // ---------------------------------------------------------------------
    // Metadata JSON round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public void Metadata_RoundTripsDictionary()
    {
        var c = new Contact { Email = "alice@example.com" };
        c.SetMetadata(new Dictionary<string, object?> { ["source"] = "widget", ["count"] = 3 });

        var recovered = c.GetMetadata();
        Assert.Equal("widget", recovered["source"]?.ToString());
    }

    [Fact]
    public void GetMetadata_ReturnsEmptyDictForBlankString()
    {
        var c = new Contact { Email = "alice@example.com", Metadata = "" };
        Assert.Empty(c.GetMetadata());
    }

    // ---------------------------------------------------------------------
    // Defaults
    // ---------------------------------------------------------------------

    [Fact]
    public void Defaults_EmptyEmailAndEmptyMetadataObject()
    {
        var c = new Contact();
        Assert.Equal(string.Empty, c.Email);
        Assert.Equal("{}", c.Metadata);
        Assert.Null(c.Name);
        Assert.Null(c.UserId);
    }
}
