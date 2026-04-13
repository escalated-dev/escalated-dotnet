using Escalated.Models;
using Microsoft.AspNetCore.Http;

namespace Escalated.Extensions;

public static class AttachmentUrlExtensions
{
    /// <summary>
    /// Populates the <see cref="Attachment.Url"/> property for each attachment
    /// using the download endpoint route: /support/attachments/{id}/download.
    /// </summary>
    public static void PopulateAttachmentUrls(this Ticket ticket, HttpRequest request)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}";
        foreach (var attachment in ticket.Attachments)
        {
            attachment.Url = $"{baseUrl}/support/attachments/{attachment.Id}/download";
        }
        foreach (var reply in ticket.Replies)
        {
            foreach (var attachment in reply.Attachments)
            {
                attachment.Url = $"{baseUrl}/support/attachments/{attachment.Id}/download";
            }
        }
    }

    /// <summary>
    /// Populates the <see cref="Attachment.Url"/> property for each attachment on a reply.
    /// </summary>
    public static void PopulateAttachmentUrls(this Reply reply, HttpRequest request)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}";
        foreach (var attachment in reply.Attachments)
        {
            attachment.Url = $"{baseUrl}/support/attachments/{attachment.Id}/download";
        }
    }
}
