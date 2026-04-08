using System.Security.Cryptography;
using System.Text;
using Escalated.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Middleware;

/// <summary>
/// Middleware that authenticates requests using Bearer API tokens.
/// Tokens are stored as SHA-256 hashes for security.
/// </summary>
public class ApiTokenAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ApiTokenAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid Authorization header." });
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var tokenHash = ComputeSha256(token);

        var db = context.RequestServices.GetRequiredService<EscalatedDbContext>();
        var apiToken = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (apiToken == null || apiToken.IsExpired)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired API token." });
            return;
        }

        // Update last used
        apiToken.LastUsedAt = DateTime.UtcNow;
        db.ApiTokens.Update(apiToken);
        await db.SaveChangesAsync();

        // Store token info in HttpContext for downstream use
        context.Items["EscalatedApiToken"] = apiToken;
        context.Items["EscalatedApiTokenId"] = apiToken.Id;
        context.Items["EscalatedApiTokenableId"] = apiToken.TokenableId;

        await _next(context);
    }

    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
