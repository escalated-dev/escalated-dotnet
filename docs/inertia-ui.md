# The Inertia UI

Escalated's screens are the shared Vue 3 pages in
[`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Every
Escalated backend serves them over [Inertia](https://inertiajs.com): the server
picks a page component and its props, and the page renders them. This package
does the same once you turn the UI on.

## Turning it on

```json
{
  "Escalated": {
    "Ui": {
      "Enabled": true,
      "Version": "2026-10-21.1",
      "HeadHtml": "<script type=\"module\" src=\"/build/escalated.js\"></script>"
    }
  }
}
```

`Ui.Enabled` defaults to **false**. escalated-laravel defaults `ui.enabled` to
true; this package shipped as a JSON API first, and turning the pages on changes
what a browser gets on routes the API already answers (below), so it stays an
opt-in. With it off the page routes do not exist (404) and nothing else changes.

| Setting | Default | What it does |
|---|---|---|
| `Ui:Enabled` | `false` | Serve the pages. |
| `Ui:Version` | none | Your frontend build's version. An Inertia visit carrying another one gets `409` with `X-Inertia-Location`, and the browser reloads. |
| `Ui:Title` | `Support` | The `<title>` of the default root view. |
| `Ui:HeadHtml` | none | Written into `<head>` as is: your bundle's script and stylesheet tags. |
| `Ui:BodyHtml` | none | Written at the end of `<body>` as is. |
| `Ui:RootElementId` | `app` | The element the app mounts on. |
| `Ui:RouteHelper` | `true` | Install the bundled `window.route()` helper (below). |

## Your frontend bundle

Build an app that mounts the Escalated pages, the way a Laravel host does:

```js
import { createApp, h } from 'vue';
import { createInertiaApp } from '@inertiajs/vue3';
import { EscalatedPlugin, resolveEscalatedPage } from '@escalated-dev/escalated';

createInertiaApp({
    resolve: (name) => resolveEscalatedPage(name),
    setup({ el, App, props, plugin }) {
        createApp({ render: () => h(App, props) }).use(plugin).use(EscalatedPlugin).mount(el);
    },
});
```

Serve the built files from your app and point `Ui:HeadHtml` at them.

## The root view

A first visit (a browser navigation) gets an HTML document holding the page
object, as Laravel's root Blade view does. The default view writes `HeadHtml`,
the route helper, the page object both as Inertia 3's
`<script data-page="app" type="application/json">` and as Inertia 1/2's
`<div id="app" data-page="…">`, and `BodyHtml`.

To render the page inside your own layout, register an `IEscalatedUiRootView`:

```csharp
builder.Services.AddScoped<IEscalatedUiRootView, MyLayout>();

public sealed class MyLayout : IEscalatedUiRootView
{
    public Task<string> RenderAsync(HttpContext context, InertiaPage page, string pageJson, CancellationToken ct = default)
    {
        // pageJson is the serialized page object. Put it in the element your app
        // mounts on, HTML-encoded inside an attribute, or in a script element
        // with "<" escaped.
    }
}
```

## The protocol

- An Inertia visit (`X-Inertia: true`) gets the page object as JSON, with
  `X-Inertia: true` and `Vary: X-Inertia`.
- A GET whose `X-Inertia-Version` differs from `Ui:Version` gets `409` and
  `X-Inertia-Location`.
- Partial reloads (`X-Inertia-Partial-Component` with `X-Inertia-Partial-Data`
  or `X-Inertia-Partial-Except`) get only the props asked for, plus `errors`.
- A form post answers with a redirect: `303` after any non-GET, so the browser
  follows it with a GET. Most go back to the page that posted (`Referer`, when it
  is on the same host).

### Routes the JSON API also answers

Some page routes share a path with a JSON API action: `GET support/agent/tickets`
(the ticket queue page, and the API's ticket list), `GET support/admin/tickets`
(likewise), `POST support/agent/tickets/bulk`, `support/admin/settings`, `GET`/`PUT support/admin/settings/public-tickets`, and the departments, tags,
SLA policy, escalation rule, canned response, macro, webhook, users, roles, API token
and workflows admin routes. With the UI on, a request reaches the page when it
is an Inertia visit or a GET whose `Accept` names `text/html`; every other
request (an API client sending `Accept: application/json`, or no `Accept`)
still reaches the API. Ticket pages are addressed by reference
(`support/agent/tickets/ESC-00042`), so they never meet the API's
`support/agent/tickets/{id}` routes.

## Shared props

Every page gets, as escalated-laravel shares them:

- `auth.user`: `{id, name, email}` of the signed-in user (from your
  `IUserDirectory`, falling back to the `name` and `email` claims), or null.
- `escalated`: `prefix`, `is_agent`, `is_admin`, `permissions`,
  `features.newsletters`, `guest_tickets_enabled`, `show_powered_by`,
  `knowledge_base_enabled`, `knowledge_base_public`,
  `knowledge_base_feedback_enabled`, `agent_type`, `broadcasting.channel_prefix`.
- `flash`: `{success, error}` from the previous request's redirect.
- `errors`: the field errors of a failed form post, as `{field: message}`.

Flash messages and errors travel in one short-lived, data-protected cookie
(`escalated_flash`), so no session middleware is needed. Behind several
instances, share the data-protection key ring as you would for cookie
authentication.

Register more `IEscalatedUiSharedProps` implementations to share your own data;
they all run, in registration order, before the page's own props.

## Props

Props are written exactly as the pages read them: top-level names as each page
declares them (`recentTickets`, `is_following`), and everything inside them
snake_case (`created_at`, `requester.name`, `latest_reply.author`), with times in
UTC ISO 8601. This applies to page props only; the JSON API keeps its camelCase.

The newsletter screens render through the same renderer. With the UI off they
answer `{component, props}` JSON as before, now with snake_case nested props
(`from_name`, `summary_total`), which is what their pages read.

## `route()`

The pages call Ziggy's `route(name, params)`, which Laravel hosts get from Ziggy.
The default root view installs a small helper with escalated-laravel's route
names and this package's paths (`UiRoutes.Defaults`); add or override names with
`services.Configure<UiRouteOptions>(o => o.Routes["name"] = "{prefix}/path/{id}")`.
A name it does not know returns `#` and logs a console warning instead of
throwing. A host that ships Ziggy-compatible routes of its own sets
`Ui:RouteHelper` to false.

## Pages served

| Page | Route |
|---|---|
| `Escalated/Agent/Dashboard` | `GET support/agent` |
| `Escalated/Agent/TicketIndex` | `GET support/agent/tickets` |
| `Escalated/Agent/TicketShow` | `GET support/agent/tickets/{reference}` |
| `Escalated/Customer/Index` | `GET support` |
| `Escalated/Customer/Create` | `GET support/create` |
| `Escalated/Customer/Show` | `GET support/{reference}` |
| `Escalated/Guest/Create` | `GET support/guest/create` |
| `Escalated/Guest/Show` | `GET support/guest/{token}` |
| `Escalated/Customer/KnowledgeBase/Index` | `GET support/kb` |
| `Escalated/Customer/KnowledgeBase/Article` | `GET support/kb/{slug}` |
| `Escalated/Admin/Tickets/Index` | `GET support/admin/tickets` |
| `Escalated/Admin/Tickets/Show` | `GET support/admin/tickets/{reference}` |
| `Escalated/Admin/Settings` | `GET support/admin/settings` |
| `Escalated/Admin/Settings/CsatSettings` | `GET support/admin/settings/csat` |
| `Escalated/Admin/Settings/DataRetention` | `GET support/admin/settings/data-retention` |
| `Escalated/Admin/Settings/DatabaseConnection` | `GET support/admin/settings/database` |
| `Escalated/Admin/Settings/EmailSettings` | `GET support/admin/settings/email` |
| `Escalated/Admin/Settings/PublicTickets` | `GET support/admin/settings/public-tickets` |
| `Escalated/Admin/Departments/Index` | `GET support/admin/departments` |
| `Escalated/Admin/Departments/Form` | `GET support/admin/departments/create`, `GET support/admin/departments/{id}/edit` |
| `Escalated/Admin/Tags/Index` | `GET support/admin/tags` |
| `Escalated/Admin/SlaPolicies/Index` | `GET support/admin/sla-policies` |
| `Escalated/Admin/SlaPolicies/Form` | `GET support/admin/sla-policies/create`, `GET support/admin/sla-policies/{id}/edit` |
| `Escalated/Admin/EscalationRules/Index` | `GET support/admin/escalation-rules` |
| `Escalated/Admin/EscalationRules/Form` | `GET support/admin/escalation-rules/create`, `GET support/admin/escalation-rules/{id}/edit` |
| `Escalated/Admin/CannedResponses/Index` | `GET support/admin/canned-responses` |
| `Escalated/Admin/Macros/Index` | `GET support/admin/macros` |
| `Escalated/Admin/Statuses/Index` | `GET support/admin/statuses` |
| `Escalated/Admin/Statuses/Form` | `GET support/admin/statuses/create`, `GET support/admin/statuses/{id}/edit` |
| `Escalated/Admin/Webhooks/Index` | `GET support/admin/webhooks` |
| `Escalated/Admin/Webhooks/Form` | `GET support/admin/webhooks/create`, `GET support/admin/webhooks/{id}/edit` |
| `Escalated/Admin/Webhooks/DeliveryLog` | `GET support/admin/webhooks/{id}/deliveries` |
| `Escalated/Admin/Users/Index` | `GET support/admin/users` |
| `Escalated/Admin/Roles/Index` | `GET support/admin/roles` |
| `Escalated/Admin/Roles/Form` | `GET support/admin/roles/create`, `GET support/admin/roles/{id}/edit` |
| `Escalated/Admin/ApiTokens/Index` | `GET support/admin/api-tokens` |
| `Escalated/Admin/KnowledgeBase/Articles/Index` | `GET support/admin/kb-articles` |
| `Escalated/Admin/KnowledgeBase/Articles/Form` | `GET support/admin/kb-articles/create`, `GET support/admin/kb-articles/{id}/edit` |
| `Escalated/Admin/KnowledgeBase/Categories/Index` | `GET support/admin/kb-categories` |
| `Escalated/Admin/Reports` | `GET support/admin/reports` |
| `Escalated/Admin/Reports/Dashboard` | `GET support/admin/reports/dashboard` |
| `Escalated/Admin/Reports/AgentMetrics` | `GET support/admin/reports/agents` |
| `Escalated/Admin/Reports/SlaReport` | `GET support/admin/reports/sla` |
| `Escalated/Admin/Reports/CsatReport` | `GET support/admin/reports/csat` |
| `Escalated/Admin/Reports/ResponseTimes` | `GET support/admin/reports/response-times` (or `/frt`) |
| `Escalated/Admin/Reports/ResolutionTimes` | `GET support/admin/reports/resolution-times` (or `/resolution`) |
| `Escalated/Admin/Reports/AgentRanking` | `GET support/admin/reports/agent-ranking` |
| `Escalated/Admin/Reports/SlaTrends` | `GET support/admin/reports/sla-trends` |
| `Escalated/Admin/Reports/Cohorts` | `GET support/admin/reports/cohorts` |
| `Escalated/Admin/Reports/Comparison` | `GET support/admin/reports/comparison` |
| `Escalated/Admin/Workflows/Index` | `GET support/admin/workflows` |
| `Escalated/Admin/Workflows/Form` | `GET support/admin/workflows/create`, `GET support/admin/workflows/{id}` |
| `Escalated/Admin/Workflows/Logs` | `GET support/admin/workflows/logs` (`?workflow=id`), `GET support/admin/workflows/{id}/logs` |

Agent form posts (`support/agent/tickets/{reference}/…`): `reply`, `note` (files
in `attachments[]` are stored through `IAttachmentStorage`, within
`Attachments:MaxSizeKb` and `MaxPerReply`), `assign`, `status`, `priority`,
`tags` (sets the ticket's tags to `tag_ids`), `department`, `macro`, `follow`
(toggles), `replies/{reply}/pin` (pins or unpins an internal note), `presence`
(answers `{viewers: [{id, name}]}`, the other staff seen on the ticket in the
last minute, tracked in process) and `actions/{action}`, plus `POST
support/agent/tickets/bulk`.

The admin ticket pages take the same posts under `support/admin/tickets/…`
(no custom actions), plus `snooze` (`snoozed_until`, a future time; a time
without a zone is read as UTC) and `unsnooze`. Their `agents` prop lists the
holders of the `escalated-agent` and `escalated-admin` roles, and `savedViews`
the signed-in admin's saved views plus shared ones. The views dialogs call the
saved-view API (`support/admin/saved-views`), which now also takes `filters` as
a JSON object and `is_shared`.

Customer form posts: `POST support` (open a ticket; `subject`, `description`,
`priority`, `department_id`, `ticket_type`, `attachments[]`), and on
`support/{reference}/…`: `reply`, `close` (403 unless `AllowCustomerClose`),
`reopen`, `rate` (1-5 with an optional comment, once, on a resolved or closed
ticket). A customer reaches only tickets they opened (anyone else's is a 404),
sees only public replies, and sees staff by display name only.

Admin form posts: `POST support/admin/settings` saves the keys in
`supported_settings` that the request carries (the page sends only those):
`guest_tickets_enabled`, `show_powered_by`, `knowledge_base_public`,
`widget_enabled`, `widget_color`, `widget_position`, `widget_greeting`,
`widget_allowed_origins`. The other Settings controls are configuration here
(`Escalated:AllowCustomerClose`, `Escalated:Attachments`, ...), so the page hides
them. The Settings sub-pages save to the settings table under escalated-laravel's
keys, each with `POST support/admin/settings/<page>` and a `success` flash:
`csat` (stored only; surveys do not read it yet), `data-retention` (applied daily
by `RetentionPurgeBackgroundService`, see below), `email` (`email_addresses` and
`default_reply_address`, stored only; outbound mail does not read them) and
`public-tickets` (the guest policy keys the widget reads; `POST` or `PUT`, the
`GET` and `PUT` answering page requests only, as the JSON API keeps them).
`database` is read-only: Escalated's connection is the one the host registers
`EscalatedDbContext` with, so the page shows that one connection (`default`),
`pinnedByConfig` is true, `POST support/admin/settings/database` answers with
an `error` flash, and `POST .../database/test` returns the connection's
description as JSON (a 422 for an unknown name; a page visit goes back with a
flash). Data retention: `DataRetentionService.PurgeExpiredAsync(dryRun, force)`,
run daily by `RetentionPurgeBackgroundService`, soft-deletes closed tickets past
`retention_closed_tickets` (deletes them with `force`), deletes tickets
soft-deleted more than 30 days ago, attachment rows and audit logs past their
periods, and expired guest verification codes. Every period defaults to
`never`. Attachment files stay in `IAttachmentStorage`, which has no delete. Departments: `POST support/admin/departments`, `PUT` and `DELETE
support/admin/departments/{id}`. SLA policies, escalation rules, canned responses,
macros, statuses and webhooks the same way (`POST support/admin/<name>`, `PUT` and
`DELETE support/admin/<name>/{id}`), plus `POST
support/admin/webhooks/deliveries/{id}/retry`. SLA targets arrive as objects keyed
by priority (`first_response_hours.high`); blank entries mean no target. Webhook
URLs must be public http(s) addresses, and legacy event names are stored as their
canonical names. Marking a status the default clears the default of the other
statuses in its category. Custom statuses are kept for the page as
escalated-laravel keeps them; tickets still use the built-in states.

People: the users admin routes serve `Users/Index` themselves, and answer the
page's `PATCH support/admin/users/{id}/role` with a 303 and a flash (an admin
cannot remove their own admin role). Roles: `POST support/admin/roles`, `PUT` and
`DELETE support/admin/roles/{id}` (system roles cannot be deleted; `permissions`
is the list of permission ids). API tokens: `POST support/admin/api-tokens`
(`name`, `user_id` of a staff user, `abilities` from `agent`, `admin`, `*`,
optional `expires_in_days` 1-365) flashes the new token once as
`plain_text_token`; `DELETE support/admin/api-tokens/{id}` revokes one. The page's
`api_enabled` is true once your pipeline includes `ApiTokenAuthMiddleware`.

Reports cover the last `days` (or `period`; default 30, at most 365) and are
computed from the tickets and ratings as escalated-laravel's `ReportingService`
computes them: response times against a 4 hour target, resolution against 24,
agents ranked by resolution rate (30%), first response (25%), CSAT (25%) and
volume (20%), `sla-trends` grouped by `group_by` (`day`, `week`, `month`).
Agents with no directory entry show their id. The channel cohorts know only
`chat` (a ticket with a chat session); others read `unknown`. The page's export
button and `Reports/AgentDetail` are not served yet (escalated-laravel renders
that page with props it does not read).

Guest pages: `POST support/guest` opens a ticket with a `ticket` email code and
redirects to its private link; `POST support/guest/{token}/reply` and `rate` act on
it, and `GET support/guest/{token}/attachments/{id}` serves its files, while the
grant lasts. See [guest-access.md](guest-access.md).

Knowledge base: articles and categories take `POST support/admin/kb-articles`
(`kb-categories`), `PUT` and `DELETE .../{id}`; a blank slug is made from the
title (name), publishing stamps `published_at` once, and deleting a category
leaves its articles and subcategories without one. The public pages show only
published articles, count a view per visit, and take `POST
support/kb/{slug}/feedback` (`helpful`). `Escalated:KnowledgeBase:Enabled` off
makes them 404 and `AllowFeedback` off hides the feedback buttons; with the
`knowledge_base_public` setting off, visitors who are not signed in get 403. Tags: `POST support/admin/tags`, `PUT` and
`DELETE support/admin/tags/{id}`. Workflows: the workflow admin routes
(`docs/workflows.md`) answer page requests with the page, and page form posts
(`POST`, `PUT`, `DELETE`, `toggle`, `reorder`) with a 303 to the list, or back to
the form with its errors.

Custom fields: `support/admin/custom-fields` (create, edit, update, delete,
`POST reorder`) and `support/admin/custom-objects` (create, edit, update, delete,
and `records` with `POST`, `PUT` and `DELETE`). Ticket values go in a
`custom_fields` object keyed by field slug on `POST support` (customer create),
`POST support/tickets` (JSON) and `PUT support/{agent|admin}/tickets/{reference}`;
the ticket pages and JSON detail return `custom_fields` with each active ticket
field's decoded `value`.

### Not served yet

`Admin/Workflows/Builder` is not a route in escalated-laravel either: the
workflow form renders the builder itself.

These pages of the shared frontend have no route here yet (names under
`Escalated/`): Admin/AuditLog/Index, Admin/Automations/Form, Admin/Automations/Index, Admin/BusinessHours/Form, Admin/BusinessHours/Index, Admin/Capacity/Index, Admin/Plugins/Index, Admin/Reports/AgentDetail, Admin/Settings/Sandbox, Admin/Settings/SsoSettings, Admin/Settings/TwoFactor, Admin/Skills/Form, Admin/Skills/Index, Admin/Workflows/Builder, Auth/TwoFactorChallenge, Error, Plugin/Page.
