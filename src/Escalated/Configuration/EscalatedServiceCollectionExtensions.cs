using Escalated.Controllers.Newsletter;
using Escalated.Data;
using Escalated.Events;
using Escalated.Localization;
using Escalated.Notifications;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Escalated.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

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

        return services.AddEscalatedServices();
    }

    /// <summary>
    /// Every service Escalated's controllers, background services and event bridge
    /// depend on. Both <c>AddEscalated</c> overloads call this, so there is one list
    /// to keep complete instead of two to keep in step. They used to keep their own,
    /// and each ended up missing services the other registered.
    /// </summary>
    internal static IServiceCollection AddEscalatedServices(this IServiceCollection services)
    {
        // A host that calls both overloads must not get every background service twice.
        if (services.Any(d => d.ServiceType == typeof(EscalatedServicesMarker)))
        {
            return services;
        }

        services.AddSingleton<EscalatedServicesMarker>();

        // WebhookDispatcher sends through IHttpClientFactory.
        services.AddHttpClient();

        // Register the default event dispatcher. It bridges domain events to
        // the Workflow engine so configured Workflows fire on ticket/reply
        // events. Host apps can opt out (e.g. NullEventDispatcher) or supply
        // their own by registering an IEscalatedEventDispatcher first.
        services.TryAddSingleton<IEscalatedEventDispatcher, WorkflowEventDispatcher>();

        // Custom ticket action registry (host apps can override for dynamic
        // per-ticket/user visibility).
        services.TryAddSingleton<ITicketActionRegistry, TicketActionRegistry>();

        // Register user directory (empty default; host apps register their own
        // implementation to surface their user table in the admin users page).
        services.TryAddSingleton<IUserDirectory, NullUserDirectory>();

        services.TryAddSingleton<ITicketSubjectResolver, NullTicketSubjectResolver>();

        // No-op default so @-mention (and other) notifications resolve even
        // before the host wires up its own delivery. Hosts register their
        // own IEscalatedNotificationSender before/after AddEscalated.
        services.TryAddSingleton<IEscalatedNotificationSender, NullNotificationSender>();

        services.AddEscalatedLocalization();

        // Register services
        services.AddScoped<TicketService>();
        services.AddScoped<MentionService>();
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
        // Workflow engine: condition evaluator, action executor, and the
        // runner the event dispatcher drives.
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowExecutorService>();
        services.AddScoped<WorkflowRunnerService>();
        services.AddScoped<CapacityService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<BusinessHoursCalculator>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AdvancedReportingService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ImportService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<SideConversationService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<ChatRoutingService>();
        services.AddScoped<ChatAvailabilityService>();

        // Inbound email: router + default Postmark and Mailgun parsers.
        // Host apps can add more parsers by registering them as
        // IInboundEmailParser; the controller dispatches by Name.
        services.AddScoped<InboundEmailRouter>(sp =>
            new InboundEmailRouter(
                sp.GetRequiredService<EscalatedDbContext>(),
                sp.GetRequiredService<IOptions<EscalatedOptions>>().Value));
        services.AddScoped<IInboundEmailParser, PostmarkInboundParser>();
        services.AddScoped<IInboundEmailParser, MailgunInboundParser>();
        services.AddScoped<InboundEmailService>();

        services.AddScoped<BounceSuppressionStore>();
        services.AddScoped<ContactSegmentResolver>();
        services.AddScoped<NewsletterPlanner>();
        services.AddScoped<NewsletterDispatcher>();
        services.AddScoped<NewsletterTracker>();
        services.AddScoped<NewsletterPermissionService>();
        services.AddScoped<NewsletterPermissionSeeder>();
        services.AddScoped<NewsletterEnabledFilter>();
        services.TryAddSingleton<INewsletterClock, SystemNewsletterClock>();
        services.TryAddSingleton<INewsletterRateLimitStore, MemoryNewsletterRateLimitStore>();
        services.TryAddScoped<INewsletterEmailSender, NullNewsletterEmailSender>();
        services.AddScoped(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EscalatedOptions>>().Value;
            var newsletter = options.Newsletters;
            return new NewsletterRenderer(new NewsletterRendererOptions
            {
                BaseUrl = newsletter.BaseUrl,
                DefaultTheme = newsletter.DefaultTheme,
                TrackingEnabled = newsletter.TrackingEnabled,
                ThemesDir = newsletter.ThemesDir
                    ?? Path.Combine(AppContext.BaseDirectory, "Views", "NewsletterThemes"),
                Brand = new NewsletterBrand
                {
                    Name = "Support",
                    Accent = newsletter.BrandAccent,
                    LogoUrl = newsletter.BrandLogoUrl,
                    PhysicalAddress = newsletter.BrandPhysicalAddress,
                },
            });
        });

        // Register the snooze background service
        services.AddHostedService<TicketSnoozeBackgroundService>();

        // Register the chat cleanup background service
        services.AddHostedService<ChatCleanupBackgroundService>();

        // Register newsletter dispatch worker. It is inert unless enabled.
        services.AddHostedService<NewsletterDispatchWorker>();

        // Register the time-based schedulers. Without these, Automations, SLA
        // breach detection, and escalation rules never run.
        services.AddHostedService<AutomationBackgroundService>();
        services.AddHostedService<SlaMonitorBackgroundService>();
        services.AddHostedService<EscalationBackgroundService>();

        return services;
    }

    /// <summary>
    /// Registers a chained <see cref="IStringLocalizer"/> stack that
    /// resolves strings from plugin-local overrides under
    /// <c>Resources/Overrides/</c> first, falling through to the
    /// vendored central catalog at <c>Resources/locales/*.json</c>
    /// (sourced from escalated-dev/escalated-locale; will swap back
    /// to a runtime dep on the <c>Escalated.Locale</c> NuGet package
    /// once that publish pipeline is online).
    /// </summary>
    private static void AddEscalatedLocalization(this IServiceCollection services)
    {
        // Standard ASP.NET Core localization (resx + JSON readers).
        services.AddLocalization(opts => opts.ResourcesPath = "Resources/Overrides");

        // Decorate the default factory: chain plugin-local first,
        // central (vendored) catalog second.
        services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory>(sp =>
        {
            var inner = ActivatorUtilities.CreateInstance<ResourceManagerStringLocalizerFactory>(sp);
            return new EscalatedLocalizerFactory(inner);
        }));
    }

    private sealed class EscalatedServicesMarker
    {
    }
}
