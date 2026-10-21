using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Ui;

/// <summary>
/// Turns entities into the objects the shared pages read: snake_case keys, the
/// relations Laravel's Eloquent models serialize (<c>requester</c>, <c>assignee</c>,
/// <c>department</c>, <c>tags</c>, <c>latest_reply.author</c>), and enum values as
/// their wire strings. Scoped: it remembers the host users it looked up for one
/// request.
/// </summary>
public sealed class UiPresenter
{
    private readonly EscalatedDbContext _db;
    private readonly IUserDirectory _users;
    private readonly Dictionary<string, UserDirectoryEntry?> _userCache = new();

    public UiPresenter(EscalatedDbContext db, IUserDirectory users)
    {
        _db = db;
        _users = users;
    }

    /// <summary><c>{id, name, email}</c> for a host user id, or null without one.</summary>
    public async Task<Dictionary<string, object?>?> UserAsync(string? id, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (!_userCache.TryGetValue(id, out var entry))
        {
            entry = await _users.FindAsync(id, ct);
            _userCache[id] = entry;
        }

        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = entry?.Name ?? id,
            ["email"] = entry?.Email,
        };
    }

    public static Dictionary<string, object?> Department(Department department) => new()
    {
        ["id"] = department.Id,
        ["name"] = department.Name,
        ["slug"] = department.Slug,
        ["description"] = department.Description,
        ["is_active"] = department.IsActive,
    };

    public static Dictionary<string, object?> Tag(Tag tag) => new()
    {
        ["id"] = tag.Id,
        ["name"] = tag.Name,
        ["slug"] = tag.Slug,
        ["color"] = tag.Color,
    };

    /// <summary>Active departments as <c>{id, name}</c>, for the pickers.</summary>
    public async Task<List<Dictionary<string, object?>>> DepartmentOptionsAsync(CancellationToken ct = default) =>
        (await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct))
            .Select(d => new Dictionary<string, object?> { ["id"] = d.Id, ["name"] = d.Name }).ToList();

    /// <summary>Every tag as <c>{id, name, color}</c>.</summary>
    public async Task<List<Dictionary<string, object?>>> TagOptionsAsync(CancellationToken ct = default) =>
        (await _db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct))
            .Select(t => new Dictionary<string, object?> { ["id"] = t.Id, ["name"] = t.Name, ["color"] = t.Color }).ToList();

    /// <summary>
    /// Staff as <c>{id, name, email}</c>, for the assignee pickers: everyone holding
    /// the <c>escalated-agent</c> or <c>escalated-admin</c> role, as escalated-laravel
    /// lists the users its agent and admin gates pass.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> AgentOptionsAsync(CancellationToken ct = default)
    {
        var ids = await _db.RoleUsers.AsNoTracking()
            .Where(ru => ru.Role != null && (ru.Role.Slug == "escalated-agent" || ru.Role.Slug == "escalated-admin"))
            .Select(ru => ru.UserId).Distinct().ToListAsync(ct);

        var agents = new List<Dictionary<string, object?>>();
        foreach (var id in ids.OrderBy(i => i, StringComparer.Ordinal))
        {
            agents.Add((await UserAsync(id, ct))!);
        }

        return agents.OrderBy(a => a["name"] as string, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tickets for a list (queue, dashboard, customer index), with requester,
    /// assignee, department, tags and the latest reply loaded in a few queries.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> TicketRowsAsync(IReadOnlyList<Ticket> tickets, CancellationToken ct = default)
    {
        var ids = tickets.Select(t => t.Id).ToList();
        var departmentIds = tickets.Where(t => t.DepartmentId is not null).Select(t => t.DepartmentId!.Value).Distinct().ToList();

        var departments = await _db.Departments.AsNoTracking().Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var tagRows = await _db.TicketTags.AsNoTracking().Where(tt => ids.Contains(tt.TicketId))
            .Join(_db.Tags.AsNoTracking(), tt => tt.TagId, tag => tag.Id, (tt, tag) => new { tt.TicketId, Tag = tag })
            .ToListAsync(ct);
        var latestIds = await _db.Replies.AsNoTracking().Where(r => ids.Contains(r.TicketId))
            .GroupBy(r => r.TicketId).Select(g => g.Max(r => r.Id)).ToListAsync(ct);
        var latest = await _db.Replies.AsNoTracking().Where(r => latestIds.Contains(r.Id)).ToDictionaryAsync(r => r.TicketId, ct);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var ticket in tickets)
        {
            var row = await TicketBaseAsync(ticket, ct);
            row["department"] = ticket.DepartmentId is { } departmentId && departments.TryGetValue(departmentId, out var department)
                ? Department(department) : null;
            row["tags"] = tagRows.Where(r => r.TicketId == ticket.Id).Select(r => Tag(r.Tag)).ToList();

            if (latest.TryGetValue(ticket.Id, out var reply))
            {
                var author = await ReplyAuthorAsync(reply, ticket, ct);
                row["latest_reply"] = new Dictionary<string, object?>
                {
                    ["id"] = reply.Id,
                    ["body"] = reply.Body,
                    ["is_internal_note"] = reply.IsInternalNote,
                    ["created_at"] = reply.CreatedAt,
                    ["author"] = author,
                };
                row["last_reply_at"] = reply.CreatedAt;
                row["last_reply_author"] = author?["name"];
            }
            else
            {
                row["latest_reply"] = null;
                row["last_reply_at"] = null;
                row["last_reply_author"] = null;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The ticket's own fields plus requester and assignee.</summary>
    public async Task<Dictionary<string, object?>> TicketBaseAsync(Ticket ticket, CancellationToken ct = default)
    {
        var requester = await RequesterAsync(ticket, ct);
        return new Dictionary<string, object?>
        {
            ["id"] = ticket.Id,
            ["reference"] = ticket.Reference,
            ["subject"] = ticket.Subject,
            ["description"] = ticket.Description,
            ["status"] = ticket.Status.ToValue(),
            ["priority"] = ticket.Priority.ToValue(),
            ["ticket_type"] = ticket.TicketType,
            ["channel"] = ticket.ChatSessions.Count > 0 ? "chat" : null,
            ["requester_type"] = ticket.RequesterType,
            ["requester_id"] = ticket.RequesterId,
            ["requester"] = requester,
            ["requester_name"] = requester?["name"],
            ["requester_email"] = requester?["email"],
            ["guest_name"] = ticket.GuestName,
            ["guest_email"] = ticket.GuestEmail,
            ["assigned_to"] = ticket.AssignedTo,
            ["assignee"] = await UserAsync(ticket.AssignedTo, ct),
            ["department_id"] = ticket.DepartmentId,
            ["sla_policy_id"] = ticket.SlaPolicyId,
            ["first_response_at"] = ticket.FirstResponseAt,
            ["first_response_due_at"] = ticket.FirstResponseDueAt,
            ["resolution_due_at"] = ticket.ResolutionDueAt,
            ["sla_first_response_breached"] = ticket.SlaFirstResponseBreached,
            ["sla_resolution_breached"] = ticket.SlaResolutionBreached,
            ["resolved_at"] = ticket.ResolvedAt,
            ["closed_at"] = ticket.ClosedAt,
            ["snoozed_until"] = ticket.SnoozedUntil,
            ["is_snoozed"] = ticket.IsSnoozed,
            ["created_at"] = ticket.CreatedAt,
            ["updated_at"] = ticket.UpdatedAt,
        };
    }

    /// <summary>
    /// The host user who opened the ticket, or the guest's name and email. Laravel
    /// leaves <c>requester</c> null for a guest; filling it keeps the requester line
    /// on the agent screens from reading blank.
    /// </summary>
    public async Task<Dictionary<string, object?>?> RequesterAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (ticket.RequesterId is not null)
        {
            return await UserAsync(ticket.RequesterId, ct);
        }

        if (ticket.GuestName is null && ticket.GuestEmail is null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["id"] = null,
            ["name"] = ticket.GuestName ?? "Guest",
            ["email"] = ticket.GuestEmail,
        };
    }

    /// <summary>
    /// The author of a reply: the host user, or the guest requester for a reply
    /// written without a user (widget and inbound email replies).
    /// </summary>
    public async Task<Dictionary<string, object?>?> ReplyAuthorAsync(Reply reply, Ticket ticket, CancellationToken ct = default)
    {
        if (reply.AuthorId is not null)
        {
            return await UserAsync(reply.AuthorId, ct);
        }

        return reply.AuthorType is "system" ? null : await RequesterAsync(ticket, ct);
    }

    public async Task<Dictionary<string, object?>> ReplyAsync(Reply reply, Ticket ticket, CancellationToken ct = default) => new()
    {
        ["id"] = reply.Id,
        ["ticket_id"] = reply.TicketId,
        ["body"] = reply.Body,
        ["type"] = reply.Type,
        ["is_internal_note"] = reply.IsInternalNote,
        ["is_pinned"] = reply.IsPinned,
        ["author_type"] = reply.AuthorType,
        ["author_id"] = reply.AuthorId,
        ["author"] = await ReplyAuthorAsync(reply, ticket, ct),
        ["attachments"] = reply.Attachments.Select(Attachment).ToList(),
        ["metadata"] = ParseJson(reply.Metadata),
        ["created_at"] = reply.CreatedAt,
        ["updated_at"] = reply.UpdatedAt,
    };

    public static Dictionary<string, object?> Attachment(Attachment attachment) => new()
    {
        ["id"] = attachment.Id,
        ["original_filename"] = attachment.Filename,
        ["filename"] = attachment.Filename,
        ["mime_type"] = attachment.MimeType,
        ["size"] = attachment.Size,
        ["url"] = attachment.Url,
        ["created_at"] = attachment.CreatedAt,
    };

    public async Task<Dictionary<string, object?>> ActivityAsync(TicketActivity activity, CancellationToken ct = default) => new()
    {
        ["id"] = activity.Id,
        ["type"] = activity.Type.ToValue(),
        ["description"] = activity.Type.Label(),
        ["properties"] = ParseJson(activity.Properties),
        ["causer_type"] = activity.CauserType,
        ["causer_id"] = activity.CauserId,
        ["causer"] = await UserAsync(activity.CauserId, ct),
        ["created_at"] = activity.CreatedAt,
        ["created_at_human"] = activity.CreatedAtHuman,
    };

    public static Dictionary<string, object?>? SatisfactionRating(SatisfactionRating? rating) => rating is null ? null : new()
    {
        ["id"] = rating.Id,
        ["rating"] = rating.Rating,
        ["comment"] = rating.Comment,
        ["created_at"] = rating.CreatedAt,
    };

    /// <summary>
    /// A ticket as its requester may see it (escalated-laravel <c>CustomerTicketPayload</c>):
    /// an allow-list of fields, public replies only, and staff reduced to a display
    /// name with no id or email. Used for the customer list rows and, with
    /// <paramref name="ticket"/>'s replies and attachments loaded, for <c>Customer/Show</c>.
    /// </summary>
    public async Task<Dictionary<string, object?>> CustomerTicketAsync(Ticket ticket, bool withThread, CancellationToken ct = default)
    {
        var requester = await RequesterAsync(ticket, ct);
        var department = ticket.Department ?? (ticket.DepartmentId is { } departmentId
            ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct)
            : null);

        var payload = new Dictionary<string, object?>
        {
            ["id"] = ticket.Id,
            ["reference"] = ticket.Reference,
            ["subject"] = ticket.Subject,
            ["description"] = ticket.Description ?? string.Empty,
            ["status"] = ticket.Status.ToValue(),
            ["priority"] = ticket.Priority.ToValue(),
            ["ticket_type"] = ticket.TicketType,
            ["channel"] = ticket.ChatSessions.Count > 0 ? "chat" : null,
            ["department"] = department is null ? null : new Dictionary<string, object?> { ["name"] = department.Name },
            ["requester"] = requester is null ? null : new Dictionary<string, object?> { ["name"] = requester["name"], ["email"] = requester["email"] },
            ["requester_name"] = requester?["name"],
            ["requester_email"] = requester?["email"],
            ["assignee"] = ticket.AssignedTo is null ? null : await StaffAsync(ticket.AssignedTo, ct),
            ["resolved_at"] = ticket.ResolvedAt,
            ["closed_at"] = ticket.ClosedAt,
            ["created_at"] = ticket.CreatedAt,
            ["updated_at"] = ticket.UpdatedAt,
        };

        var publicReplies = withThread
            ? ticket.Replies.Where(r => !r.IsInternalNote).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToList()
            : await _db.Replies.AsNoTracking().Where(r => r.TicketId == ticket.Id && !r.IsInternalNote)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(1).ToListAsync(ct);
        payload["last_reply_at"] = publicReplies.FirstOrDefault()?.CreatedAt;

        if (!withThread)
        {
            return payload;
        }

        var replies = new List<Dictionary<string, object?>>();
        foreach (var reply in publicReplies)
        {
            replies.Add(new Dictionary<string, object?>
            {
                ["id"] = reply.Id,
                ["body"] = reply.Body,
                ["is_internal_note"] = false,
                ["is_pinned"] = reply.IsPinned,
                ["author"] = await CustomerReplyAuthorAsync(reply, ticket, requester, ct),
                ["attachments"] = reply.Attachments.Select(CustomerAttachment).ToList(),
                ["created_at"] = reply.CreatedAt,
            });
        }

        payload["replies"] = replies;
        payload["attachments"] = ticket.Attachments.Select(CustomerAttachment).ToList();
        payload["satisfaction_rating"] = ticket.SatisfactionRating is { } rating
            ? new Dictionary<string, object?> { ["rating"] = rating.Rating, ["comment"] = rating.Comment }
            : null;
        return payload;
    }

    /// <summary>The requester's own replies keep their identity; staff are a display name only.</summary>
    private async Task<Dictionary<string, object?>> CustomerReplyAuthorAsync(Reply reply, Ticket ticket,
        Dictionary<string, object?>? requester, CancellationToken ct)
    {
        if (reply.AuthorId is null || (ticket.RequesterId is not null && reply.AuthorId == ticket.RequesterId))
        {
            return new Dictionary<string, object?>
            {
                ["id"] = reply.AuthorId ?? (object)0,
                ["name"] = requester?["name"] ?? "Guest",
                ["email"] = requester?["email"] ?? string.Empty,
            };
        }

        return await StaffAsync(reply.AuthorId, ct);
    }

    private async Task<Dictionary<string, object?>> StaffAsync(string userId, CancellationToken ct)
    {
        var user = await UserAsync(userId, ct);
        var name = user?["name"] as string;
        return new Dictionary<string, object?>
        {
            ["id"] = 0,
            ["name"] = string.IsNullOrEmpty(name) || name == userId ? "Support" : name,
            ["email"] = string.Empty,
        };
    }

    private static Dictionary<string, object?> CustomerAttachment(Attachment attachment) => new()
    {
        ["id"] = attachment.Id,
        ["original_filename"] = attachment.Filename,
        ["mime_type"] = attachment.MimeType,
        ["size"] = attachment.Size,
        ["url"] = attachment.Url,
    };

    /// <summary>
    /// A Laravel-style length-aware paginator: <c>data</c>, <c>current_page</c>,
    /// <c>last_page</c>, <c>per_page</c>, <c>total</c>, <c>from</c>, <c>to</c> and
    /// <c>links</c> (previous, numbered pages, next).
    /// </summary>
    public static Dictionary<string, object?> Paginator(IReadOnlyList<object?> data, int total, int page, int perPage, string path, IDictionary<string, string?> query)
    {
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, perPage)));

        string? Url(int target)
        {
            if (target < 1 || target > lastPage) return null;
            var parts = query.Where(q => q.Key != "page" && !string.IsNullOrEmpty(q.Value))
                .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value!)}")
                .Append($"page={target}");
            return $"{path}?{string.Join('&', parts)}";
        }

        var links = new List<Dictionary<string, object?>>
        {
            new() { ["url"] = Url(page - 1), ["label"] = "&laquo; Previous", ["active"] = false },
        };
        for (var i = Math.Max(1, page - 3); i <= Math.Min(lastPage, page + 3); i++)
        {
            links.Add(new Dictionary<string, object?> { ["url"] = Url(i), ["label"] = i.ToString(), ["active"] = i == page });
        }
        links.Add(new Dictionary<string, object?> { ["url"] = Url(page + 1), ["label"] = "Next &raquo;", ["active"] = false });

        return new Dictionary<string, object?>
        {
            ["data"] = data,
            ["current_page"] = page,
            ["last_page"] = lastPage,
            ["per_page"] = perPage,
            ["total"] = total,
            ["from"] = total == 0 ? null : (page - 1) * perPage + 1,
            ["to"] = total == 0 ? null : Math.Min(total, page * perPage),
            ["first_page_url"] = Url(1),
            ["last_page_url"] = Url(lastPage),
            ["prev_page_url"] = Url(page - 1),
            ["next_page_url"] = Url(page + 1),
            ["path"] = path,
            ["links"] = links,
        };
    }

    /// <summary>A JSON column as a value the page can read (object, array), or the raw text if it is not JSON.</summary>
    public static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
