using Escalated.Events;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Escalated services in the DI container so the
    /// library's controllers (added via AddApplicationPart) can resolve
    /// their constructor dependencies.
    /// </summary>
    public static IServiceCollection AddEscalated(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.TryAddSingletonEvent();
        services.AddScoped<AdvancedReportingService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AutomationRunner>();
        services.AddScoped<BusinessHoursCalculator>();
        services.AddScoped<CapacityService>();
        services.AddScoped<ChatAvailabilityService>();
        services.AddScoped<ChatRoutingService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<EscalationService>();
        services.AddScoped<ImportService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<MacroService>();
        services.AddScoped<MentionService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<SideConversationService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<SlaService>();
        services.AddScoped<TicketMergeService>();
        services.AddScoped<TicketService>();
        services.AddScoped<TicketSnoozeService>();
        services.AddScoped<TicketSplitService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<WebhookDispatcher>();
        services.AddScoped<WorkflowEngine>();
        return services;
    }

    private static void TryAddSingletonEvent(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IEscalatedEventDispatcher)))
        {
            services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();
        }
    }
}
