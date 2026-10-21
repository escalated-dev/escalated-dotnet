using System.Text.Json.Serialization;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Controllers.Widget;

/// <summary>The admin switches the widget and guest routes read (settings table).</summary>
public static class GuestSettings
{
    /// <summary>Setting key: the widget's routes answer at all. Default on.</summary>
    public const string WidgetEnabled = "widget_enabled";

    /// <summary>Setting key: guests may open tickets and look them up. Default on.</summary>
    public const string GuestTicketsEnabled = "guest_tickets_enabled";

    public static async Task<bool> IsOnAsync(SettingsService settings, string key, CancellationToken ct = default)
    {
        var value = (await settings.GetAsync(key, null, ct))?.Trim().ToLowerInvariant();
        return value is not ("0" or "false" or "off" or "no");
    }
}

/// <summary>
/// Refuses every widget request with 403 while the <c>widget_enabled</c> setting
/// is off (<c>/config</c> answers <c>{enabled: false}</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class WidgetEnabledAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var settings = context.HttpContext.RequestServices.GetRequiredService<SettingsService>();
        if (!await GuestSettings.IsOnAsync(settings, GuestSettings.WidgetEnabled, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new { enabled = false, message = "The widget is disabled." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}

public record GuestVerificationRequest(string? Email, string? Purpose);

public record GuestLookupRequest(string? Email, string? Reference)
{
    [JsonPropertyName("verification_id")]
    public string? VerificationId { get; init; }

    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }
}

/// <summary>
/// <c>POST verification</c> and <c>POST lookup</c>, shared by the widget
/// (<c>support/widget</c>) and guest (<c>support/guest</c>) routes. See
/// <c>docs/guest-access.md</c>.
/// </summary>
internal static class GuestAccessEndpoints
{
    private const int LookupLimit = 20;

    public static async Task<IActionResult> VerificationAsync(ControllerBase controller, GuestVerificationRequest request)
    {
        var services = controller.HttpContext.RequestServices;
        var ct = controller.HttpContext.RequestAborted;
        NoStore(controller.Response);

        var errors = new Dictionary<string, string[]>();
        if (GuestAccessService.NormalizeEmail(request.Email) is null) errors["email"] = new[] { "A valid email address is required." };
        if (request.Purpose is null || !GuestAccessService.Purposes.Contains(request.Purpose))
            errors["purpose"] = new[] { "The purpose must be ticket, chat or lookup." };
        if (errors.Count > 0) return Invalid(controller, errors);

        if (request.Purpose != "chat"
            && !await GuestSettings.IsOnAsync(services.GetRequiredService<SettingsService>(), GuestSettings.GuestTicketsEnabled, ct))
        {
            return controller.StatusCode(StatusCodes.Status403Forbidden, new { message = "Guest tickets are disabled." });
        }

        var result = await services.GetRequiredService<GuestAccessService>()
            .RequestCodeAsync(request.Email!, request.Purpose!, ClientIp(controller.HttpContext), ct);
        if (result.Throttled)
        {
            controller.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return controller.StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many verification codes were requested for this email. Try again later." });
        }

        return controller.StatusCode(StatusCodes.Status202Accepted, new
        {
            verification_id = result.VerificationId,
            expires_in = GuestAccessService.CodeLifetimeSeconds,
            message = "If the address can receive email, a verification code is on its way.",
        });
    }

    public static async Task<IActionResult> LookupAsync(ControllerBase controller, GuestLookupRequest request)
    {
        var services = controller.HttpContext.RequestServices;
        var ct = controller.HttpContext.RequestAborted;
        NoStore(controller.Response);

        var email = GuestAccessService.NormalizeEmail(request.Email);
        var reference = request.Reference?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (email is null) errors["email"] = new[] { "A valid email address is required." };
        if (string.IsNullOrEmpty(reference) || reference.Length > 255) errors["reference"] = new[] { "The reference is required." };
        if (string.IsNullOrEmpty(request.VerificationId)) errors["verification_id"] = new[] { "Request a verification code first." };
        if (string.IsNullOrEmpty(request.VerificationCode)) errors["verification_code"] = new[] { "Enter the code from your email." };
        if (errors.Count > 0) return Invalid(controller, errors);

        if (!await GuestSettings.IsOnAsync(services.GetRequiredService<SettingsService>(), GuestSettings.GuestTicketsEnabled, ct))
            return controller.StatusCode(StatusCodes.Status403Forbidden, new { message = "Guest tickets are disabled." });

        var access = services.GetRequiredService<GuestAccessService>();
        if (!await access.ConsumeAsync(request.VerificationId, request.VerificationCode, email, "lookup", ct))
            return InvalidCode(controller);

        // A reference alone unlocks nothing: the verified email must be the ticket's guest email.
        var db = services.GetRequiredService<EscalatedDbContext>();
        var candidates = await TicketAccessScope.WithoutVisibilityAsync(() => db.Tickets
            .Where(t => t.Reference == reference && t.GuestEmail != null)
            .Take(LookupLimit)
            .ToListAsync(ct));

        var data = new List<object>();
        foreach (var ticket in candidates.Where(t => GuestAccessService.NormalizeEmail(t.GuestEmail) == email))
        {
            var (token, expires) = await access.IssueGrantAsync(ticket, ct);
            data.Add(new { reference = ticket.Reference, subject = ticket.Subject, guest_access_token = token, expires_at = expires });
        }

        return controller.Ok(new { data });
    }

    /// <summary>Guest answers are private and never cached, nor leak their URL onwards.</summary>
    public static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, private";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }

    public static IActionResult Invalid(ControllerBase controller, Dictionary<string, string[]> errors) =>
        controller.UnprocessableEntity(new { message = errors.Values.First().First(), errors });

    public static IActionResult InvalidCode(ControllerBase controller) =>
        Invalid(controller, new Dictionary<string, string[]>
        {
            ["verification_code"] = new[] { "The verification code is invalid or has expired." },
        });

    public static string ClientIp(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true }) address = address.MapToIPv4();
        return address?.ToString() ?? "unknown";
    }
}
