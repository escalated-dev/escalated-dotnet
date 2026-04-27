using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Email.Inbound;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers;

/// <summary>
/// Single ingress point for inbound-email webhooks. Dispatches the
/// raw payload to the matching <see cref="IInboundEmailParser"/>
/// (selected via the <c>?adapter=...</c> query parameter or
/// <c>X-Escalated-Adapter</c> header), then resolves the parsed
/// message to a ticket via <see cref="InboundEmailRouter"/>.
///
/// <para>Guarded by a constant-time shared-secret check on the
/// <c>X-Escalated-Inbound-Secret</c> header — hosts configure this
/// via <see cref="EmailOptions.InboundSecret"/> (reused for signed
/// Reply-To verification, so the key pair is symmetric).</para>
///
/// <para>Writes an <see cref="InboundEmail"/> audit row per request.
/// Returns <c>202 Accepted</c> with the inbound id for the caller to
/// correlate against logs.</para>
/// </summary>
[ApiController]
[Route("support/webhook/email")]
public class InboundEmailController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;
    private readonly InboundEmailRouter _router;
    private readonly IReadOnlyDictionary<string, IInboundEmailParser> _parsers;

    public InboundEmailController(
        EscalatedDbContext db,
        IOptions<EscalatedOptions> options,
        InboundEmailRouter router,
        IEnumerable<IInboundEmailParser> parsers)
    {
        _db = db;
        _options = options.Value;
        _router = router;
        _parsers = parsers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    [HttpPost("inbound")]
    public async Task<IActionResult> Inbound(
        [FromQuery(Name = "adapter")] string? queryAdapter,
        CancellationToken ct)
    {
        if (!VerifySignature())
        {
            return Unauthorized(new { error = "missing or invalid inbound secret" });
        }

        var adapter = queryAdapter
            ?? Request.Headers["X-Escalated-Adapter"].FirstOrDefault()
            ?? string.Empty;
        if (!_parsers.TryGetValue(adapter, out var parser))
        {
            return BadRequest(new { error = $"unknown adapter: {adapter}" });
        }

        // Re-read the body as JsonElement — ASP.NET has already
        // consumed it if we'd taken a typed [FromBody] parameter.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "invalid json body" });
        }

        var message = await parser.ParseAsync(payload, ct);

        // Audit row — log before routing so every inbound is traceable.
        var inboundEmail = new InboundEmail
        {
            MessageId = message.MessageId,
            FromEmail = message.FromEmail,
            FromName = message.FromName,
            ToEmail = message.ToEmail,
            Subject = message.Subject,
            BodyText = message.BodyText,
            BodyHtml = message.BodyHtml,
            InReplyTo = message.InReplyTo,
            Adapter = adapter,
            Status = "pending",
        };
        _db.InboundEmails.Add(inboundEmail);
        await _db.SaveChangesAsync(ct);

        try
        {
            var ticket = await _router.ResolveTicketAsync(message, ct);
            inboundEmail.TicketId = ticket?.Id;
            inboundEmail.Status = ticket is null ? "unmatched" : "matched";
            inboundEmail.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Accepted(new
            {
                inboundId = inboundEmail.Id,
                status = inboundEmail.Status,
                ticketId = ticket?.Id,
            });
        }
        catch (Exception ex)
        {
            inboundEmail.Status = "failed";
            inboundEmail.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return StatusCode(500, new { error = ex.Message, inboundId = inboundEmail.Id });
        }
    }

    private bool VerifySignature()
    {
        var expected = _options.Email.InboundSecret;
        if (string.IsNullOrEmpty(expected))
        {
            // Secret unconfigured — inbound is effectively disabled.
            return false;
        }
        var provided = Request.Headers["X-Escalated-Inbound-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
