using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Hubs;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Plugins;

/// <summary>
/// The SDK's <c>ctx</c>: the <c>ctx.*</c> methods an SDK plugin calls over the
/// bridge, and a native plugin through <see cref="PluginContext"/>.
///
/// <para>Every method is scoped to the plugin being served, which the caller
/// passes; a <c>plugin</c> parameter is never read. Writes act as the
/// <see cref="PluginActor"/> of the call (the signed-in user of the request being
/// served, or nobody). Ticket and reply filters accept only the contract's keys,
/// each mapped to a field here, so a filter key never becomes SQL.</para>
/// </summary>
public sealed class PluginContextHandler
{
    /// <summary>Most rows <c>ctx.tickets.query</c> and <c>ctx.replies.query</c> return.</summary>
    public const int QueryLimit = 500;

    private static readonly HashSet<string> TicketFilters = new(StringComparer.Ordinal)
    {
        "id", "reference", "status", "priority", "channel", "ticket_type", "department_id", "requester_id", "contact_id", "assigned_to",
    };

    private readonly EscalatedDbContext _db;
    private readonly PluginStore _store;
    private readonly PluginPayloads _payloads;
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginContextHandler> _logger;

    public PluginContextHandler(EscalatedDbContext db, PluginStore store, PluginPayloads payloads, IServiceProvider services,
        ILogger<PluginContextHandler> logger)
    {
        _db = db;
        _store = store;
        _payloads = payloads;
        _services = services;
        _logger = logger;
    }

    /// <param name="plugin">The name of the plugin being served.</param>
    /// <param name="actor">Who the call acts for.</param>
    /// <param name="method">For example <c>ctx.store.query</c>.</param>
    /// <param name="p">The call's parameters.</param>
    public async Task<JsonNode?> HandleAsync(string plugin, PluginActor actor, string method, JsonObject p, CancellationToken ct = default)
    {
        switch (method)
        {
            case "ctx.config.all": return await _store.ConfigAllAsync(plugin, ct);
            case "ctx.config.get": return await _store.ConfigGetAsync(plugin, Str(p, "key"), ct);
            case "ctx.config.set": await _store.ConfigSetAsync(plugin, Obj(p, "data", required: true), ct); return null;

            case "ctx.store.get": return await _store.GetAsync(plugin, Str(p, "collection"), Key(p), ct);
            case "ctx.store.set": await _store.SetAsync(plugin, Str(p, "collection"), Key(p), p["value"]?.DeepClone(), ct); return null;
            case "ctx.store.query": return await _store.QueryAsync(plugin, Str(p, "collection"), Obj(p, "filter"), Obj(p, "options"), ct);
            case "ctx.store.insert": return await _store.InsertAsync(plugin, Str(p, "collection"), Obj(p, "data", required: true), ct);
            case "ctx.store.update": return await _store.UpdateAsync(plugin, Str(p, "collection"), Key(p), Obj(p, "data", required: true), ct);
            case "ctx.store.delete": await _store.DeleteAsync(plugin, Str(p, "collection"), Key(p), ct); return null;

            case "ctx.tickets.find": return await Unscoped(() => TicketsFindAsync(p["id"], ct));
            case "ctx.tickets.query": return await Unscoped(() => TicketsQueryAsync(Obj(p, "filter"), ct));
            case "ctx.tickets.create": return await Unscoped(() => TicketsCreateAsync(Obj(p, "data", required: true), actor, ct));
            case "ctx.tickets.update": return await Unscoped(() => TicketsUpdateAsync(p["id"], Obj(p, "data", required: true), actor, ct));

            case "ctx.replies.find": return await Unscoped(() => RepliesFindAsync(p["id"], ct));
            case "ctx.replies.query": return await Unscoped(() => RepliesQueryAsync(Obj(p, "filter"), ct));
            case "ctx.replies.create": return await Unscoped(() => RepliesCreateAsync(Obj(p, "data", required: true), actor, ct));

            case "ctx.contacts.find":
                return Id(p["id"]) is { } contactId && await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct) is { } found
                    ? PluginPayloads.Contact(found) : null;
            case "ctx.contacts.findByEmail":
                var normalized = Contact.NormalizeEmail(Str(p, "email"));
                return await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Email == normalized, ct) is { } byEmail
                    ? PluginPayloads.Contact(byEmail) : null;
            case "ctx.contacts.create": return await ContactsCreateAsync(Obj(p, "data", required: true), ct);

