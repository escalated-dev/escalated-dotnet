using Escalated.Configuration;

namespace Escalated.Tests.Configuration;

public class EscalatedOptionsTests
{
    [Fact]
    public void TicketReferencePrefixRejectsHyphens()
    {
        var options = new EscalatedOptions();

        Assert.Throws<ArgumentException>(() => options.TicketReferencePrefix = "SUP-PORT");
    }

    [Fact]
    public void TicketReferencePrefixAllowsPrefixesWithoutHyphens()
    {
        var options = new EscalatedOptions
        {
            TicketReferencePrefix = "SUPPORT"
        };

        Assert.Equal("SUPPORT", options.TicketReferencePrefix);
    }
}
