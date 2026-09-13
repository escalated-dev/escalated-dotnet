using System.Collections.Concurrent;
using System.Net;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A failed webhook delivery is retried after a backoff (2 minutes, then 4), on its
/// own, long after the request that sent it has returned.
///
/// <para>The retry was started fire-and-forget on the <see cref="WebhookDispatcher"/>
/// that sent the first attempt, and that dispatcher's <c>EscalatedDbContext</c>
/// belongs to the request's scope (or to the scope <c>WebhookEventDispatcher</c>
/// creates per event). By the time the backoff ran out the scope was gone, the
/// retry's first write threw <see cref="ObjectDisposedException"/>, and nothing
/// observed the task: no second attempt, no delivery row, no log.</para>
///
/// <para>These tests dispatch inside a scope and dispose it, as a request does, and
/// release the backoff through a <see cref="TimeProvider"/> instead of waiting two
/// minutes. They wait on in-memory signals rather than polling the database, which
/// the retry would be writing to at the same time.</para>
/// </summary>
public class WebhookRetryTests
{
    private const string EventName = "ticket.created";

    [Fact]
    public async Task FailedDelivery_RecordsItsSecondAttempt_AfterTheRequestHasEnded()
    {
        var receiver = new Receiver(HttpStatusCode.InternalServerError);
        var time = new ManualTimeProvider();
        await using var host = await StartAsync(receiver, time, new LogCapture());
        await SeedWebhookAsync(host);

        await DispatchInARequestScopeAsync(host);

        // The request returned without waiting for the retry.
        Assert.Equal(new[] { 1 }, await AttemptsAsync(host));
        Assert.Equal(new[] { TimeSpan.FromMinutes(2) }, time.PendingDelays);

        time.FirePending();

        // The second attempt failed too, so it schedules the third: it ran to the end.
        await WaitUntilAsync(() => time.PendingDelays.SequenceEqual(new[] { TimeSpan.FromMinutes(4) }),
            "the second attempt to finish and schedule the third");
        Assert.Equal(new[] { 1, 2 }, await AttemptsAsync(host));
        Assert.Equal(2, receiver.Requests);
    }

    [Fact]
    public async Task Retry_IsDropped_WhenTheWebhookWasDeactivatedDuringTheBackoff()
    {
        var receiver = new Receiver(HttpStatusCode.InternalServerError);
        var time = new ManualTimeProvider();
        var logs = new LogCapture();
        await using var host = await StartAsync(receiver, time, logs);
        var webhookId = await SeedWebhookAsync(host);

        await DispatchInARequestScopeAsync(host);
        await host.SeedAsync(async db =>
        {
            var webhook = await db.Webhooks.SingleAsync(w => w.Id == webhookId);
            webhook.Active = false;
            await db.SaveChangesAsync();
        });

        time.FirePending();

        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("no longer active")), "the retry to be dropped");
        Assert.Equal(new[] { 1 }, await AttemptsAsync(host));
        Assert.Equal(1, receiver.Requests);
        Assert.Empty(time.PendingDelays);
    }

    private static Task<EscalatedTestHost> StartAsync(Receiver receiver, ManualTimeProvider time, LogCapture logs) =>
        EscalatedTestHost.StartAsync(configureServices: services =>
        {
            services.AddSingleton<IHttpClientFactory>(new ReceiverClientFactory(receiver));
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<ILoggerProvider>(logs);
        });

    private static async Task<int> SeedWebhookAsync(EscalatedTestHost host)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var webhook = new Webhook { Url = "https://example.com/hook", Events = $"[\"{EventName}\"]", Active = true };
            db.Webhooks.Add(webhook);
            await db.SaveChangesAsync();
            id = webhook.Id;
        });

        return id;
    }

    /// <summary>A request's scope, and the DbContext in it, are disposed when the request ends.</summary>
    private static async Task DispatchInARequestScopeAsync(EscalatedTestHost host)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>()
            .DispatchAsync(EventName, new { ticket = new { id = 7 } });
    }

    private static Task<List<int>> AttemptsAsync(EscalatedTestHost host) =>
        host.QueryAsync(db => db.WebhookDeliveries.OrderBy(d => d.Id).Select(d => d.Attempts).ToListAsync());

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {what}.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class Receiver : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private int requests;

        public Receiver(HttpStatusCode status) => this.status = status;

        public int Requests => requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class ReceiverClientFactory : IHttpClientFactory
    {
        private readonly Receiver receiver;

        public ReceiverClientFactory(Receiver receiver) => this.receiver = receiver;

        // A client per call: the dispatcher sets Timeout, which a client refuses once it has sent.
        public HttpClient CreateClient(string name) => new(receiver, disposeHandler: false);
    }

    /// <summary>Timers that fire only when the test says so.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = new();

        public IReadOnlyList<TimeSpan> PendingDelays
        {
            get
            {
                lock (gate)
                {
                    return timers.Where(t => t.IsPending).Select(t => t.DueTime).ToList();
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public void FirePending()
        {
            List<ManualTimer> pending;
            lock (gate)
            {
                pending = timers.Where(t => t.IsPending).ToList();
            }

            foreach (var timer in pending)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback callback;
            private readonly object? state;
            private volatile bool done;

            public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
            {
                this.callback = callback;
                this.state = state;
                DueTime = dueTime;
            }

            public TimeSpan DueTime { get; private set; }

            public bool IsPending => !done && DueTime != Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTime = dueTime;
                return !done;
            }

            public void Fire()
            {
                done = true;
                callback(state);
            }

            public void Dispose() => done = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger : ILogger
        {
            private readonly ConcurrentQueue<string> messages;

            public Logger(ConcurrentQueue<string> messages) => this.messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}
