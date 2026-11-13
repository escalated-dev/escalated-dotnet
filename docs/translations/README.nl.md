<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ko.md">한국어</a> ·
  <b>Nederlands</b> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.pt-BR.md">Português (BR)</a> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.tr.md">Türkçe</a> ·
  <a href="README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  Voeg een complete helpdesk toe aan je ASP.NET Core-app.<br>
  Tickets, SLA's, escalatieregels, een inbox voor agents en een klantenportaal, opgeslagen via EF Core in een database naar keuze.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Website</b></a> ·
  <a href="https://escalated.dev/docs"><b>Documentatie</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Wijzigingslog</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="De inbox voor agents van Escalated: opgeslagen weergaven met tellers, SLA-afteltimers en prioriteiten bij elk ticket" width="100%">
</picture>

## Wat het doet

<table>
  <tr>
    <td width="40%">
      <h3>Een inbox waarin je agents de hele dag kunnen werken</h3>
      Gesprek, interne notities, standaardantwoorden en SLA-timers op één scherm. Tickets komen ook binnen per e-mail via Postmark, Mailgun of AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Een geopend ticket in de werkruimte van de agent met de antwoordeditor">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="De rapportpagina in het beheer met reactietijden, CSAT en SLA-overschrijdingen">
      </picture>
    </td>
    <td width="40%">
      <h3>SLA's en escalatieregels die je kunt zien</h3>
      Reactie- en oplosdoelen per prioriteit met kantooruren, regels die escaleren of opnieuw toewijzen als een doel uitloopt, en rapporten over overschrijdingen.
    </td>
  </tr>
</table>

## Functies

<table>
<tr>
<td width="33%" valign="top">

**Tickets en inbox**
- Volledige levenscyclus met statusovergangen
- Wachtrijfilters en bulkacties
- Interne notities en standaardantwoorden
- Macro's voor bulkacties met één klik
- Nevengesprekken bij tickets
- Tickets samenvoegen, koppelen en splitsen
- Uitstellen met automatisch heractiveren
- Persoonlijke en gedeelde opgeslagen weergaven
- Tickets gekoppeld aan objecten uit je eigen app
- Bijlagen met groottelimieten
- Activiteitentijdlijn bij elk ticket

</td>
<td width="33%" valign="top">

**SLA's en routering**
- Reactie- en oplosdoelen per prioriteit
- Kantooruren en detectie van overschrijdingen
- Regels die escaleren, de prioriteit wijzigen of opnieuw toewijzen
- Tijdgebaseerde automatiseringen
- Routering op basis van vaardigheden
- Capaciteitslimieten per agent

</td>
<td width="33%" valign="top">

**E-mail**
- Inkomend via Postmark, Mailgun en AWS SES
- Verificatie van ondertekende Reply-To
- Antwoorden alleen geaccepteerd van de aanvrager
- Threading met Message-ID-headers

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Klanten**
- Selfservice-klantenportaal
- Gasttickets met opzoeken via magic token
- Insluitbare widget-API
- CSAT-beoordelingen op opgeloste tickets
- Kennisbank met zoeken en feedback

</td>
<td width="33%" valign="top">

**Beheer en beveiliging**
- Afdelingen, SLA's, regels en tags
- Rollen en rechten
- TOTP-tweestapsverificatie met herstelcodes
- Inloggen met SAML 2, OAuth 2 / OIDC en JWT
- Auditlog met oude en nieuwe waarden
- API-tokens met afgebakende rechten

</td>
<td width="33%" valign="top">

**Uitbreiden en integreren**
- HMAC-ondertekende webhooks met nieuwe pogingen
- Aangepaste velden met voorwaardelijke weergave
- Importwizard met verwisselbare adapters
- Live-updates via SignalR (optioneel)
- Gedeelde Vue 3 + Inertia-UI

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Alle functies in detail</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Snel aan de slag

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Registreer het in `Program.cs` met `builder.Services.AddEscalated(...)` en `app.MapEscalated()` en ga daarna naar `/support`. Vereist .NET 8+ en SQL Server, SQLite of PostgreSQL. De [handleiding om te beginnen](../getting-started.md) behandelt configuratie en autorisatie, en [de Inertia-UI](../inertia-ui.md) zet de gedeelde schermen aan.

## Meer informatie

- [Alle functies](../features.md) · [Aan de slag](../getting-started.md) · [De Inertia-UI](../inertia-ui.md)
- [Aanpassen, realtime en lokalisatie](../customization.md) · [Architectuur, modellen en events](../architecture.md) · [API-endpoints](../api-endpoints.md)
- [Toegangsbeheer](../access-control.md) · [Tickettoegang en routering](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [Inkomende e-mail](../inbound-email.md) · [Notificatie-e-mails](../notification-emails.md) · [Gasttoegang](../guest-access.md)
- [De widget insluiten](../embedding-the-widget.md) · [Het beheer insluiten](../embedding-admin.md) · [Agentpaneel](../agent-panel.md)
- [Plugins](../plugins.md) · [Nieuwsbrieven](../newsletters.md) · [Ontwikkeling en testen](../development.md)

Escalated is ook beschikbaar voor [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) en [Filament](https://github.com/escalated-dev/escalated-filament). De UI staat in [escalated](https://github.com/escalated-dev/escalated).

## Licentie

MIT. Copyright (c) 2026 Escalated.dev. Zie [LICENSE](../../LICENSE).
