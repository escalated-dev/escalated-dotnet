using Escalated.Configuration;
using Escalated.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers;

[ApiController]
[Route("support/attachments")]
public class AttachmentController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;

    public AttachmentController(EscalatedDbContext db, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>
    /// Download an attachment by ID.
    /// </summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id);
        if (attachment == null) return NotFound();

        // For local disk storage, resolve the path relative to the app's content root
        if (attachment.Disk == "local")
        {
            var filePath = attachment.Path;
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "File not found on disk." });

            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, attachment.MimeType, attachment.Filename);
        }

        // For other storage disks, the Path may already be a full URL
        return Redirect(attachment.Path);
    }
}
