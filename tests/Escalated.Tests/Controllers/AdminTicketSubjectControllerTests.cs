using Escalated.Configuration;
using Escalated.Contracts;
using Escalated.Controllers.Admin;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Controllers;

public class AdminTicketSubjectControllerTests
{
    private const string ProjectType = "App.Models.Project";

    private sealed class FakeProjectSubject : ITicketSubject
    {
        public FakeProjectSubject(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public string TicketSubjectTitle() => Name;
        public string? TicketSubjectSubtitle() => null;
        public string? TicketSubjectUrl() => null;
        public string? TicketSubjectColor() => null;
        public string? TicketSubjectIcon() => null;
    }

    private sealed class StubResolver : ITicketSubjectResolver
    {
        private readonly Dictionary<(string, string), ITicketSubject> _map = new();

        public void Register(string type, string id, ITicketSubject subject) => _map[(type, id)] = subject;

        public Task<ITicketSubject?> ResolveAsync(string subjectType, string subjectId, CancellationToken ct = default)
            => Task.FromResult(_map.TryGetValue((subjectType, subjectId), out var s) ? s : null);
    }

    private static async Task<(AdminTicketSubjectController Ctrl, EscalatedDbContext Db, Ticket Ticket)> SeedAsync(
        StubResolver resolver)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, Options.Create(new EscalatedOptions()));
        var subjectService = new TicketSubjectService(
            db,
            resolver,
            Options.Create(new EscalatedOptions { TicketSubjects = { Types = [ProjectType] } }));
        var ctrl = new AdminTicketSubjectController(ticketService, subjectService, resolver);

        var ticket = new Ticket
        {
            Reference = "ESC-00001",
            Subject = "Help",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return (ctrl, db, ticket);
    }

    [Fact]
    public async Task Attach_ReturnsLinkForAllowlistedType()
    {
        var resolver = new StubResolver();
        resolver.Register(ProjectType, "7", new FakeProjectSubject("7", "Acme"));
        var (ctrl, _, ticket) = await SeedAsync(resolver);

        var result = await ctrl.Attach(ticket.Id, new AttachSubjectRequest(ProjectType, "7", "project"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var link = Assert.IsType<TicketSubjectLink>(ok.Value);
        Assert.Equal("7", link.SubjectId);
        Assert.Equal("project", link.Role);
    }

    [Fact]
    public async Task Attach_RejectsUnknownSubjectId()
    {
        var resolver = new StubResolver();
        var (ctrl, _, ticket) = await SeedAsync(resolver);

        var result = await ctrl.Attach(ticket.Id, new AttachSubjectRequest(ProjectType, "missing"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Detach_RemovesLink()
    {
        var resolver = new StubResolver();
        resolver.Register(ProjectType, "1", new FakeProjectSubject("1", "A"));
        var (ctrl, db, ticket) = await SeedAsync(resolver);

        var attach = await ctrl.Attach(ticket.Id, new AttachSubjectRequest(ProjectType, "1"), CancellationToken.None);
        var link = Assert.IsType<TicketSubjectLink>(Assert.IsType<OkObjectResult>(attach).Value);

        var result = await ctrl.Detach(ticket.Id, link.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(db.TicketSubjectLinks);
    }
}