            case "ctx.tags.all":
                return Array((await _db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)).Select(PluginPayloads.TagWithColor));
            case "ctx.tags.create": return await TagsCreateAsync(Obj(p, "data", required: true), ct);

            case "ctx.departments.all":
                return Array((await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)).Select(PluginPayloads.Department));
            case "ctx.departments.find":
                return Id(p["id"]) is { } departmentId && await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct) is { } department
                    ? PluginPayloads.Department(department) : null;

            case "ctx.agents.all": return Array((await AgentsAsync(ct)).Select(a => (JsonObject)a.DeepClone()));
            case "ctx.agents.find":
                var agentId = Scalar(p["id"]);
                return (await AgentsAsync(ct)).FirstOrDefault(a => PluginJson.String(a["id"]) == agentId)?.DeepClone();

            case "ctx.broadcast.toChannel": await BroadcastAsync(plugin, p, "channel", ct); return null;
            case "ctx.broadcast.toUser": await BroadcastAsync(plugin, p, "user", ct); return null;
            case "ctx.broadcast.toTicket": await BroadcastAsync(plugin, p, "ticket", ct); return null;

            case "ctx.log": Log(plugin, p); return null;
            case "ctx.emit":
                await _services.GetRequiredService<PluginRegistry>().EmitAsync(plugin, Str(p, "hook"), p["data"]?.DeepClone() ?? new JsonObject(), ct);
                return null;
            case "ctx.currentUser": return await CurrentUserAsync(actor, ct);

            default: throw new ArgumentException($"Unknown ctx method: {method}");
        }
    }

    // Tickets

    private async Task<JsonNode?> TicketsFindAsync(JsonNode? id, CancellationToken ct) =>
        await TicketAsync(id, ct) is { } ticket ? await _payloads.TicketAsync(ticket, ct) : null;

    private async Task<JsonNode?> TicketsQueryAsync(JsonObject filter, CancellationToken ct)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        foreach (var (key, value) in filter)
        {
            if (!TicketFilters.Contains(key)) throw new ArgumentException($"ctx.tickets.query cannot filter on \"{key}\".");
            var (values, includesNull) = Values(value);
            switch (key)
            {
                case "id":
                    var ids = values.Select(v => Id(v) ?? -1).ToList();
                    query = query.Where(t => ids.Contains(t.Id));
                    break;
                case "reference":
                    var references = values.Select(Scalar).OfType<string>().ToList();
                    query = query.Where(t => references.Contains(t.Reference));
                    break;
                case "status":
                    var statuses = values.Select(v => Status(Scalar(v))).ToList();
                    query = query.Where(t => statuses.Contains(t.Status));
                    break;
                case "priority":
                    var priorities = values.Select(v => Priority(Scalar(v))).ToList();
                    query = query.Where(t => priorities.Contains(t.Priority));
                    break;
                case "channel":
                    throw new ArgumentException("ctx.tickets.query cannot filter on \"channel\": this host does not store a ticket channel.");
                case "ticket_type":
                    var types = values.Select(Scalar).OfType<string>().ToList();
                    query = query.Where(t => (includesNull && t.TicketType == null) || types.Contains(t.TicketType!));
                    break;
                case "department_id":
                    var departments = values.Select(v => Id(v) ?? -1).ToList();
                    query = query.Where(t => (includesNull && t.DepartmentId == null) || (t.DepartmentId != null && departments.Contains(t.DepartmentId.Value)));
                    break;
                case "contact_id":
                    var contacts = values.Select(v => Id(v) ?? -1).ToList();
                    query = query.Where(t => (includesNull && t.ContactId == null) || (t.ContactId != null && contacts.Contains(t.ContactId.Value)));
                    break;
                case "requester_id":
                    var requesters = values.Select(Scalar).OfType<string>().ToList();
                    query = query.Where(t => (includesNull && t.RequesterId == null) || requesters.Contains(t.RequesterId!));
                    break;
                case "assigned_to":
                    var assignees = values.Select(Scalar).OfType<string>().ToList();
                    query = query.Where(t => (includesNull && t.AssignedTo == null) || assignees.Contains(t.AssignedTo!));
                    break;
            }
        }

        var tickets = await query.OrderBy(t => t.Id).Take(QueryLimit).ToListAsync(ct);
        var rows = new JsonArray();
        foreach (var ticket in tickets) rows.Add(await _payloads.TicketAsync(ticket, ct));
        return rows;
    }

    private async Task<JsonNode?> TicketsCreateAsync(JsonObject data, PluginActor actor, CancellationToken ct)
    {
        var subject = Text(data["subject"]) ?? Text(data["title"]) ?? throw new ArgumentException("ctx.tickets.create requires a subject.");
        var description = Text(data["description"]) ?? Text(data["body"]);
        var priority = data["priority"] is { } p ? Priority(Scalar(p)) : (TicketPriority?)null;
        var departmentId = data["department_id"] is { } d ? (await DepartmentAsync(d, ct)).Id : (int?)null;
        var ticketType = Text(data["ticket_type"]);
        if (ticketType is not null && !Ticket.ValidTypes.Contains(ticketType))
        {
            throw new ArgumentException($"Unknown ticket_type: {ticketType}");
        }

        string? metadata = data.ContainsKey("metadata") && data["metadata"] is JsonObject meta ? meta.ToJsonString() : null;

        // The requester is a contact, never a user id the plugin names.
        string? guestEmail = null, guestName = null;
        if (data["contact_id"] is { } contactNode)
        {
            var contactId = Id(contactNode) ?? throw new ArgumentException("contact_id must be a number.");
            var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct)
                ?? throw new ArgumentException("ctx.tickets.create: no such contact.");
            guestEmail = contact.Email;
            guestName = contact.Name ?? contact.Email;
        }
        else if (Text(data["requester_email"]) is { } email)
        {
            if (!IsEmail(email)) throw new ArgumentException("ctx.tickets.create: requester_email is not an email address.");
            guestEmail = email;
            guestName = Text(data["requester_name"]) ?? email;
        }

        var tickets = _services.GetRequiredService<TicketService>();
        var ticket = await tickets.CreateAsync(subject, description, priority: priority, departmentId: departmentId,
            guestName: guestName, guestEmail: guestEmail, ticketType: ticketType, ct: ct, metadata: metadata);

        if (Text(data["status"]) is { } status && Status(status) is var target && target != ticket.Status)
        {
            await tickets.ChangeStatusAsync(ticket, target, actor.UserId, ct);
        }

        return await _payloads.TicketAsync(ticket, ct);
    }

    private async Task<JsonNode?> TicketsUpdateAsync(JsonNode? id, JsonObject data, PluginActor actor, CancellationToken ct)
    {
        var ticket = await TrackedTicketAsync(id, ct) ?? throw new ArgumentException("ctx.tickets.update: no such ticket.");
        var tickets = _services.GetRequiredService<TicketService>();

        var subject = Text(data["subject"]) ?? Text(data["title"]);
        var description = Text(data["description"]) ?? Text(data["body"]);
        var ticketType = Text(data["ticket_type"]);
        if (ticketType is not null && !Ticket.ValidTypes.Contains(ticketType)) throw new ArgumentException($"Unknown ticket_type: {ticketType}");
        var metadataChanged = data.ContainsKey("metadata");
        if (metadataChanged) ticket.Metadata = data["metadata"] is JsonObject meta ? meta.ToJsonString() : null;
        if (subject is not null || description is not null || ticketType is not null || metadataChanged)
        {
            await tickets.UpdateAsync(ticket, subject, description, ticketType, ct);
        }

        if (data["priority"] is { } priorityNode && Priority(Scalar(priorityNode)) is var priority && priority != ticket.Priority)
        {
            await tickets.ChangePriorityAsync(ticket, priority, actor.UserId, ct);
        }

        if (data["department_id"] is { } departmentNode)
        {
            var department = await DepartmentAsync(departmentNode, ct);
            if (department.Id != ticket.DepartmentId) await tickets.ChangeDepartmentAsync(ticket, department.Id, actor.UserId, ct);
        }

        if (Text(data["status"]) is { } status && Status(status) is var target && target != ticket.Status)
        {
            await tickets.ChangeStatusAsync(ticket, target, actor.UserId, ct);
        }

        return await _payloads.TicketAsync(ticket, ct);
    }

    private async Task<Ticket?> TicketAsync(JsonNode? id, CancellationToken ct) =>
        Id(id) is { } ticketId ? await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, ct) : null;

    private async Task<Ticket?> TrackedTicketAsync(JsonNode? id, CancellationToken ct) =>
        Id(id) is { } ticketId ? await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct) : null;

    // Replies

    private async Task<JsonNode?> RepliesFindAsync(JsonNode? id, CancellationToken ct) =>
        Id(id) is { } replyId && await _db.Replies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == replyId, ct) is { } reply
            ? PluginPayloads.Reply(reply) : null;

    private async Task<JsonNode?> RepliesQueryAsync(JsonObject filter, CancellationToken ct)
    {
        var query = _db.Replies.AsNoTracking().AsQueryable();
        foreach (var (key, value) in filter)
        {
            var (values, _) = Values(value);
            switch (key)
            {
                case "ticket_id":
                    var ticketIds = values.Select(v => Id(v) ?? -1).ToList();
                    query = query.Where(r => ticketIds.Contains(r.TicketId));
                    break;
                case "is_internal":
                    var flags = values.Select(v => v is JsonValue b && b.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                        ? b.GetValue<bool>()
                        : throw new ArgumentException("is_internal must be true or false.")).ToList();
                    query = query.Where(r => flags.Contains(r.IsInternalNote));
                    break;
                case "type":
                    var types = values.Select(Scalar).OfType<string>().ToList();
                    query = query.Where(r => types.Contains(r.Type!));
                    break;
                default:
                    throw new ArgumentException($"ctx.replies.query cannot filter on \"{key}\".");
            }
        }

        return Array((await query.OrderBy(r => r.Id).Take(QueryLimit).ToListAsync(ct)).Select(PluginPayloads.Reply));
    }

    private async Task<JsonNode?> RepliesCreateAsync(JsonObject data, PluginActor actor, CancellationToken ct)
    {
        var ticket = await TrackedTicketAsync(data["ticket_id"], ct) ?? throw new ArgumentException("ctx.replies.create requires an existing ticket_id.");
        var body = Text(data["body"]) ?? throw new ArgumentException("ctx.replies.create requires a body.");
        var isNote = data["is_internal"] is JsonValue flag && flag.GetValueKind() == JsonValueKind.True;

        var reply = await _services.GetRequiredService<TicketService>().AddReplyAsync(ticket, body, actor.UserId, isNote: isNote, ct: ct);
        return PluginPayloads.Reply(reply);
    }

    // Contacts, tags, departments, agents

    /// <summary>Finds the contact with this email, or creates it.</summary>
    private async Task<JsonNode?> ContactsCreateAsync(JsonObject data, CancellationToken ct)
    {
        var email = Text(data["email"]);
        if (email is null || !IsEmail(email)) throw new ArgumentException("ctx.contacts.create requires an email address.");
        var name = Text(data["name"]);
        var normalized = Contact.NormalizeEmail(email);

        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Email == normalized, ct);
        if (contact is null)
        {
            contact = new Contact { Email = normalized, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _db.Contacts.Add(contact);
        }
        else if (string.IsNullOrEmpty(contact.Name) && name is not null)
        {
            contact.Name = name;
            contact.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return PluginPayloads.Contact(contact);
    }

    private async Task<JsonNode?> TagsCreateAsync(JsonObject data, CancellationToken ct)
    {
        var name = Text(data["name"]) ?? throw new ArgumentException("ctx.tags.create requires a name.");
        var slug = Tag.GenerateSlug(name);
        if (slug.Length == 0) throw new ArgumentException("ctx.tags.create: the name has no usable characters.");

        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tag is null)
        {
            tag = new Tag { Name = name, Slug = slug, Color = Text(data["color"]), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync(ct);
        }

        return PluginPayloads.TagWithColor(tag);
    }

    private async Task<Department> DepartmentAsync(JsonNode departmentId, CancellationToken ct)
    {
        var id = Id(departmentId) ?? throw new ArgumentException("department_id must be a number.");
        return await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new ArgumentException("No such department.");
    }

    /// <summary>Staff as the host's pages list them: <c>{id, name, email}</c>.</summary>
    private async Task<List<JsonObject>> AgentsAsync(CancellationToken ct) =>
        (await _services.GetRequiredService<UiPresenter>().AgentOptionsAsync(ct))
            .Select(agent => new JsonObject
            {
                ["id"] = agent.GetValueOrDefault("id")?.ToString(),
                ["name"] = agent.GetValueOrDefault("name")?.ToString(),
                ["email"] = agent.GetValueOrDefault("email")?.ToString(),
            })
            .ToList();

    // Misc

    private async Task BroadcastAsync(string plugin, JsonObject p, string target, CancellationToken ct)
    {
        var eventName = Str(p, "event");
        var data = p["data"] is JsonObject d ? (JsonObject)d.DeepClone() : new JsonObject();
        data["plugin"] = plugin;
        var payload = JsonSerializer.Deserialize<JsonElement>(data.ToJsonString());

        switch (target)
        {
            case "ticket":
                var ticketId = Id(p["ticketId"]) ?? throw new ArgumentException("ticketId is required.");
                await _services.GetRequiredService<TicketBroadcastService>().SendAsync(ticketId, $"ticket-{ticketId}", eventName, payload, ct: ct);
                break;
            case "user":
                var userId = Scalar(p["userId"]) ?? throw new ArgumentException("userId is required.");
                if (_services.GetService<IHubContext<EscalatedHub>>() is { } userHub) await userHub.Clients.User(userId).SendAsync(eventName, payload, ct);
                break;
            default:
                var channel = Str(p, "channel");
                if (_services.GetService<IHubContext<EscalatedHub>>() is { } hub) await hub.Clients.Group($"plugin.{channel}").SendAsync(eventName, payload, ct);
                break;
        }
    }

    private void Log(string plugin, JsonObject p)
    {
        var message = Scalar(p["message"]) ?? string.Empty;
        var data = p["data"]?.ToJsonString();
        var level = PluginJson.String(p["level"]) switch
        {
            "debug" => LogLevel.Debug,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information,
        };
        _logger.Log(level, "[{Plugin}] {Message} {Data}", plugin, message, data);
    }

    private async Task<JsonNode?> CurrentUserAsync(PluginActor actor, CancellationToken ct)
    {
        if (actor.UserId is null) return null;
        var user = await _services.GetRequiredService<IUserDirectory>().FindAsync(actor.UserId, ct);
        return new JsonObject { ["id"] = actor.UserId, ["name"] = user?.Name, ["email"] = user?.Email };
    }

    // Input

    private static Task<JsonNode?> Unscoped(Func<Task<JsonNode?>> work) => TicketAccessScope.WithoutVisibilityAsync(work);

    private static (List<JsonNode?> Values, bool IncludesNull) Values(JsonNode? value)
    {
        var values = value is JsonArray array ? array.ToList() : new List<JsonNode?> { value };
        return (values.Where(v => v is not null).ToList(), values.Any(v => v is null));
    }

    private static TicketStatus Status(string? value) =>
        Enum.GetValues<TicketStatus>().FirstOrDefault(s => s.ToValue() == value) is var status && status.ToValue() == value
            ? status
            : throw new ArgumentException($"Unknown status: {value}");

    private static TicketPriority Priority(string? value) =>
        Enum.GetValues<TicketPriority>().FirstOrDefault(s => s.ToValue() == value) is var priority && priority.ToValue() == value
            ? priority
            : throw new ArgumentException($"Unknown priority: {value}");

    private static bool IsEmail(string value) => MailAddress.TryCreate(value, out var address) && address.Address == value;

    private static JsonArray Array(IEnumerable<JsonObject> items) => new(items.Select(i => (JsonNode?)i).ToArray());

    private static int? Id(JsonNode? node) => PluginJson.Number(node) is { } d && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue
        ? (int)d
        : PluginJson.String(node) is { } s && int.TryParse(s, out var i) ? i : null;

    private static string? Scalar(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number => v.ToJsonString(),
            _ => null,
        }
        : null;

    private static string? Text(JsonNode? node) => Scalar(node)?.Trim() is { Length: > 0 } text ? text : null;

    private static string Str(JsonObject p, string key) =>
        PluginJson.String(p[key]) is { Length: > 0 } value ? value : throw new ArgumentException($"{key} is required.");

    private static string Key(JsonObject p) =>
        Scalar(p["key"]) is { Length: > 0 } value ? value : throw new ArgumentException("key is required.");

    private static JsonObject Obj(JsonObject p, string key, bool required = false) => p[key] switch
    {
        JsonObject obj => obj,
        null when !required => new JsonObject(),
        _ => throw new ArgumentException($"{key} must be an object."),
    };
}

