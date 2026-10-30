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
using Escalated.Authorization;
using Escalated.Widget;
using Microsoft.AspNetCore.Hosting;

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

        // WebhookDispatcher sends through IHttpClientFactory, and waits out its retry
        // backoff on TimeProvider.
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        // Webhook deliveries and inbound attachment downloads go to URLs that admins
        // and email senders choose. Their handler resolves the host itself, refuses
        // loopback/private/link-local/metadata addresses, and never follows redirects.
        services.AddHttpClient(WebhookDispatcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => OutboundUrlGuard.CreateHandler());
        services.AddHttpClient<AttachmentDownloader>()
            .ConfigurePrimaryHttpMessageHandler(() => OutboundUrlGuard.CreateHandler());
        services.TryAddSingleton(sp => new AttachmentDownloaderOptions
        {
            MaxBytes = (long)sp.GetRequiredService<IOptions<EscalatedOptions>>().Value.Attachments.MaxSizeKb * 1024,
        });
        services.TryAddSingleton<IAttachmentStorage>(sp =>
        {
            var root = sp.GetRequiredService<IOptions<EscalatedOptions>>().Value.Attachments.LocalRoot;
            var contentRoot = sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory;
            return new LocalFileAttachmentStorage(Path.IsPathRooted(root) ? root : Path.Combine(contentRoot, root));
        });

        // Domain events. Escalated's services dispatch through EscalatedEventDispatcher,
        // which delivers webhooks, runs Workflows, then hands the event to every
        // IEscalatedEventDispatcher the host registered. A host dispatcher is a
        // listener: registering one, before or after this call, adds to the
        // pipeline instead of replacing the Workflow bridge.
        services.AddSingleton<WorkflowEventDispatcher>();
        services.AddSingleton<NotificationEventDispatcher>();
        services.AddSingleton<WebhookEventDispatcher>();
        services.AddScoped<EscalatedEventDispatcher>();
        services.AddScoped<IEscalatedEventDispatcher>(sp => sp.GetRequiredService<EscalatedEventDispatcher>());

        // Custom ticket action registry (host apps can override for dynamic
        // per-ticket/user visibility).
        services.TryAddSingleton<ITicketActionRegistry, TicketActionRegistry>();

        // Register user directory (empty default; host apps register their own
        // implementation to surface their user table in the admin users page).
        services.TryAddSingleton<IUserDirectory, NullUserDirectory>();

        services.TryAddSingleton<ITicketSubjectResolver, NullTicketSubjectResolver>();

        // Per-IP counters for the guest widget endpoints. In memory by default;
        // a multi-instance host registers a shared IGuestRateLimiter.
        services.TryAddSingleton<IGuestRateLimiter, MemoryGuestRateLimiter>();

        // Embedding the widget on other sites: CORS at the front of the pipeline
        // for the widget routes, guest forcing after host authentication, and
        // ESCALATED_WIDGET_ALLOWED_ORIGINS. Inert until an origin is listed.
        services.AddScoped<WidgetOriginPolicy>();
        services.AddSingleton<IPostConfigureOptions<EscalatedOptions>, WidgetOriginsFromConfiguration>();
        services.AddTransient<IStartupFilter, WidgetCorsStartupFilter>();
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options => options.Filters.Add(new WidgetGuestFilter()));

        // Notification emails: the default sender picks recipients as the Laravel
        // reference does and sends through SMTP (Escalated:Email:Smtp). With no SMTP
        // host it sends nothing and logs that once. Hosts register their own
        // IEscalatedNotificationSender (or IEscalatedMailTransport) before or after
        // AddEscalated; NotificationEventDispatcher calls whichever resolves.
        services.TryAddSingleton<IEscalatedMailTransport, SmtpMailTransport>();
        services.TryAddScoped<IEscalatedNotificationSender, EmailNotificationSender>();

        services.AddEscalatedLocalization();

        // The Inertia UI (Escalated:Ui:Enabled, off by default). Its routes match
        // nothing until it is on; these services only render pages. The root view
        // and the shared props are host-replaceable (see docs/inertia-ui.md). Flash
        // messages travel in a data-protected cookie, so no session is needed.
        services.AddDataProtection();
        services.AddOptions<Escalated.Ui.UiRouteOptions>();
        services.TryAddScoped<Escalated.Ui.IEscalatedUiRootView, Escalated.Ui.DefaultEscalatedUiRootView>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Escalated.Ui.IEscalatedUiSharedProps, Escalated.Ui.EscalatedUiSharedProps>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Escalated.Ui.IEscalatedUiSharedProps, Escalated.Plugins.PluginSharedProps>());
        services.AddScoped<Escalated.Ui.UiPresenter>();
        services.TryAddSingleton(sp => new Escalated.Ui.UiPresence(sp.GetService<TimeProvider>()));
        services.TryAddSingleton<Escalated.Middleware.ApiTokenAuthState>();
        services.AddScoped<Escalated.Controllers.Ui.TicketPageServices>();

        // Controllers marked [EscalatedPrefixedRoute] answer under RoutePrefix.
        services.AddOptions<Microsoft.AspNetCore.Mvc.MvcOptions>()
            .Configure<IOptions<EscalatedOptions>>((mvc, escalated) =>
                mvc.Conventions.Add(new EscalatedRoutePrefixConvention(escalated.Value.RoutePrefix)));

        // The admin, agent and customer authorization policies the controllers
        // require. A host policy of the same name takes precedence.
        services.AddEscalatedAuthorization();
        services.AddScoped<TicketAccess>();
        services.TryAddSingleton<Escalated.Hubs.TicketRealtimeAudience>();
        services.AddScoped<Escalated.Hubs.TicketBroadcastService>();
        services.AddScoped<TicketAccessFilter>();
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options => options.Filters.AddService<TicketAccessFilter>());
        services.AddOptions<EscalatedOptions>()
            .Validate(options => options.Visibility is "all" or "department", "Visibility must be all or department.")
            .Validate(options => !options.Accounts.ScopeAgents || (options.Accounts.Enabled && options.Accounts.Resolver is not null), "Account visibility requires Accounts.Enabled and Accounts.Resolver.")
            .ValidateOnStart();

        // Plugins: native IEscalatedPlugin services and SDK plugins over the runtime
        // bridge (Escalated:Plugins:Runtime, off by default). See docs/plugins.md.
        services.TryAddSingleton<Escalated.Plugins.Bridge.PluginBridge>();
        services.TryAddSingleton<Escalated.Plugins.PluginEventRelay>();
        services.AddScoped<Escalated.Plugins.PluginStore>();
        services.AddScoped<Escalated.Plugins.PluginPayloads>();
        services.AddScoped<Escalated.Plugins.PluginAccess>();
        services.AddScoped<Escalated.Plugins.PluginContextHandler>();
        services.AddScoped<Escalated.Plugins.PluginRegistry>();
        services.AddScoped<Escalated.Plugins.PluginEventDispatcher>();
        services.AddTransient<IStartupFilter, Escalated.Plugins.PluginStartupCheck>();

        // Register services
        services.AddScopedWithEventBus<TicketService>();
        services.AddScoped<MentionService>();
        services.AddScopedWithEventBus<SlaService>();
        services.AddScopedWithEventBus<AssignmentService>();
        services.AddScopedWithEventBus<EscalationService>();
        services.AddScoped<MacroService>();
        services.AddScoped<TicketMergeService>();
        services.AddScopedWithEventBus<TicketSplitService>();
        services.AddScoped<TicketSnoozeService>();
        services.AddScoped<TicketSubjectService>();
        services.AddScoped<WebhookDispatcher>();
        services.AddScoped<AutomationRunner>();
        // Workflow engine: condition evaluator, action executor, and the
        // runner the event dispatcher drives.
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowExecutorService>();
        services.AddScoped<WorkflowRunnerService>();
        services.AddScoped<WorkflowDelayedActionProcessor>();
        services.AddScoped<GuestAccessService>();
        services.AddScoped<CapacityService>();
        services.AddScoped<SkillRoutingService>();
        services.AddScoped<BusinessHoursCalculator>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AdvancedReportingService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<DataRetentionService>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<ImportService>();
        // The admin Import wizard: credential sealing, and the in-process run a
        // host can replace with its own queue by registering IImportDispatcher first.
        services.TryAddSingleton<ImportCredentials>();
        services.TryAddSingleton<BackgroundImportDispatcher>();
        services.TryAddSingleton<IImportDispatcher>(sp => sp.GetRequiredService<BackgroundImportDispatcher>());
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<SideConversationService>();
        services.AddScopedWithEventBus<ChatSessionService>();
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
        // Install-level access switches (ADR 2026-10-29-configurable-access-features).
        services.TryAddSingleton<AccessFeatures>();
        services.AddScoped<AccessControl>();
        services.AddScoped<PermissionSeeder>();
        services.AddHostedService<PermissionSeederHostedService>();
        services.AddScoped<EnsureAccessFeaturesFilter>();
        services.AddScoped<EnforceRoutePermissionsFilter>();
        services.AddScoped<RequireTwoFactorFilter>();
        services.TryAddSingleton<Escalated.Controllers.Ui.TwoFactorThrottle>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>, TwoFactorCookieEvents>());
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            // A switched-off feature's routes 404 first; then the two-factor challenge; then each
            // staff route's permission.
            options.Filters.AddService<EnsureAccessFeaturesFilter>(-2000);
            options.Filters.AddService<RequireTwoFactorFilter>(-1500);
            options.Filters.AddService<EnforceRoutePermissionsFilter>(-1000);
        });
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
        services.AddHostedService<WorkflowDelayedActionBackgroundService>();
        services.AddHostedService<SlaMonitorBackgroundService>();
        services.AddHostedService<EscalationBackgroundService>();

        // Applies the data retention periods daily (every period defaults to never).
        services.AddHostedService<RetentionPurgeBackgroundService>();

        // Runs active SDK plugins' due cron schedules every minute (inert without the runtime).
        services.TryAddSingleton<Escalated.Plugins.PluginCronBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<Escalated.Plugins.PluginCronBackgroundService>());

        return services;
    }

    /// <summary>
    /// Registers a chained <see cref="IStringLocalizer"/> stack that
    /// resolves strings from plugin-local overrides under
    /// <c>Resources/Overrides/</c> first, falling through to the
    /// vendored central catalog at <c>Resources/locales/*.json</c>
    /// (sourced from escalated-dev/escalated-locale). An optional
    /// <c>Escalated.Locale</c> runtime provider is consulted before
    /// the embedded fallback when a host supplies that package.
    /// </summary>
    private static void AddEscalatedLocalization(this IServiceCollection services)
    {
        // Standard ASP.NET Core factory for optional resx overrides.
        services.AddLocalization(opts => opts.ResourcesPath = "Resources/Overrides");

        // Decorate the default factory: chain plugin-local first,
        // optional shared provider next, embedded JSON catalog last.
        services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory>(sp =>
        {
            var inner = ActivatorUtilities.CreateInstance<ResourceManagerStringLocalizerFactory>(sp);
            return new EscalatedLocalizerFactory(inner);
        }));
    }

    /// <summary>
    /// Registers a service that takes an <see cref="IEscalatedEventDispatcher"/>, and
    /// builds it with <see cref="EscalatedEventDispatcher"/> rather than whatever the
    /// interface resolves to. A host registering its own dispatcher after
    /// <c>AddEscalated</c> makes that registration the one the interface resolves to,
    /// and the service would otherwise dispatch to the host alone.
    /// </summary>
    private static void AddScopedWithEventBus<TService>(this IServiceCollection services)
        where TService : class
    {
        services.AddScoped(sp =>
            ActivatorUtilities.CreateInstance<TService>(sp, sp.GetRequiredService<EscalatedEventDispatcher>()));
    }

    private sealed class EscalatedServicesMarker
    {
    }
}
