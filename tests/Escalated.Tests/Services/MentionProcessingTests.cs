using Escalated.Models;
using Escalated.Notifications;
using Escalated.Services;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Covers the wired @-mention pipeline: an internal note resolves handles to
/// host agents, persists a <see cref="Mention"/> row and notifies the agent;
/// unknown handles are ignored; and public replies never mention. Mirrors the
/// Laravel reference <c>MentionServiceTest</c> + <c>LocalDriver::addReply</c>.
/// </summary>
public class MentionProcessingTests
{
    private static readonly UserDirectoryEntry Grace = new("7", "grace", "grace@support.test");
    private static readonly UserDirectoryEntry Hopper = new("9", "Grace Hopper", "ghopper@support.test");

    private sealed class FakeUserDirectory : IUserDirectory
    {
        private readonly List<UserDirectoryEntry> _entries;

        public FakeUserDirectory(params UserDirectoryEntry[] entries) => _entries = entries.ToList();

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        {
            IEnumerable<UserDirectoryEntry> q = _entries;
            if (!string.IsNullOrEmpty(search))
            {
                q = q.Where(e =>
                    (e.Name != null && e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (e.Email != null && e.Email.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            var items = q.Take(pageSize).ToList();
            return Task.FromResult(new UserDirectoryPage(items, items.Count, page, pageSize));
        }

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
    }

    private static (TicketService tickets, MentionService mentions, Mock<IEscalatedNotificationSender> notifier,
        Escalated.Data.EscalatedDbContext db) Build(params UserDirectoryEntry[] agents)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var notifier = new Mock<IEscalatedNotificationSender>();
        var mentions = new MentionService(db, new FakeUserDirectory(agents), notifier.Object);
        var tickets = new TicketService(db, events.Object, TestHelpers.DefaultOptions(), mentions);
        return (tickets, mentions, notifier, db);
    }

    [Fact]
    public async Task InternalNote_PersistsResolvedMention_AndNotifies()
    {
        var (tickets, _, notifier, db) = Build(Grace);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        var note = await tickets.AddReplyAsync(ticket, "Hey @grace can you review?", authorId: "1", isNote: true);

        var mention = Assert.Single(db.Mentions);
        Assert.Equal("7", mention.UserId);
        Assert.Equal(note.Id, mention.ReplyId);
        Assert.Null(mention.ReadAt);
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), "7", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InternalNote_ResolvesByEmailLocalPart()
    {
        var (tickets, _, notifier, db) = Build(Hopper);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        await tickets.AddReplyAsync(ticket, "cc @ghopper", authorId: "1", isNote: true);

        var mention = Assert.Single(db.Mentions);
        Assert.Equal("9", mention.UserId);
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), "9", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InternalNote_UnknownHandle_IsIgnored()
    {
        var (tickets, _, notifier, db) = Build(Grace);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        await tickets.AddReplyAsync(ticket, "Hey @nobody please look", authorId: "1", isNote: true);

        Assert.Empty(db.Mentions);
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublicReply_DoesNotMention()
    {
        var (tickets, _, notifier, db) = Build(Grace);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        await tickets.AddReplyAsync(ticket, "Hey @grace", authorId: "1", isNote: false);

        Assert.Empty(db.Mentions);
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InternalNote_DoesNotMentionTheAuthor()
    {
        var (tickets, _, notifier, db) = Build(Grace);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        // Author (id "7") mentions the handle that resolves to their own id.
        await tickets.AddReplyAsync(ticket, "note to self @grace", authorId: "7", isNote: true);

        Assert.Empty(db.Mentions);
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InternalNote_MultipleDistinctAgents_AllMentioned()
    {
        var (tickets, _, notifier, db) = Build(Grace, Hopper);
        var ticket = await tickets.CreateAsync("Subject", "desc");

        await tickets.AddReplyAsync(ticket, "cc @grace and @ghopper", authorId: "1", isNote: true);

        Assert.Equal(2, db.Mentions.Count());
        notifier.Verify(n => n.SendMentionNotificationAsync(
            It.IsAny<Reply>(), It.IsAny<Ticket>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessMentions_IsIdempotent_ForTheSameReply()
    {
        var (tickets, mentions, _, db) = Build(Grace);
        var ticket = await tickets.CreateAsync("Subject", "desc");
        var note = await tickets.AddReplyAsync(ticket, "Hey @grace", authorId: "1", isNote: true);

        // Re-processing the same reply must not create a duplicate row.
        await mentions.ProcessMentionsAsync(note, ticket);

        Assert.Single(db.Mentions);
    }

    [Fact]
    public async Task ResolveMentionsAsync_MatchesKnownHandles_IgnoresUnknown()
    {
        var (_, mentions, _, _) = Build(Grace, Hopper);

        var ids = await mentions.ResolveMentionsAsync("hi @grace @ghopper @stranger");

        Assert.Equal(new[] { "7", "9" }, ids.OrderBy(x => x).ToArray());
    }
}
