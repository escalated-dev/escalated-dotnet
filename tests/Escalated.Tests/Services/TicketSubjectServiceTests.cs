using Escalated.Configuration;
using Escalated.Contracts;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Services;

public class TicketSubjectServiceTests
{
    private const string ProjectType = "App.Models.Project";

    private sealed class FakeProjectSubject : ITicketSubject
    {
        public FakeProjectSubject(string id, string name, string? account = null)
        {
            Id = id;
            Name = name;
            Account = account;
        }

        public string Id { get; }
        public string Name { get; }
        public string? Account { get; }

        public string TicketSubjectTitle() => Name;

        public string? TicketSubjectSubtitle() => Account == null ? null : $"Project · {Account}";

        public string? TicketSubjectUrl() => $"https://app.test/projects/{Id}";

        public string? TicketSubjectColor() => "#2563eb";

        public string? TicketSubjectIcon() => "folder";
    }

    private sealed class StubResolver : ITicketSubjectResolver
    {
        private readonly Dictionary<(string Type, string Id), ITicketSubject> _subjects = new();

        public void Register(string type, ITicketSubject subject)
            => _subjects[(type, subject.TicketSubjectTitle() is var _ ? GetId(subject) : "")] = subject;

        public void Register(string type, string id, ITicketSubject subject)
            => _subjects[(type, id)] = subject;

        public Task<ITicketSubject?> ResolveAsync(string subjectType, string subjectId, CancellationToken ct = default)
            => Task.FromResult(_subjects.TryGetValue((subjectType, subjectId), out var s) ? s : null);

        private static string GetId(ITicketSubject subject)
            => subject is FakeProjectSubject p ? p.Id : string.Empty;
    }

    private static (TicketSubjectService Service, EscalatedDbContext Db, StubResolver Resolver) Create(
        Action<EscalatedOptions>? configure = null)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var resolver = new StubResolver();
        var options = new EscalatedOptions();
        configure?.Invoke(options);
        var service = new TicketSubjectService(db, resolver, Options.Create(options));
        return (service, db, resolver);
    }

    private static async Task<Ticket> SeedTicketAsync(EscalatedDbContext db)
    {
        var ticket = new Ticket
        {
            Reference = "ESC-00001",
            Subject = "Help",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task AttachAsync_PreservesStringSubjectId()
    {
        var (service, db, _) = Create(o => o.TicketSubjects.Types = [ProjectType]);
        var ticket = await SeedTicketAsync(db);

        var link = await service.AttachAsync(ticket, ProjectType, "prj_9f1c", role: "project");

        Assert.Equal(ProjectType, link.SubjectType);
        Assert.Equal("prj_9f1c", link.SubjectId);
        Assert.Equal("project", link.Role);
        Assert.Equal(1, await db.TicketSubjectLinks.CountAsync());
    }

    [Fact]
    public async Task AttachAsync_IsIdempotentAndUpdatesRole()
    {
        var (service, db, _) = Create();
        var ticket = await SeedTicketAsync(db);

        await service.AttachAsync(ticket, ProjectType, "p1");
        await service.AttachAsync(ticket, ProjectType, "p1", role: "account");

        var links = await service.ListAsync(ticket);
        Assert.Single(links);
        Assert.Equal("account", links[0].Role);
    }

    [Fact]
    public async Task SerializeAsync_UsesPresentationContract()
    {
        var (service, db, resolver) = Create();
        var ticket = await SeedTicketAsync(db);
        var project = new FakeProjectSubject("7", "Acme Redesign", "Acme");
        resolver.Register(ProjectType, "7", project);

        await service.AttachAsync(ticket, ProjectType, "7", role: "project");
        var payload = await service.SerializeAsync(await service.ListAsync(ticket));

        Assert.Single(payload);
        Assert.Equal(ProjectType, payload[0].Type);
        Assert.Equal("7", payload[0].Id);
        Assert.Equal("project", payload[0].Role);
        Assert.Equal("Acme Redesign", payload[0].Title);
        Assert.Equal("Project · Acme", payload[0].Subtitle);
        Assert.Equal("https://app.test/projects/7", payload[0].Url);
        Assert.Equal("#2563eb", payload[0].Color);
        Assert.Equal("folder", payload[0].Icon);
        Assert.False(payload[0].Missing);
    }

    [Fact]
    public async Task SerializeAsync_FallsBackWhenSubjectMissing()
    {
        var (service, db, _) = Create();
        var ticket = await SeedTicketAsync(db);
        await service.AttachAsync(ticket, ProjectType, "99");

        var payload = await service.SerializeAsync(await service.ListAsync(ticket));

        Assert.Single(payload);
        Assert.Equal("Project#99", payload[0].Title);
        Assert.True(payload[0].Missing);
    }

    [Fact]
    public async Task DetachAsync_RemovesLink()
    {
        var (service, db, _) = Create();
        var ticket = await SeedTicketAsync(db);
        await service.AttachAsync(ticket, ProjectType, "1");

        var removed = await service.DetachAsync(ticket, ProjectType, "1");

        Assert.Equal(1, removed);
        Assert.Empty(await service.ListAsync(ticket));
    }

    [Fact]
    public async Task SyncAsync_ReplacesSubjectsInOrder()
    {
        var (service, db, _) = Create();
        var ticket = await SeedTicketAsync(db);
        await service.AttachAsync(ticket, ProjectType, "a");

        await service.SyncAsync(ticket,
        [
            (ProjectType, "b", "primary"),
            (ProjectType, "c", null),
        ]);

        var links = await service.ListAsync(ticket);
        Assert.Equal(2, links.Count);
        Assert.Equal("b", links[0].SubjectId);
        Assert.Equal("primary", links[0].Role);
        Assert.Equal(0, links[0].Position);
        Assert.Equal("c", links[1].SubjectId);
        Assert.Equal(1, links[1].Position);
    }

    [Fact]
    public async Task AttachAsync_RejectsTypeOutsideAllowlistWhenConfigured()
    {
        var (service, db, _) = Create(o => o.TicketSubjects.Types = ["App.Models.Other"]);
        var ticket = await SeedTicketAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AttachAsync(ticket, ProjectType, "1"));
    }

    [Fact]
    public async Task AttachAsync_AllowsAnyTypeWhenAllowlistEmpty()
    {
        var (service, db, _) = Create(o => o.TicketSubjects.Types = []);
        var ticket = await SeedTicketAsync(db);

        var link = await service.AttachAsync(ticket, ProjectType, "1");

        Assert.NotNull(link);
    }

    [Fact]
    public async Task AttachAsync_ViaApiRequiresAllowlistedType()
    {
        var (service, db, _) = Create(o => o.TicketSubjects.Types = []);
        var ticket = await SeedTicketAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AttachAsync(ticket, ProjectType, "1", viaApi: true));
    }

    [Fact]
    public async Task DetachByLinkIdAsync_RemovesMatchingRow()
    {
        var (service, db, _) = Create();
        var ticket = await SeedTicketAsync(db);
        var link = await service.AttachAsync(ticket, ProjectType, "1");

        var removed = await service.DetachByLinkIdAsync(ticket, link.Id);

        Assert.Equal(1, removed);
    }
}
