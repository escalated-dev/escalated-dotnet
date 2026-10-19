using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Serves the standalone widget bundle (<c>Resources/widget/escalated-widget.js</c>,
/// built by <c>@escalated-dev/escalated</c> and embedded in this assembly) so any
/// site can load it with a <c>&lt;script&gt;</c> tag pointing at this install.
/// </summary>
[ApiController]
[Route("support/widget")]
[AllowAnonymous]
public class WidgetScriptController : ControllerBase
{
    public const string ResourceName = "Escalated.Widget.escalated-widget.js";

    private static readonly Lazy<(byte[] Body, string ETag)?> Bundle = new(Load);

    [HttpGet("embed.js")]
    public IActionResult Script()
    {
        if (Bundle.Value is not var (body, etag))
        {
            return NotFound();
        }

        var headers = Response.Headers;
        headers.CacheControl = "public, max-age=3600";
        headers.ETag = etag;
        headers.XContentTypeOptions = "nosniff";
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        headers.AccessControlAllowOrigin = "*";

        if (Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } ifNoneMatch
            && ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(body, "application/javascript; charset=utf-8");
    }

    private static (byte[] Body, string ETag)? Load()
    {
        using var stream = typeof(WidgetScriptController).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var body = buffer.ToArray();
        return (body, "\"" + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()[..32] + "\"");
    }
}
