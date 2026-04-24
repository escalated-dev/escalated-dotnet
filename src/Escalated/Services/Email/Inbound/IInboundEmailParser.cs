namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Transport-specific parser that normalizes a provider's webhook
/// payload into an <see cref="InboundMessage"/>. Implementations are
/// registered via DI and chosen by <see cref="Name"/> (matches the
/// adapter label on <c>POST /escalated/webhook/email/inbound</c>).
///
/// Add a new provider by implementing this interface and registering
/// the type in the host app's <c>AddEscalated()</c> pipeline.
/// </summary>
public interface IInboundEmailParser
{
    /// <summary>
    /// Short provider name. Must match the value in the
    /// <c>?adapter=...</c> query or <c>X-Escalated-Adapter</c>
    /// header on the inbound webhook request.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Parse a raw webhook payload (e.g. a JObject of Postmark's
    /// JSON body) into an <see cref="InboundMessage"/>.
    /// </summary>
    /// <param name="rawPayload">Provider-native payload object.</param>
    Task<InboundMessage> ParseAsync(object rawPayload, CancellationToken ct = default);
}
