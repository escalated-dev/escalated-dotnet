using Escalated.Events;
using Escalated.Localization;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

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
        services.AddEscalatedLocalization();
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

        // Inbound email: router + default Postmark parser.
        // Host apps can add more parsers by registering them as
        // IInboundEmailParser; the controller dispatches by Name.
        services.AddScoped<Services.Email.Inbound.InboundEmailRouter>(sp =>
            new Services.Email.Inbound.InboundEmailRouter(
                sp.GetRequiredService<Data.EscalatedDbContext>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.EscalatedOptions>>().Value));
        services.AddScoped<Services.Email.Inbound.IInboundEmailParser,
            Services.Email.Inbound.PostmarkInboundParser>();
        services.AddScoped<Services.Email.Inbound.IInboundEmailParser,
            Services.Email.Inbound.MailgunInboundParser>();
        services.AddScoped<Services.Email.Inbound.InboundEmailService>();
        return services;
    }

    private static void TryAddSingletonEvent(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IEscalatedEventDispatcher)))
        {
            services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();
        }
    }

    /// <summary>
    /// Registers a chained <see cref="IStringLocalizer"/> stack that
    /// resolves strings from plugin-local overrides under
    /// <c>Resources/Overrides/</c> first, falling through to the
    /// central <c>Escalated.Locale</c> NuGet catalog. This is the
    /// single seam every Escalated host plugin uses to consume shared
    /// translations, so adding a new locale upstream lights it up
    /// here without a code change.
    /// </summary>
    private static void AddEscalatedLocalization(this IServiceCollection services)
    {
        // Standard ASP.NET Core localization (resx + JSON readers).
        services.AddLocalization(opts => opts.ResourcesPath = "Resources/Overrides");

        // Decorate the default factory: chain plugin-local first,
        // central Escalated.Locale second.
        services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory>(sp =>
        {
            var inner = ActivatorUtilities.CreateInstance<ResourceManagerStringLocalizerFactory>(sp);
            return new EscalatedLocalizerFactory(inner);
        }));
    }
}
