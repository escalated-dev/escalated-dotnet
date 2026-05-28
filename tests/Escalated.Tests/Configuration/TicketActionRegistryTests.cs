using Escalated.Configuration;
using Escalated.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Configuration;

public class TicketActionRegistryTests
{
    private static TicketActionRegistry Build(params TicketActionConfig[] actions) =>
        new(Options.Create(new EscalatedOptions { TicketActions = actions.ToList() }));

    [Fact]
    public void ForTicket_SerializesVisibleActionsWithDefaults()
    {
        var registry = Build(new TicketActionConfig { Key = "sync-crm", Label = "Sync CRM" });

        var actions = registry.ForTicket(new Ticket { Id = 1 }, null);

        Assert.Single(actions);
        Assert.Equal("sync-crm", actions[0]["key"]);
        Assert.Equal("Sync CRM", actions[0]["label"]);
        Assert.Equal("secondary", actions[0]["variant"]);
        Assert.Equal(false, actions[0]["disabled"]);
        Assert.Null(actions[0]["confirmation"]);
    }

    [Fact]
    public void ForTicket_OmitsInvisibleActionsAndMarksDisabled()
    {
        var registry = Build(
            new TicketActionConfig { Key = "hidden", Label = "Hidden", Visible = false },
            new TicketActionConfig { Key = "locked", Label = "Locked", Enabled = false });

        var actions = registry.ForTicket(new Ticket { Id = 1 }, null);

        Assert.Single(actions);
        Assert.Equal("locked", actions[0]["key"]);
        Assert.Equal(true, actions[0]["disabled"]);
    }

    [Fact]
    public void Find_ReturnsConfigOrNull()
    {
        var registry = Build(new TicketActionConfig { Key = "a", Label = "A" });

        Assert.NotNull(registry.Find("a"));
        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void SkipsActionsMissingKeyOrLabel()
    {
        var registry = Build(
            new TicketActionConfig { Key = "", Label = "no key" },
            new TicketActionConfig { Key = "ok", Label = "Ok" });

        Assert.Null(registry.Find(""));
        Assert.NotNull(registry.Find("ok"));
    }
}
