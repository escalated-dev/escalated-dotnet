<p align="center"><sub>
  <a href="docs/translations/README.ar.md">العربية</a> ·
  <a href="docs/translations/README.de.md">Deutsch</a> ·
  <b>English</b> ·
  <a href="docs/translations/README.es.md">Español</a> ·
  <a href="docs/translations/README.fr.md">Français</a> ·
  <a href="docs/translations/README.it.md">Italiano</a> ·
  <a href="docs/translations/README.ja.md">日本語</a> ·
  <a href="docs/translations/README.ko.md">한국어</a> ·
  <a href="docs/translations/README.nl.md">Nederlands</a> ·
  <a href="docs/translations/README.pl.md">Polski</a> ·
  <a href="docs/translations/README.pt-BR.md">Português (BR)</a> ·
  <a href="docs/translations/README.ru.md">Русский</a> ·
  <a href="docs/translations/README.tr.md">Türkçe</a> ·
  <a href="docs/translations/README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  Add a complete helpdesk to your ASP.NET Core app.<br>
  Tickets, SLAs, escalation rules, an agent inbox and a customer portal, stored through EF Core in a database you choose.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Website</b></a> ·
  <a href="https://escalated.dev/docs"><b>Docs</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="CHANGELOG.md"><b>Changelog</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="The Escalated agent inbox: saved views with counts, SLA countdowns and priorities on every ticket" width="100%">
</picture>

## What it does

<table>
  <tr>
    <td width="40%">
      <h3>An inbox your agents can work in all day</h3>
      Conversation, internal notes, canned replies and SLA timers on one screen. Tickets also arrive by email through Postmark, Mailgun or AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="A ticket open in the agent workspace with the reply composer">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="The admin reports page with response times, CSAT and SLA breaches">
      </picture>
    </td>
    <td width="40%">
      <h3>SLAs and escalation rules you can see</h3>
      Response and resolution targets per priority with business hours, rules that escalate or reassign when they slip, and reports on breaches.
    </td>
  </tr>
</table>

## Features

<table>
<tr>
<td width="33%" valign="top">

**Tickets and inbox**
- Full lifecycle with status transitions
- Queue filters and bulk actions
- Internal notes and canned responses
- Macros for one-click batch actions
- Side conversations on tickets
- Merge, link and split tickets
- Snooze with automatic wake-up
- Personal and shared saved views
- Ticket subjects from your own app
- Attachments with size limits
- Activity timeline on every ticket

</td>
<td width="33%" valign="top">

**SLAs and routing**
- Per-priority response and resolution targets
- Business hours and breach detection
- Rules that escalate, reprioritize or reassign
- Time-based automations
- Skill-based routing
- Per-agent capacity limits

</td>
<td width="33%" valign="top">

**Email**
- Inbound from Postmark, Mailgun and AWS SES
- Signed Reply-To verification
- Replies accepted only from the requester
- Threading with Message-ID headers

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Customers**
- Self-service customer portal
- Guest tickets with magic-token lookup
- Embeddable widget API
- CSAT ratings on resolved tickets
- Knowledge base with search and feedback

</td>
<td width="33%" valign="top">

**Admin and security**
- Departments, SLAs, rules and tags
- Roles and permissions
- TOTP two-factor with recovery codes
- SAML 2, OAuth 2 / OIDC and JWT sign-in
- Audit log with old and new values
- API tokens with scoped abilities

</td>
<td width="33%" valign="top">

**Extend and integrate**
- HMAC-signed webhooks with retries
- Custom fields with conditional display
- Import wizard with pluggable adapters
- Live updates over SignalR (opt-in)
- Shared Vue 3 + Inertia UI

</td>
</tr>
</table>

<p align="center"><sub><a href="docs/features.md">Every feature in detail</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Quick start

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Register it in `Program.cs` with `builder.Services.AddEscalated(...)` and `app.MapEscalated()`, then visit `/support`. Needs .NET 8+ and SQL Server, SQLite or PostgreSQL. The [getting started guide](docs/getting-started.md) covers configuration and authorization, and [the Inertia UI](docs/inertia-ui.md) turns on the shared screens.

## Learn more

- [All features](docs/features.md) · [Getting started](docs/getting-started.md) · [The Inertia UI](docs/inertia-ui.md)
- [Customization, real-time and localization](docs/customization.md) · [Architecture, models and events](docs/architecture.md) · [API endpoints](docs/api-endpoints.md)
- [Access control](docs/access-control.md) · [Ticket access and routing](docs/ticket-access-and-routing.md) · [Workflows](docs/workflows.md)
- [Inbound email](docs/inbound-email.md) · [Notification emails](docs/notification-emails.md) · [Guest access](docs/guest-access.md)
- [Embedding the widget](docs/embedding-the-widget.md) · [Embedding the admin](docs/embedding-admin.md) · [Agent panel](docs/agent-panel.md)
- [Plugins](docs/plugins.md) · [Newsletters](docs/newsletters.md) · [Development and testing](docs/development.md)

Escalated is also available for [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) and [Filament](https://github.com/escalated-dev/escalated-filament). The UI lives in [escalated](https://github.com/escalated-dev/escalated).

## License

MIT. Copyright (c) 2026 Escalated.dev. See [LICENSE](LICENSE).