/// <summary>
/// The SDK's <c>ctx</c> for a native plugin, scoped to that plugin and to the user
/// the call acts for. Every method is the same <c>ctx.*</c> call an SDK plugin makes
/// over the bridge; <see cref="CallAsync"/> reaches any of them by name
/// (<c>await ctx.CallAsync("tickets.create", new JsonObject { ["data"] = ... })</c>).
/// </summary>
public sealed class PluginContext
{
    private readonly PluginContextHandler _handler;

    public PluginContext(PluginContextHandler handler, string plugin, PluginActor actor)
    {
        _handler = handler;
        PluginName = plugin;
        Actor = actor;
    }

    public string PluginName { get; }
    public PluginActor Actor { get; }

    public Task<JsonNode?> CallAsync(string method, JsonObject? parameters = null, CancellationToken ct = default) =>
        _handler.HandleAsync(PluginName, Actor, method.StartsWith("ctx.", StringComparison.Ordinal) ? method : "ctx." + method,
            parameters ?? new JsonObject(), ct);

    public async Task<JsonObject> ConfigAllAsync(CancellationToken ct = default) =>
        await CallAsync("config.all", ct: ct) as JsonObject ?? new JsonObject();

    public Task<JsonNode?> ConfigGetAsync(string key, CancellationToken ct = default) =>
        CallAsync("config.get", new JsonObject { ["key"] = key }, ct);

