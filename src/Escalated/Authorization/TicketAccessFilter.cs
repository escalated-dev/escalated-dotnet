using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Escalated.Authorization;

/// <summary>Encloses controller execution and response serialization after host authentication.</summary>
public sealed class TicketAccessFilter : IAsyncResourceFilter, IAsyncExceptionFilter
{
    private readonly TicketAccess access;
    public TicketAccessFilter(TicketAccess access) => this.access = access;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        await access.ForUserAsync(context.HttpContext.User, async () => await next(), context.HttpContext.RequestAborted);
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is TicketAccessDeniedException)
        {
            context.Result = new NotFoundObjectResult(new { error = "Ticket not found." });
            context.ExceptionHandled = true;
        }
        return Task.CompletedTask;
    }
}
