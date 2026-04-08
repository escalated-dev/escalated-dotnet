using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Background service that periodically ends chat sessions which have been
/// idle (no activity) for longer than the configured timeout.
/// </summary>
public class ChatCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatCleanupBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(30);

    public ChatCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ChatCleanupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupIdleSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up idle chat sessions.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CleanupIdleSessionsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var chatService = scope.ServiceProvider.GetRequiredService<ChatSessionService>();

        var cutoff = DateTime.UtcNow - _idleTimeout;

        var idleSessions = await db.ChatSessions
            .Where(s => (s.Status == "waiting" || s.Status == "active") && s.LastActivityAt <= cutoff)
            .ToListAsync(ct);

        foreach (var session in idleSessions)
        {
            try
            {
                await chatService.EndAsync(session.Id, causerId: null, ct);
                _logger.LogInformation("Ended idle chat session {SessionId} (ticket {TicketId}).",
                    session.Id, session.TicketId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to end idle chat session {SessionId}.", session.Id);
            }
        }
    }
}