    public Task ConfigSetAsync(JsonObject data, CancellationToken ct = default) =>
        CallAsync("config.set", new JsonObject { ["data"] = data.DeepClone() }, ct);

    public Task<JsonNode?> StoreGetAsync(string collection, string key, CancellationToken ct = default) =>
        CallAsync("store.get", new JsonObject { ["collection"] = collection, ["key"] = key }, ct);

    public Task StoreSetAsync(string collection, string key, JsonNode? value, CancellationToken ct = default) =>
        CallAsync("store.set", new JsonObject { ["collection"] = collection, ["key"] = key, ["value"] = value?.DeepClone() }, ct);

    public async Task<JsonArray> StoreQueryAsync(string collection, JsonObject? filter = null, JsonObject? options = null, CancellationToken ct = default) =>
        await CallAsync("store.query", new JsonObject
        {
            ["collection"] = collection,
            ["filter"] = filter?.DeepClone() ?? new JsonObject(),
            ["options"] = options?.DeepClone() ?? new JsonObject(),
        }, ct) as JsonArray ?? new JsonArray();

    public async Task<JsonObject> StoreInsertAsync(string collection, JsonObject data, CancellationToken ct = default) =>
        (JsonObject)(await CallAsync("store.insert", new JsonObject { ["collection"] = collection, ["data"] = data.DeepClone() }, ct))!;

