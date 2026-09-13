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

        return endpoints;
    }
}
