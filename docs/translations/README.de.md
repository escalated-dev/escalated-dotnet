<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <b>Deutsch</b> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ko.md">한국어</a> ·
  <a href="README.nl.md">Nederlands</a> ·
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
  Füge deiner ASP.NET Core-App einen vollständigen Helpdesk hinzu.<br>
  Tickets, SLAs, Eskalationsregeln, ein Agenten-Posteingang und ein Kundenportal, gespeichert über EF Core in einer Datenbank deiner Wahl.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Website</b></a> ·
  <a href="https://escalated.dev/docs"><b>Dokumentation</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Änderungsprotokoll</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Der Agenten-Posteingang von Escalated: gespeicherte Ansichten mit Zählern, SLA-Countdowns und Prioritäten an jedem Ticket" width="100%">
</picture>

## Was es kann

<table>
  <tr>
    <td width="40%">
      <h3>Ein Posteingang, in dem deine Agenten den ganzen Tag arbeiten können</h3>
      Konversation, interne Notizen, Textbausteine und SLA-Timer auf einem Bildschirm. Tickets kommen auch per E-Mail über Postmark, Mailgun oder AWS SES an.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Ein geöffnetes Ticket im Agenten-Arbeitsbereich mit dem Antwort-Editor">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="Die Berichtsseite im Adminbereich mit Antwortzeiten, CSAT und SLA-Verstößen">
      </picture>
    </td>
    <td width="40%">
      <h3>SLAs und Eskalationsregeln, die du im Blick hast</h3>
      Reaktions- und Lösungsziele pro Priorität mit Geschäftszeiten, Regeln, die eskalieren oder neu zuweisen, wenn ein Ziel verfehlt wird, und Berichte über Verstöße.
    </td>
  </tr>
</table>

## Funktionen

<table>
<tr>
<td width="33%" valign="top">

**Tickets und Posteingang**
- Vollständiger Lebenszyklus mit Statusübergängen
- Warteschlangenfilter und Massenaktionen
- Interne Notizen und Textbausteine
- Makros für Sammelaktionen mit einem Klick
- Interne Nebengespräche an Tickets
- Tickets zusammenführen, verknüpfen und aufteilen
- Zurückstellen mit automatischer Wiedervorlage
- Persönliche und geteilte gespeicherte Ansichten
- Tickets mit Objekten aus deiner eigenen App verknüpfen
- Anhänge mit Größenbegrenzung
- Aktivitätsverlauf an jedem Ticket

</td>
<td width="33%" valign="top">

**SLAs und Routing**
- Reaktions- und Lösungsziele pro Priorität
- Geschäftszeiten und Erkennung von Verstößen
- Regeln, die eskalieren, neu priorisieren oder neu zuweisen
- Zeitbasierte Automatisierungen
- Routing nach Fähigkeiten
- Kapazitätsgrenzen pro Agent

</td>
<td width="33%" valign="top">

**E-Mail**
- Eingang über Postmark, Mailgun und AWS SES
- Prüfung signierter Reply-To-Adressen
- Antworten werden nur vom Anfragenden angenommen
- Threading über Message-ID-Header

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Kunden**
- Self-Service-Kundenportal
- Gast-Tickets mit Abruf per Magic-Token
- Einbettbare Widget-API
- CSAT-Bewertungen für gelöste Tickets
- Wissensdatenbank mit Suche und Feedback

</td>
<td width="33%" valign="top">

**Verwaltung und Sicherheit**
- Abteilungen, SLAs, Regeln und Tags
- Rollen und Berechtigungen
- TOTP-Zwei-Faktor mit Wiederherstellungscodes
- Anmeldung per SAML 2, OAuth 2 / OIDC und JWT
- Audit-Log mit alten und neuen Werten
- API-Tokens mit eingeschränkten Berechtigungen

</td>
<td width="33%" valign="top">

**Erweitern und integrieren**
- HMAC-signierte Webhooks mit Wiederholungen
- Benutzerdefinierte Felder mit bedingter Anzeige
- Import-Assistent mit austauschbaren Adaptern
- Live-Updates über SignalR (optional)
- Gemeinsame Oberfläche mit Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Alle Funktionen im Detail</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Schnellstart

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Registriere es in `Program.cs` mit `builder.Services.AddEscalated(...)` und `app.MapEscalated()` und rufe danach `/support` auf. Voraussetzungen: .NET 8+ und SQL Server, SQLite oder PostgreSQL. Die [Einstiegsanleitung](../getting-started.md) erklärt Konfiguration und Autorisierung, und [die Inertia-Oberfläche](../inertia-ui.md) schaltet die gemeinsamen Ansichten ein.

## Mehr erfahren

- [Alle Funktionen](../features.md) · [Erste Schritte](../getting-started.md) · [Die Inertia-Oberfläche](../inertia-ui.md)
- [Anpassung, Echtzeit und Lokalisierung](../customization.md) · [Architektur, Modelle und Events](../architecture.md) · [API-Endpunkte](../api-endpoints.md)
- [Zugriffskontrolle](../access-control.md) · [Ticketzugriff und Routing](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [Eingehende E-Mails](../inbound-email.md) · [Benachrichtigungs-E-Mails](../notification-emails.md) · [Gastzugang](../guest-access.md)
- [Widget einbetten](../embedding-the-widget.md) · [Adminbereich einbetten](../embedding-admin.md) · [Agenten-Panel](../agent-panel.md)
- [Plugins](../plugins.md) · [Newsletter](../newsletters.md) · [Entwicklung und Tests](../development.md)

Escalated gibt es auch für [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) und [Filament](https://github.com/escalated-dev/escalated-filament). Die Oberfläche liegt in [escalated](https://github.com/escalated-dev/escalated).

## Lizenz

MIT. Copyright (c) 2026 Escalated.dev. Siehe [LICENSE](../../LICENSE).
