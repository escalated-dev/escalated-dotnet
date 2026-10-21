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
(the ticket queue page, and the API's ticket list) and `POST
support/agent/tickets/bulk`. With the UI on, a request reaches the page when it
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

Agent form posts (`support/agent/tickets/{reference}/…`): `reply`, `note` (files
in `attachments[]` are stored through `IAttachmentStorage`, within
`Attachments:MaxSizeKb` and `MaxPerReply`), `assign`, `status`, `priority`,
`tags` (sets the ticket's tags to `tag_ids`), `department`, `macro`, `follow`
(toggles) and `actions/{action}`, plus `POST support/agent/tickets/bulk`.

Customer form posts: `POST support` (open a ticket; `subject`, `description`,
`priority`, `department_id`, `ticket_type`, `attachments[]`), and on
`support/{reference}/…`: `reply`, `close` (403 unless `AllowCustomerClose`),
`reopen`, `rate` (1-5 with an optional comment, once, on a resolved or closed
ticket). A customer reaches only tickets they opened (anyone else's is a 404),
sees only public replies, and sees staff by display name only.

### Not served yet

These pages of the shared frontend have no route here yet (names under
`Escalated/`): Admin/ApiTokens/Index, Admin/AuditLog/Index, Admin/Automations/Form, Admin/Automations/Index, Admin/BusinessHours/Form, Admin/BusinessHours/Index, Admin/CannedResponses/Index, Admin/Capacity/Index, Admin/CustomFields/Form, Admin/CustomFields/Index, Admin/CustomObjects/Form, Admin/CustomObjects/Index, Admin/CustomObjects/Records, Admin/Departments/Form, Admin/Departments/Index, Admin/EscalationRules/Form, Admin/EscalationRules/Index, Admin/Import/Connect, Admin/Import/Index, Admin/Import/Mapping, Admin/Import/Progress, Admin/Import/Review, Admin/KnowledgeBase/Articles/Form, Admin/KnowledgeBase/Articles/Index, Admin/KnowledgeBase/Categories/Index, Admin/Macros/Index, Admin/Plugins/Index, Admin/Reports, Admin/Reports/AgentDetail, Admin/Reports/AgentMetrics, Admin/Reports/AgentRanking, Admin/Reports/Cohorts, Admin/Reports/Comparison, Admin/Reports/CsatReport, Admin/Reports/Dashboard, Admin/Reports/ResolutionTimes, Admin/Reports/ResponseTimes, Admin/Reports/SlaReport, Admin/Reports/SlaTrends, Admin/Roles/Form, Admin/Roles/Index, Admin/Settings, Admin/Settings/CsatSettings, Admin/Settings/DataRetention, Admin/Settings/DatabaseConnection, Admin/Settings/EmailSettings, Admin/Settings/PublicTickets, Admin/Settings/Sandbox, Admin/Settings/SsoSettings, Admin/Settings/TwoFactor, Admin/Skills/Form, Admin/Skills/Index, Admin/SlaPolicies/Form, Admin/SlaPolicies/Index, Admin/Statuses/Form, Admin/Statuses/Index, Admin/Tags/Index, Admin/Tickets/Index, Admin/Tickets/Show, Admin/Users/Index, Admin/Webhooks/DeliveryLog, Admin/Webhooks/Form, Admin/Webhooks/Index, Admin/Workflows/Builder, Admin/Workflows/Form, Admin/Workflows/Index, Admin/Workflows/Logs, Auth/TwoFactorChallenge, Customer/KnowledgeBase/Article, Customer/KnowledgeBase/Index, Error, Guest/Create, Guest/Show, Plugin/Page.
