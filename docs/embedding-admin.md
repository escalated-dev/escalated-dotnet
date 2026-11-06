# Embedding the admin in your own admin panel (optional)

Escalated ships a complete standalone admin at `/support/admin` (your
`Escalated:RoutePrefix` + `/admin`), with its own sidebar and top bar. **That
is the default, and it needs no setup.** If your ASP.NET Core app already has
an admin panel, you can instead show Escalated's admin screens inside it: under
your sidebar and header, your URL prefix and your authorization policies.

This is an optional alternative to the standalone admin. Nothing on this page
has any effect until you turn it on: with no embed configuration every route,
page, prop, header and policy is exactly what it was before (a test compares
the whole endpoint table with a snapshot taken before the feature existed). The
design is in the ADR
[`2026-11-05-embeddable-admin`](https://github.com/escalated-dev/escalated-developer-context/blob/main/decisions/2026-11-05-embeddable-admin.md);
the frontend side is documented in
[`@escalated-dev/escalated` docs/embedding-admin.md](https://github.com/escalated-dev/escalated/blob/main/docs/embedding-admin.md).
It needs the Inertia UI on (`Escalated:Ui:Enabled`, see [inertia-ui.md](inertia-ui.md)).

## Contents

- [Pick your setup](#pick-your-setup)
- [Turning it on](#turning-it-on)
- [Configuration reference](#configuration-reference)
- [What changes, and what does not](#what-changes-and-what-does-not)
- [1. Vue + Inertia host](#1-vue--inertia-host)
- [2. Razor host](#2-razor-host)
- [3. iframe](#3-iframe)
- [The admin menu: `escalated.navigation`](#the-admin-menu-escalatednavigation)
- [Security](#security)
- [Turning it off](#turning-it-off)

## Pick your setup

| Your admin panel | Use | `Mode` |
|---|---|---|
| Inertia + Vue in this app | [Vue + Inertia host](#1-vue--inertia-host) | `layout` |
| Razor views (MVC or Razor Pages) | [Razor host](#2-razor-host) (your Razor layout as the root view) | `layout` |
| Another app or origin, a non-Vue SPA, a CMS admin | [iframe](#3-iframe) | `iframe` |

## Turning it on

Call `EmbedAdmin()` on the options in `Program.cs`:

```csharp
using Escalated.Configuration;

builder.Services.AddEscalated(builder.Configuration, db => db.UseSqlServer(connectionString));

// Your admin policy: the embedded admin requires it in addition to Escalated's own.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("BackOfficeAdmin", policy => policy.RequireRole("Admin")));

builder.Services.Configure<EscalatedOptions>(options => options.EmbedAdmin(
    prefix: "admin/support",
    policies: new[] { "BackOfficeAdmin" },
    layout: "Layouts/AdminLayout"));
```

or set the keys in `appsettings.json`:

```json
{
  "Escalated": {
    "Ui": {
      "Enabled": true,
      "Embed": {
        "Enabled": true,
        "Mode": "layout",
        "Prefix": "admin/support",
        "Policies": [ "BackOfficeAdmin" ],
        "ReplaceAdminGate": false,
        "Layout": "Layouts/AdminLayout",
        "RootView": null,
        "FrameAncestors": []
      }
    }
  }
}
```

or the environment variables `ESCALATED_EMBED=true`, `ESCALATED_EMBED_MODE`,
`ESCALATED_EMBED_PREFIX` and `ESCALATED_EMBED_FRAME_ANCESTORS`
(comma-separated). The variables fill settings left at their defaults; explicit
configuration or `EmbedAdmin()` wins. The settings are read when the endpoint
table is built, so change them with a restart.

## Configuration reference

| Key (`Escalated:Ui:Embed:*`) | `EmbedAdmin()` argument | Env var | Default | Meaning |
|---|---|---|---|---|
| `Enabled` | (always on) | `ESCALATED_EMBED` | `false` | Master switch. While `false`, nothing below has any effect. |
| `Mode` | `mode` | `ESCALATED_EMBED_MODE` | `"layout"` | `"layout"` (Vue + Inertia or Razor host) or `"iframe"`. |
| `Prefix` | `prefix` | `ESCALATED_EMBED_PREFIX` | `"admin/support"` | URL prefix the admin moves under. |
| `Policies` | `policies` | | `null` | Your "middleware": authorization policies every embedded admin request must pass, on top of your authentication. `null` adds none. |
| `Filters` (code only) | `filters` | | `[]` | MVC filters (`IFilterMetadata`, e.g. `new ServiceFilterAttribute(typeof(AuditAdminAccess))`) added to every embedded admin action. |
| `ReplaceAdminGate` | `replaceAdminGate` | | `false` | `true` removes Escalated's `escalated-admin` policy from these routes so your policies alone decide. See [Security](#security). |
| `Layout` | `layout` | | `null` | Name of your Vue admin layout, matched against the frontend plugin's `layouts` option. |
| `RootView` | `rootView` | | `null` | Razor view for embedded pages ([Razor host](#2-razor-host)), e.g. `~/Views/Shared/SupportAdmin.cshtml`. |
| `FrameAncestors` | `frameAncestors` | `ESCALATED_EMBED_FRAME_ANCESTORS` (comma-separated) | `[]` | iframe mode: exact origins allowed to frame the admin. `*`, wildcards, paths and non-http(s) entries are ignored. |

## What changes, and what does not

While embedding is on:

- Every action routed under `/support/admin` (the admin screens, the plugin
  admin and plugin pages, API tokens, import, reports, settings, and the admin
  JSON endpoints that share those URLs) answers under your `Prefix` instead,
  with the same controller action. Your `Policies` and `Filters` are added;
  Escalated's own checks stay: the `escalated-admin` policy (unless you replace
  it), two-factor, route permissions and the access feature switches, which
  keep reading the action's standalone route, so they apply exactly as before.
  The old `/support/admin/...` URLs answer 404.
- **Route names do not change.** The shared frontend builds admin URLs with
  `route('escalated.admin.…')`; the bundled route helper (`UiRoutes`) points
  those names at the new URLs, and redirects, form URLs and plugin menu links
  follow.
- Embedded pages carry `escalated.embed` (below) and every Escalated page
  carries `escalated.navigation`, the admin menu for your sidebar.
- Embedded endpoints carry `EscalatedEmbeddedAdminMetadata`, so your own
  endpoint conventions can find them.

Unchanged: the agent panel (`/support/agent`), customer portal, guest pages,
knowledge base, widget, the JSON API under `/support/api`, SSO, the two-factor
enrolment page (`/support/admin/settings/two-factor`, personal to each staff
member) and challenge, and the newsletter admin (`/admin/newsletters`).

`escalated.embed` on an embedded page:

```json
{
  "enabled": true,
  "mode": "layout",
  "layout": "Layouts/AdminLayout",
  "route": "escalated.admin.tickets.index",
  "base_url": "/admin/support",
  "allowed_origins": []
}
```

On every other page `enabled` is `false` and `route` is `null`. `route` is the
`route()` name of the page's URL.

## 1. Vue + Inertia host

Backend: turn embedding on (above) with your prefix and admin policy.

Frontend: register your admin layout under the name you gave `Layout`, or add
`admin` to the plugin's `layoutScope`:

```js
import { createApp, h } from 'vue';
import { createInertiaApp } from '@inertiajs/vue3';
import { EscalatedPlugin, resolveEscalatedPage } from '@escalated-dev/escalated';
import AdminLayout from './Layouts/AdminLayout.vue';
import AppLayout from './Layouts/AppLayout.vue';

createInertiaApp({
    resolve: (name) => resolveEscalatedPage(name),
    setup({ el, App, props, plugin }) {
        createApp({ render: () => h(App, props) })
            .use(plugin)
            .use(EscalatedPlugin, {
                layout: AppLayout, // customer pages, as before
                layouts: { 'Layouts/AdminLayout': AdminLayout }, // embedded admin pages
                theme: { panel: { mode: 'light' } }, // match your admin
            })
            .mount(el);
    },
});
```

Your admin layout needs the slots the plugin's `layout` always required, a
`#header` slot (Escalated puts the page title there) and a default slot. List
Escalated's menu with `<EscalatedNavigation />`:

```vue
<!-- Layouts/AdminLayout.vue -->
<script setup>
import { Link } from '@inertiajs/vue3';
import { EscalatedNavigation } from '@escalated-dev/escalated';
</script>

<template>
    <div class="flex min-h-screen">
        <aside class="w-64 border-r bg-white p-3">
            <Link href="/backoffice">Dashboard</Link>
            <!-- …your own items… -->
            <p class="mt-4 px-3 text-xs font-semibold uppercase text-gray-500">Support</p>
            <EscalatedNavigation />
        </aside>
        <div class="flex-1">
            <header class="border-b bg-white px-6 py-4"><slot name="header" /></header>
            <main class="p-6"><slot /></main>
        </div>
    </div>
</template>
```

Escalated renders each admin page chrome-free (no Escalated sidebar, top bar,
logo or footer) inside your layout. To use the same layout for customer pages
too, use `layout: AdminLayout, layoutScope: ['customer', 'admin']` instead of
`layouts`.

## 2. Razor host

If your admin layout is a Razor view, make a view that contains your chrome and
the Inertia mount point, and point `RootView` at it. Embedded admin pages
(first visits; Inertia visits get JSON as always) render through it; every
other Escalated page keeps the normal root view. Rendering a view needs MVC's
view services, so register them with `AddControllersWithViews()` (or
`AddMvc()` / `AddRazorPages()`):

```csharp
builder.Services.AddControllersWithViews();

builder.Services.Configure<EscalatedOptions>(options => options.EmbedAdmin(
    prefix: "admin/support",
    policies: new[] { "BackOfficeAdmin" },
    rootView: "~/Views/Shared/SupportAdmin.cshtml"));
```

The view's model is `Escalated.Ui.EscalatedEmbedRootViewModel`: write `Head`
in `<head>` (the route helper and your `Escalated:Ui:HeadHtml` bundle tags),
`Mount` where the page renders, and list `Navigation`:

```cshtml
@model Escalated.Ui.EscalatedEmbedRootViewModel
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>@Model.Title · Back office</title>
    <link rel="stylesheet" href="/css/backoffice.css">
    @Model.Head
</head>
<body>
    <header class="topbar">Back office</header>
    <div class="layout">
        <nav class="sidebar" aria-label="Back office">
            <a href="/backoffice">Dashboard</a>
            <p class="menu-heading">Support</p>
            <ul>
                @foreach (var item in Model.Navigation)
                {
                    <li><a href="@item.Url" class="@(item.Active ? "active" : null)" aria-current="@(item.Active ? "page" : null)">@item.Label</a></li>
                }
            </ul>
        </nav>
        <main>@Model.Mount</main>
    </div>
</body>
</html>
```

(This is the view the test suite renders: `tests/Escalated.Tests/Views/Shared/SupportAdmin.cshtml`.)
Your Inertia entry needs no extra option: Escalated sees
`escalated.embed.enabled` and renders the page chrome-free (title + content).

Your own Razor pages can list the same menu:

```cshtml
@using Escalated.Ui
@foreach (var item in await Context.EscalatedAdminNavigationAsync())
{
    <a href="@item.Url">@item.Label</a>
}
```

## 3. iframe

For an admin that is not part of this app (another origin, another stack),
frame the embedded admin:

```csharp
builder.Services.Configure<EscalatedOptions>(options => options.EmbedAdmin(
    prefix: "admin/support",
    policies: new[] { "BackOfficeAdmin" },
    mode: "iframe",
    frameAncestors: new[] { "https://admin.example.com" }));
```

or `ESCALATED_EMBED=true`, `ESCALATED_EMBED_MODE=iframe`,
`ESCALATED_EMBED_FRAME_ANCESTORS=https://admin.example.com`.

Embedded responses then carry
`Content-Security-Policy: frame-ancestors 'self' https://admin.example.com`
and no `X-Frame-Options` (the allow-listed `frame-ancestors` replaces it on
those responses only). This is applied once the response is built, after your
middleware, so a security-headers middleware that adds `X-Frame-Options`
cannot undo it. If your app already sends a CSP, it is kept and this directive
is added as a second policy; if your own policy has a `frame-ancestors`
directive, add the same origins there too, since browsers enforce both. Layout
mode adds no headers.

On the framing page:

```html
<iframe id="support-admin" src="https://support.example.com/admin/support/tickets"
        style="width:100%;border:0" title="Support administration"></iframe>
<script type="module">
    import { connectEscalatedFrame } from '@escalated-dev/escalated/embed';

    const frame = connectEscalatedFrame(document.getElementById('support-admin'), {
        origin: 'https://support.example.com',
        onNavigate: ({ route, title }) => highlightMenu(route),
    });
    frame.setTheme('light');
</script>
```

The frame sizes itself to its content and reports each page change; the
protocol is in the frontend guide.

### Cookies in a frame

- **Same site** (`admin.example.com` framing `support.example.com`): the
  default `SameSite=Lax` cookies work.
- **Cross-site** (different registrable domains): your sign-in cookie must be
  sent in a third-party context. With cookie authentication or ASP.NET Core
  Identity:

  ```csharp
  builder.Services.ConfigureApplicationCookie(options => // or .AddCookie(options => ...)
  {
      options.Cookie.SameSite = SameSiteMode.None;
      options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
  });
  ```

  and serve both sites over HTTPS. Escalated's own cookies on embedded
  responses (the flash message, the import wizard's credentials) go out as
  `SameSite=None; Secure` by themselves in iframe mode over HTTPS. Browsers
  that block third-party cookies will still not keep a session in a cross-site
  frame; use the Vue + Inertia or Razor setup in that case.
- The two-factor challenge does not move and is not framed: staff who must pass
  it do so at `/support/two-factor/challenge` outside the frame (or use a
  layout setup).
- CSRF handling is unchanged.

## The admin menu: `escalated.navigation`

While embedding is on, every Escalated page shares the admin menu, and
`EscalatedAdminNavigation.ForAsync(user, httpContext)` (or
`HttpContext.EscalatedAdminNavigationAsync()`) returns the same list for Razor.
The helper works whether or not embedding is on; its URLs follow wherever the
admin is mounted.

```json
[
  {
    "key": "tickets",
    "label": "Tickets",
    "route": "escalated.admin.tickets.index",
    "url": "/admin/support/tickets",
    "icon": "tickets",
    "position": 20,
    "active": true,
    "badge": null,
    "plugin": null,
    "children": []
  }
]
```

It is filtered for the user: empty for anyone who fails the `escalated-admin`
policy (unless you replaced it, then it is built for any signed-in user),
screens this app does not serve are left out, Roles while roles & permissions
is off and Newsletters while newsletters are off, and while roles & permissions
is on each item needs the permission its route requires. Active plugins' admin
menu items are included (key `plugin:<slug>:<n>`, `route` null), sorted by
`position` with the core items. Core `icon` keys match the frontend's
`escalatedNavIcons`.

## Security

- **The admin gate stays on by default.** Your policies run *in addition to*
  Escalated's `escalated-admin` policy. Set `ReplaceAdminGate` only if your
  policies themselves restrict the routes to admins: with it on, your policies
  are the only check on who reaches the admin (Escalated still requires a
  signed-in user it can identify). Two-factor and route permissions still apply
  either way, and the agent panel keeps Escalated's policies.
- **Framing is allow-listed.** In iframe mode only the origins you list (plus
  your own) may frame the admin; nothing else, and never `*`.
- **No open `postMessage`.** The frame talks only to an allow-listed parent
  origin and ignores messages from anywhere else.

## Turning it off

Remove the `EmbedAdmin()` call and the `Escalated:Ui:Embed` settings (with
`ESCALATED_EMBED` unset), and restart. The admin returns to `/support/admin`
with Escalated's own chrome.
