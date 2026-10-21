using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Ui;

/// <summary>
/// What a redirect carries to the next page: flash messages (<c>success</c>,
/// <c>error</c>) and validation errors (<c>field → message</c>). Laravel keeps
/// these in the session; this package keeps them in one short-lived, data-protected
/// cookie, so a host needs no session middleware. The next page render reads and
/// clears it.
/// </summary>
public sealed class UiFlashData
{
    public Dictionary<string, string> Flash { get; set; } = new();
    public Dictionary<string, string> Errors { get; set; } = new();

    public bool IsEmpty => Flash.Count == 0 && Errors.Count == 0;
}

public static class UiFlash
{
    public const string CookieName = "escalated_flash";
    private const string Purpose = "Escalated.Ui.Flash";
    private const string ItemKey = "Escalated.Ui.Flash";

    /// <summary>Queues a flash message for the next page (<c>success</c> or <c>error</c>).</summary>
    public static void Put(HttpContext context, string key, string message)
    {
        var data = Pending(context);
        data.Flash[key] = message;
        Write(context, data);
    }

    /// <summary>Queues validation errors for the next page.</summary>
    public static void PutErrors(HttpContext context, IReadOnlyDictionary<string, string> errors)
    {
        var data = Pending(context);
        foreach (var (field, message) in errors)
        {
            data.Errors[field] = message;
        }

        Write(context, data);
    }

    /// <summary>The data queued by the previous request, removed as it is read.</summary>
    public static UiFlashData Take(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie))
        {
            return new UiFlashData();
        }

        if (!context.Response.HasStarted)
        {
            context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
        }

        try
        {
            var json = Protector(context).Unprotect(cookie);
            return JsonSerializer.Deserialize<UiFlashData>(json) ?? new UiFlashData();
        }
        catch (Exception)
        {
            // Tampered with, or written under a key ring this instance does not have.
            return new UiFlashData();
        }
    }

    private static UiFlashData Pending(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var existing) && existing is UiFlashData data)
        {
            return data;
        }

        data = new UiFlashData();
        context.Items[ItemKey] = data;
        return data;
    }

    private static void Write(HttpContext context, UiFlashData data)
    {
        var value = Protector(context).Protect(JsonSerializer.Serialize(data));
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(5),
        });
    }

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
}
