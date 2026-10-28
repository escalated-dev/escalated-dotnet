# Embedding the widget on other sites

The support widget, ticket form and live chat can run on a site other than
the one your ASP.NET Core app serves: a store, a marketing site or a customer
portal loads one script from your app. Nothing changes until you list the sites
that may do this.

## 1. Allow the embedding sites

In configuration (`appsettings.json`):

```json
{
  "Escalated": {
    "Widget": {
      "AllowedOrigins": ["https://shop.example.com", "https://*.example.org"]
    }
  }
}
```

in code:

```csharp
builder.Services.Configure<EscalatedOptions>(options =>
    options.Widget.AllowedOrigins.Add("https://shop.example.com"));
```

in the `ESCALATED_WIDGET_ALLOWED_ORIGINS` configuration value, comma separated
(an environment variable under the default host configuration):

```bash
ESCALATED_WIDGET_ALLOWED_ORIGINS=https://shop.example.com,https://*.example.org
```

or in the `widget_allowed_origins` setting (one per line or comma separated),
saved through `POST /support/admin/settings`:

```json
{ "widget_allowed_origins": "https://shop.example.com\nhttps://*.example.org" }
```

All of them are combined. Each entry is an origin: `scheme://host[:port]`,
http or https, no path, query or user name. `https://*.example.org` matches
every subdomain of `example.org` at any depth, but not `example.org` itself and
not `http://`. A bare `*` is never accepted. The admin endpoint rejects the
whole update with a 400 validation problem (`errors.widget_allowed_origins`)
when any entry is invalid, and saves valid entries normalised, one per line.
Invalid entries in configuration are ignored.

`Escalated:Widget:PreflightMaxAge` sets how long browsers cache a preflight
answer (default 600 seconds).

## 2. Add the script to the other site

```html
<script src="https://help.example.com/support/widget/embed.js" async></script>
```

For an on-page support form instead of a floating button:

```html
<div id="support-form"></div>
<script
  src="https://help.example.com/support/widget/embed.js"
  data-mode="inline"
  data-target="#support-form"
  data-tabs="ticket"
  data-department="3"
  data-lock-department="true"
  data-meta-order-number="A-1001"
></script>
```

