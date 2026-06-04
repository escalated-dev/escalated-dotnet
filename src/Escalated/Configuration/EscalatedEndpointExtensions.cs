using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Configuration;

public static class EscalatedEndpointExtensions
{
    /// <summary>
    /// Maps all Escalated MVC controllers under the configured route prefix.
    /// Call after app.MapControllers() or use this instead.
    /// </summary>
    public static IEndpointRouteBuilder MapEscalated(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        var prefix = options.RoutePrefix.TrimStart('/').TrimEnd('/');

        // Customer routes
        endpoints.MapControllerRoute(
            name: "escalated-customer",
            pattern: $"{prefix}/{{action=Index}}/{{id?}}",
            defaults: new { controller = "CustomerTicket", area = "Escalated" });

        // Guest routes
        endpoints.MapControllerRoute(
            name: "escalated-guest-create",
            pattern: $"{prefix}/guest/create",
            defaults: new { controller = "Widget", action = "Create", area = "Escalated" });

        endpoints.MapControllerRoute(
            name: "escalated-guest-lookup",
            pattern: $"{prefix}/guest/{{token}}",
            defaults: new { controller = "Widget", action = "Lookup", area = "Escalated" });

        // Agent routes
        endpoints.MapControllerRoute(
            name: "escalated-agent",
            pattern: $"{prefix}/agent/{{action=Index}}/{{id?}}",
            defaults: new { controller = "AgentTicket", area = "Escalated" });

        // Admin routes
        endpoints.MapControllerRoute(
            name: "escalated-admin",
            pattern: $"{prefix}/admin/{{controller=AdminTicket}}/{{action=Index}}/{{id?}}",
            defaults: new { area = "Escalated" });

        // Widget API routes
        endpoints.MapControllerRoute(
            name: "escalated-widget",
            pattern: $"{prefix}/widget/{{action}}/{{id?}}",
            defaults: new { controller = "Widget", area = "Escalated" });

        MapNewsletterRoutes(endpoints);

        return endpoints;
    }

    /// <summary>
    /// Registers canonical newsletter routes (admin CRUD at <c>/admin/newsletters*</c>,
    /// tracking at <c>/escalated/n/*</c>, webhooks at <c>/escalated/webhooks/newsletter/*</c>).
    /// Controllers use attribute routing; the host must call <c>MapControllers()</c> with
    /// this assembly included via <c>AddApplicationPart</c>.
    /// </summary>
    private static void MapNewsletterRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute("AdminNewsletter.Index", "admin/newsletters", new { controller = "AdminNewsletter", action = "Index" });
        endpoints.MapControllerRoute("AdminNewsletter.Store", "admin/newsletters", new { controller = "AdminNewsletter", action = "Store" });
        endpoints.MapControllerRoute("AdminNewsletter.Create", "admin/newsletters/new", new { controller = "AdminNewsletter", action = "Create" });
        endpoints.MapControllerRoute("AdminNewsletter.Show", "admin/newsletters/{newsletter}", new { controller = "AdminNewsletter", action = "Show" });
        endpoints.MapControllerRoute("AdminNewsletter.Update", "admin/newsletters/{newsletter}", new { controller = "AdminNewsletter", action = "Update" });
        endpoints.MapControllerRoute("AdminNewsletter.Destroy", "admin/newsletters/{newsletter}", new { controller = "AdminNewsletter", action = "Destroy" });
        endpoints.MapControllerRoute("AdminNewsletter.Preview", "admin/newsletters/preview", new { controller = "AdminNewsletter", action = "Preview" });
        endpoints.MapControllerRoute("AdminNewsletter.Test", "admin/newsletters/test", new { controller = "AdminNewsletter", action = "Test" });

