using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Email.Inbound;
using Microsoft.AspNetCore.Http;

namespace Escalated.Ui;

/// <summary>
/// Files attached to a ticket or reply from a page (<c>attachments[]</c>), checked against
/// <see cref="AttachmentOptions"/> and stored through <see cref="IAttachmentStorage"/>.
/// </summary>
internal static class UiAttachments
{
    /// <summary>The validation message for the upload, or null when it can be stored.</summary>
    public static string? Validate(IFormFileCollection? files, AttachmentOptions options)
    {
        if (files is null || files.Count == 0)
        {
            return null;
        }

        if (options.MaxPerReply > 0 && files.Count > options.MaxPerReply)
        {
            return $"No more than {options.MaxPerReply} attachments may be added.";
        }

        foreach (var file in files)
        {
            var name = AttachmentDownloader.SafeFilename(file.FileName);
            if (InboundEmailService.BlockedExtensions.Contains(Path.GetExtension(name).TrimStart('.')))
            {
                return $"{name} is a file type that cannot be attached.";
            }

            if (options.MaxSizeKb > 0 && file.Length > (long)options.MaxSizeKb * 1024)
            {
                return $"{name} is larger than {options.MaxSizeKb} KB.";
            }
        }

        return null;
    }

    public static async Task StoreAsync(EscalatedDbContext db, IAttachmentStorage storage, IFormFileCollection? files,
        string attachableType, int attachableId, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = AttachmentDownloader.SafeFilename(file.FileName);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            await using var stream = file.OpenReadStream();
            var path = await storage.PutAsync(name, stream, contentType, ct);
            db.Attachments.Add(new Attachment
            {
                AttachableType = attachableType,
                AttachableId = attachableId,
                Filename = name,
                MimeType = contentType,
                Size = file.Length,
                Path = path,
                Disk = storage.Name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
