# Plugins

Escalated runs two kinds of plugin, as the shared plugin contract
(escalated-developer-context `domain-model/plugin-contract.md`) describes:

- **Native plugins**: .NET classes implementing `IEscalatedPlugin`, registered in
  the host's container and run in the host process. Listed as `registered`.
- **SDK plugins**: TypeScript plugins built with `@escalated-dev/plugin-sdk`
  (`definePlugin()`), run by `@escalated-dev/plugin-runtime` in a Node subprocess
  and reached over JSON-RPC on stdio. Off by default. Listed as `composer`
  (managed by npm).

Both share one registry, one activation table (`escalated_plugins`), one store
(`escalated_plugin_store`) and one admin screen. Nothing a plugin declares runs
until an admin activates it.

## Native plugins

```csharp
using System.Text.Json.Nodes;
using Escalated.Plugins;

public sealed class CrmPlugin : EscalatedPlugin
{
    public override PluginManifest Manifest { get; } = new()
    {
        Name = "@acme/escalated-plugin-crm",      // slug: acme--escalated-plugin-crm
        Version = "1.0.0",
        Pages = new[] { new PluginPage { Route = "dashboard", Component = "CrmDashboard", Menu = new PluginMenu { Label = "CRM" } } },
        Endpoints = new[] { new PluginRoute("GET", "/CrmDashboard"), new PluginRoute("POST", "/contacts/:id", "crm.manage") },
        Webhooks = new[] { new PluginRoute("POST", "/inbound") },
        FilterHooks = new[] { new PluginFilterHook("import.adapters", 20) },
    };

    public override async Task HandleActionAsync(string hook, JsonNode? payload, PluginContext ctx, CancellationToken ct)
    {
        if (hook == "ticket.created")
            await ctx.StoreInsertAsync("seen", new JsonObject { ["ticket"] = payload!["id"]!.DeepClone() }, ct);
    }

    public override Task<object?> HandleEndpointAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct) =>
        Task.FromResult<object?>(request.Path switch
        {
            "/CrmDashboard" => new { open_deals = 4 },          // the page's props
            _ => PluginHttpResponse.Json(new { id = request.Params["id"] }, 201),
        });
}
```

```csharp
builder.Services.AddEscalated();
builder.Services.AddEscalatedPlugin<CrmPlugin>();   // scoped IEscalatedPlugin
```

Extend `EscalatedPlugin` and override what the plugin needs: `OnActivateAsync`,
`OnDeactivateAsync`, `HandleActionAsync`, `ApplyFilterAsync`,
`HandleEndpointAsync`, `HandleWebhookAsync`. A native plugin without
`ActionHooks` in its manifest is offered every action; list them to narrow it.

`ctx` (`PluginContext`) is the SDK's `PluginContext`: `ConfigAllAsync`,
`ConfigGetAsync`, `ConfigSetAsync` (merges), `StoreGet/Set/Query/Insert/Update/DeleteAsync`,
`EmitAsync`, `LogAsync`, `CurrentUserAsync`, and `CallAsync("tickets.create", ...)`
for any `ctx.*` method by name.

Two plugins with one slug fail the host at startup, naming both.

## SDK plugins (the runtime)

```json
{
  "Escalated": {
    "Plugins": {
      "Runtime": {
        "Enabled": true,
        "Command": "node",
        "Arguments": [ "node_modules/@escalated-dev/plugin-runtime/build/bin/escalated-plugins.js" ],
        "WorkingDirectory": null
      }
    }
  }
}
```