All host parameters (tabs, department, prefill, metadata, title, the
JavaScript API) are described in the frontend package's
[embedding guide](https://github.com/escalated-dev/escalated/blob/main/docs/embedding-the-widget.md).

## What the server does

`AddEscalated()` registers `WidgetCorsMiddleware` with an `IStartupFilter`, so
it runs at the front of your pipeline without a `Use...` call. It handles
every `/support/widget/*` route, including the live chat routes under
`/support/widget/chat/*`. For a request whose `Origin` names another site on
the list it:

- answers preflight requests (`OPTIONS` with `Access-Control-Request-Method`)
  with 204, `Access-Control-Allow-Origin: <origin>`, methods
  `GET, POST, OPTIONS`, headers
  `Accept, Authorization, Content-Type, X-Requested-With` and a 600 second
  `Access-Control-Max-Age`;
- adds `Access-Control-Allow-Origin`, `Access-Control-Expose-Headers:
  Retry-After` and `Vary: Origin` to every other response, including 400,
  404 and the guest rate limit's 429;
- treats the visitor as a guest even if the same browser is signed in to your
  app: `HttpContext.User` is replaced with an anonymous principal, before and
  again after your authentication middleware (a global MVC authorization
  filter). Guest access grants (`Authorization: Bearer`) work as for any guest.

`Access-Control-Allow-Credentials` is never sent, so browsers send no cookies
with these requests.

Preflights from other origins get a 204 without a grant. Every other request
from an origin that is not listed, same-origin requests and requests without
an `Origin` header are handled exactly as before. The widget routes take JSON
and use no antiforgery token, so there is no check to skip. Guest rate limits
apply to embedded requests unchanged.

The middleware compares the `Origin` header with the request's own scheme and
`Host`. It runs before your `UseForwardedHeaders()`, so behind a TLS-terminating
proxy it may see `http`; the guest filter checks again once forwarded headers
are applied. Do not list your app's own origin: it does not need CORS.

### `GET /support/widget/config`

Returns what the widget needs before it renders:

```json
{
  "enabled": true,
  "color": "#4F46E5",
  "position": "bottom-right",
  "greeting": "Hi there! How can we help?",
  "departments": [{ "id": 1, "name": "Sales" }],
  "kb_enabled": true,
  "guest_tickets_enabled": true,
  "guest_verification_required": true,
  "accepts_metadata": true,
  "status_lookup_enabled": true
}
```

While the `widget_enabled` setting is off (`0` or `false`; it is on by default),
every widget route answers 403 and `/config` answers `{ "enabled": false }`.
`color`, `position` and `greeting` come from the `widget_color`,
`widget_position` and `widget_greeting` settings. `departments` lists active
departments. `kb_enabled` follows `Escalated:KnowledgeBase:Enabled`, and
`guest_tickets_enabled` the `guest_tickets_enabled` setting (on by default).
Guests verify their email with a code before they open a ticket, start a chat
or look a ticket up ([verified guest access](guest-access.md)), so
`guest_verification_required` and `status_lookup_enabled` are true.

### What the shared widget calls

| Method + path | Body | Answer |
|---|---|---|
| `GET /articles?q=` | | `{ "articles": [{ "title", "slug", "excerpt" }] }`; queries under 2 characters find nothing |
| `GET /articles/{slug}` | | `{ "title", "slug", "body", "category" }`; 404 unless published |
| `POST /verification` | `email`, `purpose` (`ticket`, `chat`, `lookup`) | 202 `{ "verification_id", "expires_in": 600, "message" }`; the code is emailed |
| `POST /tickets` | `name`, `email`, `subject`, `description`, `department_id?`, `metadata?`, `verification_id`, `verification_code` | 201 `{ "message", "reference", "guest_access_token", "expires_at" }` |
| `POST /lookup` | `email`, `reference`, `verification_id`, `verification_code` | `{ "data": [{ "reference", "subject", "guest_access_token", "expires_at" }] }` |
| `GET /tickets/{reference}` | `Authorization: Bearer <guest_access_token>` | `{ "reference", "subject", "status", "status_label", "status_color", "created_at", "department", "replies" }` |
| `GET /chat/availability` | `department_id?` | `{ "available": true }` |
| `POST /chat/start` | `name`, `email`, `department_id?`, `message?`, `metadata?`, `verification_id`, `verification_code` | `{ "id": "<token>", "expires_at", "status", "agent" }` |
| `GET /chat/{id}/messages` | | `{ "messages": [{ "id", "body", "is_agent", "author": { "name" }, "created_at" }], "typing": null, "agent", "ended" }` |
| `POST /chat/{id}/messages` | `{ "body" }` | the stored message |
| `POST /chat/{id}/typing` | | sent to agents watching the chat over SignalR |
| `POST /chat/{id}/end` | | the ended chat |
| `POST /chat/{id}/rate` | `{ "rating": 1-5, "comment"? }` | once per chat, after it ends |

Paths are under `/support/widget`, so the CORS above covers them. Both
`/articles` routes answer 404 when the knowledge base is off. `department_id`
(or the older `departmentId`) must name an active department; anything else is a
400 with `errors.department_id`.

The chat `id` is a random 256-bit token; only its SHA-256 is stored (the
`ChatSessionVisitorToken` migration adds `escalated_chat_sessions.VisitorTokenHash`).
It expires `Escalated:GuestAccess:TtlMinutes` after the chat starts
(`expires_at`, default 24 hours; the `ChatVisitorTokenExpiry` migration adds
`VisitorTokenExpiresAt`). Every chat route resolves the chat by that token and
answers 404 for an unknown or expired token, numeric session ids included. Agent names come from your `IUserDirectory`
and fall back to "Agent".

### Host parameters

`POST /support/widget/tickets` and `POST /support/widget/chat/start` accept an
optional `metadata` object. It is stored as JSON in the ticket's `Metadata`
column, and for a chat also in the chat session's `Metadata`. The values come
from the visitor's browser, so only a flat map is kept: up to 25 entries, keys
of letters, digits, `_ . : -` (64 characters at most), and string, number or
boolean values (strings trimmed and cut to 500 characters). `page_url` is kept
only when it is an http(s) URL of at most 2048 characters. `embedded_on` is
always set by the server from the request's `Origin` and cannot be supplied by
the page. A `metadata` that is not an object, or has more than 50 keys, is
rejected with a 400 validation problem. Treat all of it as unverified visitor
input. Both columns already exist, so there is no migration.

### The script

`GET /support/widget/embed.js` serves `src/Escalated/Resources/widget/escalated-widget.js`,
the standalone bundle from `@escalated-dev/escalated` 0.12.1, embedded in the
`Escalated` assembly, with `Content-Type: application/javascript; charset=utf-8`,
`Cache-Control: public, max-age=3600`, an `ETag` (answering `If-None-Match`
with 304), `X-Content-Type-Options: nosniff`,
`Cross-Origin-Resource-Policy: cross-origin` and
`Access-Control-Allow-Origin: *`. If you serve the bundle from a CDN instead,
pages need `data-base-url` pointing at your app.

To update the bundle, copy `dist/widget/escalated-widget.js` from the
published npm package (`npm pack @escalated-dev/escalated@<version>`) over the
vendored file.

## Troubleshooting

- **The browser reports a CORS error.** The page's origin is not on the list,
  or it differs in scheme or port (`http://` vs `https://`, `:8080`).
- **A host CORS policy also covers the widget.** Escalated answers widget
  preflights before your `app.UseCors()` runs, but a default policy for every
  route can still grant origins that are not on Escalated's list on the actual
  requests. Leave `/support/widget` out of it.
- **A signed-in agent testing the embed sees guest behaviour.** That is
  intended: requests from another site never act as the signed-in user.
