using Escalated.Data;
using Escalated.Events;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Escalated.Configuration;

public static class EscalatedServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Escalated services and the EscalatedDbContext.
    /// </summary>
    public static IServiceCollection AddEscalated(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureDb = null)
    {
        // Bind options
        services.Configure<EscalatedOptions>(configuration.GetSection(EscalatedOptions.SectionName));

        // Register DbContext
        if (configureDb != null)
        {
            services.AddDbContext<EscalatedDbContext>(configureDb);
        }
        else
        {
            services.AddDbContext<EscalatedDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("Escalated")));
        }

        // Register event dispatcher (no-op default; host apps can override)
        services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();

        // Custom ticket action registry (host apps can override for dynamic
        // per-ticket/user visibility).
        services.TryAddSingleton<ITicketActionRegistry, TicketActionRegistry>();

        // Register user directory (empty default; host apps register their own
        // implementation to surface their user table in the admin users page).
        services.TryAddSingleton<IUserDirectory, NullUserDirectory>();

        services.TryAddSingleton<ITicketSubjectResolver, NullTicketSubjectResolver>();

        // Register services
        services.AddScoped<TicketService>();
        services.AddScoped<SlaService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<EscalationService>();
        services.AddScoped<MacroService>();
        services.AddScoped<TicketMergeService>();
        services.AddScoped<TicketSplitService>();
        services.AddScoped<TicketSnoozeService>();
        services.AddScoped<TicketSubjectService>();
        services.AddScoped<WebhookDispatcher>();
        services.AddScoped<AutomationRunner>();
        services.AddScoped<CapacityService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<BusinessHoursCalculator>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ImportService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<SideConversationService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<ChatRoutingService>();
        services.AddScoped<ChatAvailabilityService>();

        // Register the snooze background service
        services.AddHostedService<TicketSnoozeBackgroundService>();

        // Register the chat cleanup background service
        services.AddHostedService<ChatCleanupBackgroundService>();

        return services;
    }
}