Install the runtime and the plugins with npm in the working directory (the
host's content root by default): `npm install @escalated-dev/plugin-runtime
@escalated-dev/plugin-slack`. The runtime loads `node_modules/@escalated-dev/plugin-*`
and `plugins/*`.

The bridge follows the contract's "SDK bridge":

- Started on first use; handshake `{protocol_version: "1.0", host: "dotnet"}`,
  then `manifest`. Endpoints and webhooks need HTTP contract 1.
- `activate`/`deactivate` on the admin's transitions; `action` and `filter` carry
  `plugins: [active names]`. Nothing is sent for an inactive plugin.
- The runtime's `ctx.*` calls are answered while the host waits for its own
  call, for the plugin that call serves; a `plugin` parameter naming another
  plugin is refused (or ignored when one plugin is served).
- Timeouts: handshake and manifest 15 s, action 30 s, filter 5 s, endpoint 30 s,
  webhook 60 s. A runtime that fails is restarted on next use with exponential
  backoff (5 s doubling, capped at 5 minutes). Meanwhile actions are dropped
  with a warning, filters return their input, endpoints and webhooks answer 503.
- A runtime that stamps its `ctx` calls with the id of the host call they
  serve (`host_call_id`; runtimes after 0.2.0 say so in the handshake) is
  called concurrently: each `ctx` call acts for the plugin and the signed-in
  user of the call it names, whatever else is in flight, and a `ctx` handler may
  call the runtime itself. A timeout ends only that call.
- An older runtime is called one call at a time. A `ctx` handler cannot then
  call the runtime itself; what it causes (events, `ctx.emit`) is delivered
  afterwards.

Without Node, or with the runtime off, the host starts and serves requests as
before; SDK plugins are just not listed.

## Admin screen

All behind the `escalated-admin` policy, under `{prefix}/admin/plugins`:

| Method | Path | Route name |
|---|---|---|
| GET | `/admin/plugins` | `escalated.admin.plugins.index` (`Escalated/Admin/Plugins/Index`) |
| POST | `/admin/plugins/{slug}/activate` | `escalated.admin.plugins.activate` |
| POST | `/admin/plugins/{slug}/deactivate` | `escalated.admin.plugins.deactivate` |
| DELETE | `/admin/plugins/{slug}` | `escalated.admin.plugins.destroy` |

`can_upload` is `false`: plugins are installed by changing the build (a NuGet
reference and `AddEscalatedPlugin`, or an npm package), so there is no upload
and `POST /admin/plugins/upload` is a 404. Each POST and DELETE redirects to the
index with 303 and a `success` or `error` flash; JSON clients get 200 or 404.

- **Activate**: the row is upserted active, then `OnActivateAsync` (runtime:
  `activate`), then `plugin.activated` `{plugin, slug}`.
- **Deactivate**: `plugin.deactivated` while still active, `OnDeactivateAsync`,
  then the row.
- **Uninstall**: refused for SDK plugins (npm removes them). Otherwise
  `plugin.uninstalling`, deactivate, and the row is deleted; the plugin stays
  listed (its code is in the build) and its store rows are kept.

Transitions are idempotent, and a callback that throws is logged without undoing
the transition. Lifecycle actions go to every active plugin, the subject
included. The index never shows a plugin's config.

## Pages, endpoints and webhooks

| Kind | Route | Guard |
|---|---|---|
| Page | `GET {prefix}/admin/plugins/{slug}/{route}` | Admin, plus the page's `capability` (403) |
| Endpoint | `{METHOD} {prefix}/api/plugins/{slug}{path}` | Admin, plus `capability` (403) |
| Webhook | `{METHOD} {prefix}/webhooks/plugins/{slug}{path}` | None; the plugin authenticates the sender |

- A page renders `Escalated/Plugin/Page` with `plugin` (the name), `component`,
  `layout` (default `admin`) and `props`: the plugin's own `GET /{component}`
  answer, or `{}` when it has none or it fails.
- Pages share one route; the contract's per-page route names
  (`escalated.plugin.{slug}.page.{route}`) are not registered, since nothing
  calls them and menu items carry the URL.
- Manifest paths may hold `:param` segments; handlers get `params`, `query`,
  `body`, `headers`, `rawBodyBase64` and `clientIp` (HTTP contract 1), and are
  dispatched on the manifest path. `Cookie`, `Authorization`, `X-XSRF-TOKEN`,
  `X-CSRF-TOKEN` and `Proxy-*` are never passed on.
- A state-changing endpoint call (not GET, HEAD or OPTIONS) must carry
  `X-Requested-With` or `X-Inertia`, which page scripts send and a cross-site
  form cannot.
- A plain return value is JSON with 200; `PluginHttpResponse` (the SDK's
  `httpResponse()`) sets status, format and headers. A malformed envelope is a
  502, a handler that throws a 500 with a generic message. Inactive or unknown
  plugins and undeclared paths are a 404.

A **capability** is an ASP.NET Core authorization policy when the host has
registered one by that name, and otherwise an Escalated permission slug the user
holds through their roles.

## Hooks

**Actions.** The canonical events of ADR 2026-10-21 (`ticket.created`,
`reply.created`, `chat.started`, ...) reach active plugins under exactly those
names, never the legacy ones, after the change is saved. Payloads:

| Events | Payload |
|---|---|
| `ticket.*`, `sla.*` | The ticket flat plus `ticket`, and the event's extras: `old_status`/`new_status`, `agent_id`, `previous_agent_id`, `old_priority`/`new_priority`, `old_department_id`/`new_department_id`, `tag` (`{id, name, slug}`), `type` |
| `reply.created`, `note.created` | The reply flat plus `reply` and `ticket` |
| `chat.*` | `{session: {session_id, ticket_id, status, agent_id, department_id, ...extras}}`, never the visitor's token or email |

The ticket object carries `id`, `reference`, `subject`, `title`, `description`,
`status`, `priority`, `channel` (always null: tickets here have no channel),
`ticket_type`, `department_id`, `assigned_to`, `requester_id`, `contact_id`,
`metadata`, `tags`, `created_at`, `updated_at`.

Actions are delivered in the background, in order, from a fresh scope acting as
no user, so a slow or failing plugin never fails or delays the request. What
plugins do in response (writes through `ctx`, `ctx.emit`) is delivered too, at
most five levels deep. `ctx.emit` drops names that are canonical events, their
legacy names, or lifecycle actions.

**Filters.** `PluginRegistry.ApplyFilterAsync(hook, value)` runs active plugins'
filters by ascending priority (default 10); one that throws or takes over 5 s is
skipped. SDK filters run in one runtime call, at the place of the lowest SDK
priority. This version applies no filter of its own; hosts and plugins may.

## The plugin context

Every `ctx.*` method of the SDK is served, scoped to the plugin being served
(`PluginContextHandler`):

- `config.all/get/set` (the `__config__` row; `set` merges) and
  `store.get/set/query/insert/update/delete`. `query` filters in .NET:
  equality (an array field matches when it holds the value),
  `$gt $gte $lt $lte $ne $in $nin` on dotted field names matching
  `^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$`, and `limit`, `orderBy`, `order`.
  Any other name or operator is an error; nothing reaches SQL.
- `tickets.find/query/create/update` and `replies.find/query/create` go through
  `TicketService`, so events, activity and SLAs apply. `create` takes
  `subject`/`title`, `body`/`description`, `priority`, `status`,
  `department_id`, `ticket_type`, `metadata`, and `contact_id` or
  `requester_email`/`requester_name` (a contact; `channel` is accepted and not
  stored). `query` filters only on the contract's keys (`channel` is an error
  here) and returns at most 500 rows. Writes act as the signed-in user of the
  request being served, or nobody during event delivery.
- `contacts.find/findByEmail/create` (find or create), `tags.all/create`,
  `departments.all/find`, `agents.all/find` (users with the agent or admin role,
  named through `IUserDirectory`).
- `broadcast.toTicket` (the ticket's SignalR room), `toUser`, `toChannel`
  (`plugin.{channel}`); a no-op without SignalR.
- `log`, `emit`, `currentUser`.

## UI extensions

Every page shares `escalated.plugins`, built from active plugins: `menuItems`
(from `pages[].menu`; `admin` items for admins only), `pageComponents`
(`"{page}.{slot}"` to components) and `dashboardWidgets`. Items with a
`capability` the viewer lacks are left out. With no active plugin it is `{}`.

## Tables

`escalated_plugins` (slug up to 255, unique) and `escalated_plugin_store`
(`plugin` 191, `collection` 100, `key` 191, `data` as `jsonb` on PostgreSQL and
text elsewhere, indexed on `(plugin, collection, key)`), created by the
`PluginSystem` migration for SQLite, PostgreSQL and SQL Server.

## Security

Native plugins are trusted host code. SDK plugins run outside the host process
and its database connection, but share one Node process. Store scoping by
plugin name keeps plugins' data apart; it is not a security boundary between
them. Plugin config may hold secrets and is never sent to the admin screen or
the shared props.
