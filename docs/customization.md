# Customization

## Embedding the admin in your own admin panel (optional)

Escalated's admin runs standalone at `/support/admin` with its own sidebar; that
is the default and needs nothing. If your app already has an admin panel, you
can instead mount Escalated's admin screens inside it: under your URL prefix and
authorization policies, rendered in your Vue + Inertia layout or your Razor
layout (or an allow-listed iframe), with its menu listed in your own sidebar.
Nothing changes unless you turn it on:

```csharp
builder.Services.Configure<EscalatedOptions>(options => options.EmbedAdmin(
    prefix: "admin/support",
    policies: new[] { "BackOfficeAdmin" },
    layout: "Layouts/AdminLayout"));
```

Escalated's admin policy, two-factor and route permissions stay in place. See
[docs/embedding-admin.md](embedding-admin.md) for the settings
(`Escalated:Ui:Embed`, `ESCALATED_EMBED*`), the three host setups and security.

## Frontend Integration

Escalated ships a Vue component library and default pages via the [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) npm package. Integrate with Inertia.js for seamless SPA rendering inside your existing layout.

```bash
npm install @escalated-dev/escalated
```

This package serves those pages over Inertia once `Escalated:Ui:Enabled` is `true`. It is **off by default** (escalated-laravel defaults it on), so an API-only host is unchanged. See [docs/inertia-ui.md](inertia-ui.md) for the settings, the root view, the shared props and the pages served.

## Real-time Updates

Enable SignalR for live ticket updates:

```json
{
  "Escalated": {
    "EnableRealTime": true
  }
}
```

```csharp
// Program.cs
app.MapHub<EscalatedHub>("/support/hub");
```

Clients join ticket-specific groups to receive updates:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Localization

Escalated for ASP.NET Core consumes its translation catalog from the
central [`Escalated.Locale`](https://www.nuget.org/packages/Escalated.Locale)
NuGet package. `AddEscalated()` registers a chained
`IStringLocalizer` stack that resolves keys in this order:

1. **Plugin-local overrides** -- `.resx` files under
   `src/Escalated/Resources/Overrides/`. Empty by default.
2. **Optional shared provider** -- a host that installs `Escalated.Locale`
   can supply its `LocaleProvider` localizer.
3. **Embedded catalog** -- the JSON files under `Resources/locales/` are
   shipped inside the Escalated assembly and work without the optional package.

Lookup uses `CurrentUICulture`, its parent cultures, and then English. For
example, `fr-CA` reads the French catalog. Missing keys return the key with
`ResourceNotFound` set. `GetAllStrings(true)` merges this fallback chain;
`GetAllStrings(false)` reads only the selected culture's own catalog, with
English used for invariant culture.

Catalog keys use dotted paths, such as `nav.admin`. Shared messages use named
placeholders; pass a dictionary to name their values explicitly:

```csharp
localizer["bulk.updated", new Dictionary<string, object?> { ["count"] = 3 }];
```

Positional arguments use the placeholder order in the English message. `.resx`
overrides retain standard .NET composite formatting. Formatting follows
`CurrentCulture`; unformatted lookups retain the catalog's placeholders.

Changes to the vendored catalogs ship with a new plugin build. Drop a resx into
`Resources/Overrides/` for wording specific to your plugin or host resource
type. See `src/Escalated/Resources/Overrides/README.md` for naming conventions.

## Ticket subjects

A ticket has a **requester** (the person who raised it) and a **subject line**
(free text). Sometimes a ticket is also *about* one or more host-app entities —
a Project, a Customer, an asset — that aren't people. Attach them as ticket
**subjects** so agents see what the ticket concerns and can jump straight to it
in your app.

Implement `ITicketSubject` on any attachable host model:

```csharp
public class Project : ITicketSubject
{
    public string TicketSubjectTitle() => Name;

    public string? TicketSubjectSubtitle() => Customer == null ? null : $"Project · {Customer.Name}";

    public string? TicketSubjectUrl() => $"/projects/{Id}";

    public string? TicketSubjectColor() => "#2563eb";

    public string? TicketSubjectIcon() => "folder";
}
```

Register a resolver so Escalated can load host models by stored `(type, id)`:

```csharp
builder.Services.AddSingleton<ITicketSubjectResolver, MyTicketSubjectResolver>();
```

Attach, detach, or sync subjects programmatically via `TicketSubjectService`, or
through the admin API when types are allowlisted in config:

```json
"Escalated": {
  "TicketSubjects": {
    "Types": [
      "App.Models.Project",
      "App.Models.Customer"
    ]
  }
}
```

Each attached subject is serialized on ticket responses as
`{ type, id, role, title, subtitle, url, color, icon, missing }`. When the
resolver cannot find a host record, `missing` is `true` and `title` falls back
to `TypeName#id`. `subject_id` is stored as a string, so integer, UUID, or
string-keyed host models all work.

Admin endpoints:

```
POST   /support/admin/tickets/{id}/subjects       { "type", "id", "role?" }
DELETE /support/admin/tickets/{ticketId}/subjects/{subjectLinkId}
```

Programmatic attach works for any type when the allowlist is empty; the admin
API only accepts allowlisted types.

## Custom Ticket Actions

Host applications can add custom buttons to the agent ticket screen and handle
clicks via the event dispatcher. Register actions in configuration:

```json
{
  "Escalated": {
    "TicketActions": [
      {
        "Key": "sync-crm",
        "Label": "Sync CRM",
        "Variant": "primary",
        "Confirmation": "Sync this ticket to the CRM?",
        "Metadata": { "icon": "refresh-cw" }
      }
    ]
  }
}
```

Visible actions are exposed on the agent ticket response as `custom_actions`
(each with a `url` and `method`). Triggering one
(`POST /support/agent/tickets/{id}/actions/{action}`) validates the action is
visible (404) and enabled (403), records an internal note for auditability, and
dispatches `TicketCustomActionTriggeredEvent` to your `IEscalatedEventDispatcher`:

```csharp
if (@event is TicketCustomActionTriggeredEvent triggered && triggered.Action == "sync-crm")
{
    // triggered.Ticket, triggered.UserId, triggered.Payload, triggered.Metadata
}
```

For dynamic per-ticket visibility, register your own `ITicketActionRegistry`.

## Plugins

Admin > Plugins (`Escalated/Admin/Plugins/Index`) lists native plugins
(`IEscalatedPlugin` classes registered with `services.AddEscalatedPlugin<T>()`)
and, with `Escalated:Plugins:Runtime:Enabled`, SDK plugins (`@escalated-dev/plugin-sdk`)
run by `@escalated-dev/plugin-runtime` over JSON-RPC on stdio. Active plugins get
the canonical events, filters, admin pages (`Escalated/Plugin/Page`), endpoints
under `{prefix}/api/plugins/{slug}`, webhooks under `{prefix}/webhooks/plugins/{slug}`,
a store and config, and menus, slots and widgets on the shared screens. See
[docs/plugins.md](plugins.md).

## Configurable ticket access and routing

Opt-in department visibility, host-resolved account identity, account/type rule conditions and targeted SLA policies are described in [Ticket access and routing](ticket-access-and-routing.md). All restrictions are disabled by default.
