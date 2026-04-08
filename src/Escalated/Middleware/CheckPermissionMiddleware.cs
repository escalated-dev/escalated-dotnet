using Escalated.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Middleware;

/// <summary>
/// Middleware that checks if the current API token has a required ability.
/// </summary>
public class CheckPermissionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _requiredAbility;

    public CheckPermissionMiddleware(RequestDelegate next, string requiredAbility)
    {
        _next = next;
        _requiredAbility = requiredAbility;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var apiToken = context.Items["EscalatedApiToken"] as Models.ApiToken;
        if (apiToken != null && !apiToken.HasAbility(_requiredAbility))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = $"Token lacks required ability: {_requiredAbility}" });
            return;
        }

        await _next(context);
    }
}
