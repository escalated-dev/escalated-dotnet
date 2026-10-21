using System.Text.Json;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A spendable email proof for widget tests that are not about verification:
/// issues a code through <see cref="GuestAccessService"/> as the
/// <c>verification</c> endpoint does, without needing a mailbox.
/// </summary>
public static class GuestProof
{
    public static async Task<(string Id, string Code)> IssueAsync(EscalatedTestHost host, string email, string purpose = "ticket")
    {
        using var scope = host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<GuestAccessService>()
            .IssueCodeAsync(email, purpose, $"test-{Guid.NewGuid():N}");
        return (result.VerificationId!, result.Code!);
    }

    /// <summary>The widget's ticket body plus a fresh proof for <paramref name="email"/>.</summary>
    public static async Task<Dictionary<string, object?>> WithProofAsync(EscalatedTestHost host, Dictionary<string, object?> body, string purpose = "ticket")
    {
        var (id, code) = await IssueAsync(host, (string)body["email"]!, purpose);
        body["verification_id"] = id;
        body["verification_code"] = code;
        return body;
    }

    /// <summary>
    /// <paramref name="body"/> as a dictionary, with a fresh proof added when
    /// <paramref name="path"/> creates a widget ticket or chat and the body has an email.
    /// </summary>
    public static async Task<object> ProvedAsync(EscalatedTestHost host, string path, object body)
    {
        var purpose = path.EndsWith("/widget/tickets", StringComparison.Ordinal) ? "ticket"
            : path.EndsWith("/chat/start", StringComparison.Ordinal) ? "chat"
            : null;
        if (purpose is null) return body;

        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(body))!;
        if (dict.TryGetValue("email", out var email) && email is JsonElement { ValueKind: JsonValueKind.String } e
            && GuestAccessService.NormalizeEmail(e.GetString()) is not null)
        {
            var (id, code) = await IssueAsync(host, e.GetString()!, purpose);
            dict["verification_id"] = id;
            dict["verification_code"] = code;
        }

        return dict;
    }
}
