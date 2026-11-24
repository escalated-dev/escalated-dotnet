using Escalated.Controllers.Api;
using Escalated.Controllers.Customer;
using Escalated.Controllers.Guest;
using Escalated.Controllers.Ui;
using Escalated.Controllers.Widget;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace Escalated.Configuration;

/// <summary>
/// Leaves out the optional public routes an install switched off, and adds the host's
/// <see cref="EscalatedRouteOptions.CustomerPolicies"/> to the customer portal and the
/// knowledge base. With the defaults (widget and guest routes on, no customer policies)
/// it changes nothing.
/// <list type="bullet">
/// <item><see cref="WidgetOptions.Enabled"/> false: no <c>support/widget*</c> route.</item>
/// <item><see cref="GuestAccessOptions.RoutesEnabled"/> false: no <c>support/guest*</c> or
/// <c>support/api/v1/mobile/guest*</c> route.</item>
/// </list>
/// Escalated has no Slack inbound or cloud-to-site webhook routes, so there is nothing to
/// switch for those.
/// </summary>
internal sealed class OptionalRoutesActionDescriptorProvider : IActionDescriptorProvider
{
    internal static readonly Type[] WidgetControllers =
    {
        typeof(WidgetController), typeof(WidgetChatController), typeof(WidgetScriptController),
    };

    internal static readonly Type[] GuestControllers =
    {
        typeof(GuestPageController), typeof(GuestAccessController), typeof(MobileGuestController),
    };

    internal static readonly Type[] CustomerControllers =
    {
        typeof(CustomerPageController), typeof(CustomerTicketController), typeof(KnowledgeBasePageController),
    };

    private readonly IOptions<EscalatedOptions> _options;

    public OptionalRoutesActionDescriptorProvider(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    // After MVC's own providers (ControllerActionDescriptorProvider is -1000).
    public int Order => 1000;

    public void OnProvidersExecuting(ActionDescriptorProviderContext context)
    {
    }

    public void OnProvidersExecuted(ActionDescriptorProviderContext context)
    {
        var options = _options.Value;
        var removed = new HashSet<Type>();
        if (!options.Widget.RoutesRegistered) removed.UnionWith(WidgetControllers);
        if (!options.GuestAccess.RoutesRegistered) removed.UnionWith(GuestControllers);

        var policies = (options.Routes.CustomerPolicies ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        for (var i = context.Results.Count - 1; i >= 0; i--)
        {
            if (context.Results[i] is not ControllerActionDescriptor action)
            {
                continue;
            }

            var controller = action.ControllerTypeInfo.AsType();
            if (removed.Contains(controller))
            {
                context.Results.RemoveAt(i);
                continue;
            }

            if (policies.Count > 0 && CustomerControllers.Contains(controller))
            {
                // The knowledge base is [AllowAnonymous]; that would skip every policy, so it goes.
                var metadata = action.EndpointMetadata.Where(m => m is not IAllowAnonymous).ToList();
                metadata.AddRange(policies.Select(p => new AuthorizeAttribute(p)));
                action.EndpointMetadata = metadata;
            }
        }
    }
}
