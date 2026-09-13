using Escalated.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace Escalated.Controllers.Newsletter;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NewsletterEnabledAttribute : TypeFilterAttribute
{
    public NewsletterEnabledAttribute() : base(typeof(NewsletterEnabledFilter))
    {
    }
}

public sealed class NewsletterEnabledFilter : IAsyncActionFilter
{
    private readonly IOptions<EscalatedOptions> _options;

    public NewsletterEnabledFilter(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_options.Value.EnableNewsletters)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var executed = await next();

        // NewsletterPermissionService.RequireAsync throws when the signed-in admin
        // lacks the newsletter permission the action needs. Unhandled, that was a 500.
        if (executed.Exception is UnauthorizedAccessException denied && !executed.ExceptionHandled)
        {
            executed.Result = new ObjectResult(new { error = denied.Message }) { StatusCode = StatusCodes.Status403Forbidden };
            executed.ExceptionHandled = true;
        }
    }
}
