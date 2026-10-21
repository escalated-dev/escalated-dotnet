using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Events;
using Escalated.Models;
using Escalated.Notifications;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Default outbound notification emails, through the real HTTP pipeline and
/// <c>AddEscalated()</c>. Recipients follow escalated-laravel's notification
/// listeners; nobody is emailed about their own action.
/// </summary>
public class NotificationEmailTests
{
    private const string Agent = "agent-5";
    private const string OtherAgent = "agent-6";
    private const string Customer = "customer-7";
    private const string Follower = "follower-8";
    private const string Domain = "help.example.com";

    private sealed class RecordingTransport : IEscalatedMailTransport
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();
        public bool IsConfigured => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public List<string> To => Sent.Select(m => m.ToEmail).ToList();
    }

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, string> Emails = new()
        {
            [Agent] = "agent5@example.com",
            [OtherAgent] = "agent6@example.com",
            [Customer] = "customer7@example.com",
            [Follower] = "follower8@example.com",
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Emails.TryGetValue(id, out var email) ? new UserDirectoryEntry(id, id, email) : null);
    }

    private static async Task<(EscalatedTestHost Host, RecordingTransport Mail)> StartAsync(string inboundSecret = "")
    {
        var mail = new RecordingTransport();
        var host = await EscalatedTestHost.StartAsync(
            o =>
            {
                o.Email.Domain = Domain;
                o.Email.InboundSecret = inboundSecret;
            },
            services =>
            {
                services.AddSingleton<IEscalatedMailTransport>(mail);
                services.AddSingleton<IUserDirectory, Directory>();
            });
        await host.GrantRoleAsync(Agent, "escalated-agent");
        await host.GrantRoleAsync(OtherAgent, "escalated-agent");
        return (host, mail);
    }

    private static async Task<int> CustomerTicketAsync(EscalatedTestHost host, string? assignee = null, params string[] followers)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket
            {
                Reference = $"ESC-{Guid.NewGuid():N}"[..12],
                Subject = "Printer on fire",
                Description = "Help",
                RequesterId = Customer,
                AssignedTo = assignee,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            foreach (var follower in followers) db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = follower });
            await db.SaveChangesAsync();
            id = ticket.Id;
        });
        return id;
    }

    [Fact]
    public async Task GuestTicketFromTheWidget_EmailsTheGuestTheThreadAnchor()
    {
        var (host, mail) = await StartAsync(inboundSecret: "s3cret");
        await using var _ = host;

        var response = await host.Client.PostAsJsonAsync("/support/widget/tickets",
            new { subject = "Where is my order?", description = "It is late", name = "Gail", email = "gail@example.com" });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var message = Assert.Single(mail.Sent);
        Assert.Equal("gail@example.com", message.ToEmail);
        Assert.Equal($"<ticket-{id}@{Domain}>", message.MessageId);
        Assert.StartsWith($"reply+{id}.", message.ReplyTo);
        Assert.Contains("Where is my order?", message.Subject);
    }

    [Fact]
    public async Task AgentReply_EmailsTheRequesterAndFollowers_NotTheAuthor()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host, Agent, Follower, Agent);

        var response = await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/reply", new { body = "On it" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(new[] { "customer7@example.com", "follower8@example.com" }, mail.To.OrderBy(x => x));
        var toCustomer = mail.Sent.First(m => m.ToEmail == "customer7@example.com");
        Assert.Equal($"<ticket-{id}@{Domain}>", toCustomer.InReplyTo);
        Assert.Equal($"<ticket-{id}@{Domain}>", toCustomer.References);
        Assert.Matches($"^<ticket-{id}-reply-\\d+@{Domain.Replace(".", "\\.")}>$", toCustomer.MessageId);
    }

    [Fact]
    public async Task CustomerReply_EmailsTheAssignee_NotTheCustomer()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host, Agent);

        var response = await host.ClientFor(Customer).PostAsJsonAsync($"/support/tickets/{id}/reply", new { body = "Any news?" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(new[] { "agent5@example.com" }, mail.To);
    }

    [Fact]
    public async Task InternalNote_EmailsNobody()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host, Agent, Follower);

        var response = await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/note", new { body = "Customer is grumpy" });
        response.EnsureSuccessStatusCode();

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Assignment_EmailsTheAgent_UnlessTheyAssignedThemselves()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host);

        (await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/assign", new { agentId = Agent })).EnsureSuccessStatusCode();
        Assert.Empty(mail.Sent);

        (await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/assign", new { agentId = OtherAgent })).EnsureSuccessStatusCode();
        var message = Assert.Single(mail.Sent);
        Assert.Equal("agent6@example.com", message.ToEmail);
        Assert.Equal($"<ticket-{id}@{Domain}>", message.InReplyTo);
    }

    [Fact]
    public async Task StatusChange_EmailsTheRequesterAndFollowers_NotWhoeverChangedIt()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host, Agent, Follower, Agent);

        (await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/status", new { status = "resolved" })).EnsureSuccessStatusCode();

        Assert.Equal(new[] { "customer7@example.com", "follower8@example.com" }, mail.To.OrderBy(x => x));
        Assert.All(mail.Sent, m => Assert.Contains("resolved", m.Subject));
    }

    [Fact]
    public async Task SlaBreachAndEscalation_EmailTheAssignee()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;
        var id = await CustomerTicketAsync(host, Agent);

        using (var scope = host.Services.CreateScope())
        {
            var ticket = await scope.ServiceProvider.GetRequiredService<TicketService>().FindByIdAsync(id);
            var bus = scope.ServiceProvider.GetRequiredService<IEscalatedEventDispatcher>();
            await bus.DispatchAsync(new SlaBreachedEvent(ticket!, "resolution"));
            await bus.DispatchAsync(new TicketEscalatedEvent(ticket!, null));
        }

        Assert.Equal(new[] { "agent5@example.com", "agent5@example.com" }, mail.To);
        Assert.Contains(mail.Sent, m => m.Subject.StartsWith("SLA Breach"));
        Assert.Contains(mail.Sent, m => m.Subject.StartsWith("Escalated"));
    }

    [Fact]
    public async Task ChatMessages_EmailNobody()
    {
        var (host, mail) = await StartAsync();
        await using var _ = host;

        using (var scope = host.Services.CreateScope())
        {
            var chats = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            var session = await chats.StartAsync("Visitor", "visitor@example.com", "Hello?");
            await chats.SendMessageAsync(session.Id, "Hi there", Agent, "agent");
        }

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task AHostSender_ReceivesTheCallsInstead()
    {
        var host = await EscalatedTestHost.StartAsync(configureServices: services =>
            services.AddSingleton<IEscalatedNotificationSender, RecordingSender>());
        await using var _ = host;

        await host.Client.PostAsJsonAsync("/support/widget/tickets",
            new { subject = "Hello", description = "x", name = "Gail", email = "gail@example.com" });

        var sender = (RecordingSender)host.Services.GetRequiredService<IEscalatedNotificationSender>();
        Assert.Equal(new[] { "new-ticket" }, sender.Calls);
    }

    private sealed class RecordingSender : IEscalatedNotificationSender
    {
        public List<string> Calls { get; } = new();

        public Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct = default) { Calls.Add("new-ticket"); return Task.CompletedTask; }
        public Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct = default) { Calls.Add("reply"); return Task.CompletedTask; }
        public Task SendMentionNotificationAsync(Reply reply, Ticket ticket, string mentionedUserId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendSlaBreachNotificationAsync(Ticket ticket, string breachType, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendEscalationNotificationAsync(Ticket ticket, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendResolvedNotificationAsync(Ticket ticket, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Messages);
        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    [Fact]
    public async Task WithoutSmtp_NothingIsSent_AndThatIsLoggedOnce()
    {
        var logs = new LogCapture();
        var host = await EscalatedTestHost.StartAsync(configureServices: services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddSingleton<IUserDirectory, Directory>();
        });
        await using var _ = host;

        for (var i = 0; i < 2; i++)
        {
            var response = await host.Client.PostAsJsonAsync("/support/widget/tickets",
                new { subject = $"Hello {i}", description = "x", name = "Gail", email = "gail@example.com" });
            response.EnsureSuccessStatusCode();
        }

        Assert.Single(logs.Messages, m => m.Contains("notification emails are off"));
    }

    /// <summary>
    /// The default transport against a minimal SMTP server: the threading headers
    /// and the signed Reply-To survive <see cref="System.Net.Mail.SmtpClient"/>.
    /// </summary>
    [Fact]
    public async Task SmtpTransport_SendsWithTheThreadingHeaders()
    {
        using var server = new FakeSmtpServer();
        var host = await EscalatedTestHost.StartAsync(
            o =>
            {
                o.Email.Domain = Domain;
                o.Email.InboundSecret = "s3cret";
                o.Email.FromAddress = "support@help.example.com";
                o.Email.Smtp.Host = "127.0.0.1";
                o.Email.Smtp.Port = server.Port;
                o.Email.Smtp.EnableSsl = false;
            },
            services => services.AddSingleton<IUserDirectory, Directory>());
        await using var _ = host;
        await host.GrantRoleAsync(Agent, "escalated-agent");
        var id = await CustomerTicketAsync(host, Agent);

        (await host.ClientFor(Agent).PostAsJsonAsync($"/support/agent/tickets/{id}/reply", new { body = "On it" })).EnsureSuccessStatusCode();

        var data = await server.NextMessageAsync();
        Assert.Contains("RCPT TO:<customer7@example.com>", data.Envelope);
        Assert.Contains($"In-Reply-To: <ticket-{id}@{Domain}>", data.Data);
        Assert.Contains($"References: <ticket-{id}@{Domain}>", data.Data);
        Assert.Matches($"Message-ID: <ticket-{id}-reply-\\d+@", data.Data);
        Assert.Contains($"Reply-To: reply+{id}.", data.Data);
        Assert.Contains("support@help.example.com", data.Data);
    }

    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task<(string Envelope, string Data)> received;

        public FakeSmtpServer()
        {
            listener.Start();
            received = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public async Task<(string Envelope, string Data)> NextMessageAsync() =>
            await received.WaitAsync(TimeSpan.FromSeconds(30));

        private async Task<(string, string)> AcceptAsync()
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var envelope = new StringBuilder();
            var data = new StringBuilder();

            await writer.WriteLineAsync("220 localhost ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go ahead");
                        while (await reader.ReadLineAsync() is { } body && body != ".") data.AppendLine(body);
                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return (envelope.ToString(), data.ToString());
                    default:
                        envelope.AppendLine(line);
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }

            return (envelope.ToString(), data.ToString());
        }

        public void Dispose() => listener.Stop();
    }
}
