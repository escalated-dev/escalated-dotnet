using System.Globalization;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Reference <see cref="IAttachmentStorage"/> for hosts without cloud
/// storage — writes to the local filesystem under a configured root.
/// Files are prefixed with a UTC timestamp (including ticks) to avoid
/// collisions between uploads with the same original filename.
///
/// <para>Host apps with durable storage needs should implement
/// <see cref="IAttachmentStorage"/> themselves and inject their
/// S3/Azure/GCS adapter into <see cref="AttachmentDownloader"/>
/// instead of using this class.</para>
/// </summary>
public class LocalFileAttachmentStorage : IAttachmentStorage
{
    public string Name => "local";

    public string Root { get; }

    public LocalFileAttachmentStorage(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Local file storage root is required.", nameof(root));
        }
        Directory.CreateDirectory(root);
        Root = root;
    }

    public async Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var prefix = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-" + now.Ticks.ToString(CultureInfo.InvariantCulture);
        var storedName = $"{prefix}-{filename}";
        var fullPath = Path.Combine(Root, storedName);

        try
        {
            await using var file = File.Create(fullPath);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            // Best-effort cleanup on partial write.
            try { File.Delete(fullPath); } catch { /* ignore */ }
            throw;
        }
        return fullPath;
    }
}
