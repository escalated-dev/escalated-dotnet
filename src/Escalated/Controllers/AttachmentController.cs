using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers;

[ApiController]
[Route("support/attachments")]
[Authorize(Policy = EscalatedPolicies.Customer)]
public class AttachmentController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;
    private readonly IAuthorizationService _authorization;

    public AttachmentController(EscalatedDbContext db, IOptions<EscalatedOptions> options,
        IAuthorizationService authorization)
    {
        _db = db;
        _options = options.Value;
        _authorization = authorization;
    }

    /// <summary>
    /// Download an attachment by ID. Staff may download any attachment; anyone else
    /// only one on a ticket they requested, and never one on an internal note.
    /// </summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id);
        if (attachment == null) return NotFound();

        if (!await MayDownloadAsync(attachment)) return Forbid();

        // For local disk storage, resolve the path relative to the app's content root
        if (attachment.Disk == "local")
        {
            var filePath = attachment.Path;
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "File not found on disk." });

            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, attachment.MimeType, attachment.Filename);
        }

        if (!IsSafeRedirectUrl(attachment.Path))
        {
            return BadRequest(new { error = "Attachment storage path is not a valid download URL." });
        }

        // For other storage disks, the Path must be an absolute HTTP(S) URL.
        return Redirect(attachment.Path);
    }

    private async Task<bool> MayDownloadAsync(Attachment attachment)
    {
        if ((await _authorization.AuthorizeAsync(User, EscalatedPolicies.Agent)).Succeeded)
        {
            return true;
        }

        var userId = this.CurrentUserId();
        if (userId is null)
        {
            return false;
        }

        int? ticketId = null;
        if (attachment.AttachableType.EndsWith("ticket", StringComparison.OrdinalIgnoreCase))
        {
            ticketId = attachment.AttachableId;
        }
        else if (attachment.AttachableType.EndsWith("reply", StringComparison.OrdinalIgnoreCase))
        {
            ticketId = await _db.Replies
                .Where(r => r.Id == attachment.AttachableId && !r.IsInternalNote)
                .Select(r => (int?)r.TicketId)
                .FirstOrDefaultAsync();
        }

        return ticketId is not null
               && await _db.Tickets.AnyAsync(t => t.Id == ticketId && t.RequesterId == userId);
    }

    private static bool IsSafeRedirectUrl(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
