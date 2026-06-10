using Escalated.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

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

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_options.Value.EnableNewsletters)
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        return next();
    }
}