    public async Task<JsonObject> StoreUpdateAsync(string collection, string key, JsonObject data, CancellationToken ct = default) =>
        (JsonObject)(await CallAsync("store.update", new JsonObject { ["collection"] = collection, ["key"] = key, ["data"] = data.DeepClone() }, ct))!;

    public Task StoreDeleteAsync(string collection, string key, CancellationToken ct = default) =>
        CallAsync("store.delete", new JsonObject { ["collection"] = collection, ["key"] = key }, ct);

    /// <summary>Fires an action of the plugin's choosing to every active plugin (delivered after this call returns).</summary>
    public Task EmitAsync(string hook, JsonNode? data = null, CancellationToken ct = default) =>
        CallAsync("emit", new JsonObject { ["hook"] = hook, ["data"] = data?.DeepClone() ?? new JsonObject() }, ct);

    public Task LogAsync(string level, string message, JsonNode? data = null, CancellationToken ct = default) =>
        CallAsync("log", new JsonObject { ["level"] = level, ["message"] = message, ["data"] = data?.DeepClone() }, ct);

    /// <summary><c>{id, name, email}</c> of the user the call acts for, or null.</summary>
    public async Task<JsonObject?> CurrentUserAsync(CancellationToken ct = default) =>
        await CallAsync("currentUser", ct: ct) as JsonObject;
}
