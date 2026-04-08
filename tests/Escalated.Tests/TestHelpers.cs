using Escalated.Configuration;
using Escalated.Data;
using Escalated.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Escalated.Tests;

public static class TestHelpers
{
    public static EscalatedDbContext CreateInMemoryDb(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new EscalatedDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static IOptions<EscalatedOptions> DefaultOptions()
    {
        return Options.Create(new EscalatedOptions());
    }

    public static Mock<IEscalatedEventDispatcher> MockEventDispatcher()
    {
        var mock = new Mock<IEscalatedEventDispatcher>();
        mock.Setup(x => x.DispatchAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