        endpoints.MapControllerRoute("AdminNewsletterList.Index", "admin/newsletters/lists", new { controller = "AdminNewsletterList", action = "Index" });
        endpoints.MapControllerRoute("AdminNewsletterList.Store", "admin/newsletters/lists", new { controller = "AdminNewsletterList", action = "Store" });
        endpoints.MapControllerRoute("AdminNewsletterList.Create", "admin/newsletters/lists/new", new { controller = "AdminNewsletterList", action = "Create" });
        endpoints.MapControllerRoute("AdminNewsletterList.Show", "admin/newsletters/lists/{list}", new { controller = "AdminNewsletterList", action = "Show" });
        endpoints.MapControllerRoute("AdminNewsletterList.Update", "admin/newsletters/lists/{list}", new { controller = "AdminNewsletterList", action = "Update" });
        endpoints.MapControllerRoute("AdminNewsletterList.Destroy", "admin/newsletters/lists/{list}", new { controller = "AdminNewsletterList", action = "Destroy" });
        endpoints.MapControllerRoute("AdminNewsletterList.AddMember", "admin/newsletters/lists/{list}/members", new { controller = "AdminNewsletterList", action = "AddMember" });
        endpoints.MapControllerRoute("AdminNewsletterList.RemoveMember", "admin/newsletters/lists/{list}/members/{contactId}", new { controller = "AdminNewsletterList", action = "RemoveMember" });
        endpoints.MapControllerRoute("AdminNewsletterList.ImportCsv", "admin/newsletters/lists/{list}/import", new { controller = "AdminNewsletterList", action = "ImportCsv" });

        endpoints.MapControllerRoute("AdminNewsletterTemplate.Index", "admin/newsletters/templates", new { controller = "AdminNewsletterTemplate", action = "Index" });
        endpoints.MapControllerRoute("AdminNewsletterTemplate.Store", "admin/newsletters/templates", new { controller = "AdminNewsletterTemplate", action = "Store" });
        endpoints.MapControllerRoute("AdminNewsletterTemplate.Create", "admin/newsletters/templates/new", new { controller = "AdminNewsletterTemplate", action = "Create" });
        endpoints.MapControllerRoute("AdminNewsletterTemplate.Show", "admin/newsletters/templates/{template}", new { controller = "AdminNewsletterTemplate", action = "Show" });
        endpoints.MapControllerRoute("AdminNewsletterTemplate.Update", "admin/newsletters/templates/{template}", new { controller = "AdminNewsletterTemplate", action = "Update" });
        endpoints.MapControllerRoute("AdminNewsletterTemplate.Destroy", "admin/newsletters/templates/{template}", new { controller = "AdminNewsletterTemplate", action = "Destroy" });

        endpoints.MapControllerRoute("AdminNewsletterSettings.Show", "admin/newsletters/settings", new { controller = "AdminNewsletterSettings", action = "Show" });
        endpoints.MapControllerRoute("AdminNewsletterSettings.Update", "admin/newsletters/settings", new { controller = "AdminNewsletterSettings", action = "Update" });

        endpoints.MapControllerRoute("NewsletterPublic.Open", "escalated/n/o/{token}.gif", new { controller = "NewsletterPublic", action = "Open" });
        endpoints.MapControllerRoute("NewsletterPublic.Click", "escalated/n/c/{token}", new { controller = "NewsletterPublic", action = "Click" });
        endpoints.MapControllerRoute("NewsletterPublic.UnsubscribeShow", "escalated/n/u/{token}", new { controller = "NewsletterPublic", action = "UnsubscribeShow" });
        endpoints.MapControllerRoute("NewsletterPublic.UnsubscribeStore", "escalated/n/u/{token}", new { controller = "NewsletterPublic", action = "UnsubscribeStore" });
        endpoints.MapControllerRoute("NewsletterPublic.View", "escalated/n/v/{token}", new { controller = "NewsletterPublic", action = "View" });

        endpoints.MapControllerRoute("NewsletterEspWebhook.Postmark", "escalated/webhooks/newsletter/postmark", new { controller = "NewsletterEspWebhook", action = "Postmark" });
        endpoints.MapControllerRoute("NewsletterEspWebhook.Mailgun", "escalated/webhooks/newsletter/mailgun", new { controller = "NewsletterEspWebhook", action = "Mailgun" });
        endpoints.MapControllerRoute("NewsletterEspWebhook.Ses", "escalated/webhooks/newsletter/ses", new { controller = "NewsletterEspWebhook", action = "Ses" });
        endpoints.MapControllerRoute("NewsletterEspWebhook.Sendgrid", "escalated/webhooks/newsletter/sendgrid", new { controller = "NewsletterEspWebhook", action = "Sendgrid" });
    }
}
