<p align="center">
  <a href="README.ar.md">العربية</a> •
  <b>Deutsch</b> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <a href="README.it.md">Italiano</a> •
  <a href="README.ja.md">日本語</a> •
  <a href="README.ko.md">한국어</a> •
  <a href="README.nl.md">Nederlands</a> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated für ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Ein vollständiges, einbettbares Support-Ticket-System für ASP.NET Core. Integrieren Sie es in jede App und erhalten Sie einen kompletten Helpdesk mit SLA-Tracking, Eskalationsregeln, Agenten-Workflows und einem Kundenportal. Keine externen Dienste erforderlich.

> **[escalated.dev](https://escalated.dev)** -- Mehr erfahren, Demos ansehen und Cloud- vs. Self-Hosted-Optionen vergleichen.

## Funktionen

- **Ticket-Lebenszyklus** -- Erstellen, zuweisen, antworten, lösen, schließen, wiedereröffnen mit konfigurierbaren Statusübergängen
- **SLA-Engine** -- Antwort- und Lösungsziele pro Priorität, Geschäftszeitenberechnung, automatische Verletzungserkennung
- **Eskalationsregeln** -- Bedingungsbasierte Regeln, die automatisch eskalieren, priorisieren, neu zuweisen oder benachrichtigen
- **Automatisierungen** -- Zeitbasierte Regeln mit Bedingungen und Aktionen
- **Agenten-Dashboard** -- Ticket-Warteschlange mit Filtern, Massenaktionen, internen Notizen, vorgefertigten Antworten
- **Kundenportal** -- Self-Service-Ticket-Erstellung, Antworten und Statusverfolgung
- **Admin-Panel** -- Abteilungen, SLA-Richtlinien, Eskalationsregeln, Tags und mehr verwalten
- **Makros und vorgefertigte Antworten** -- Stapelaktionen und wiederverwendbare Antwortvorlagen
- **Benutzerdefinierte Felder** -- Dynamische Metadaten mit bedingter Anzeigelogik
- **Wissensdatenbank** -- Artikel, Kategorien, Suche und Feedback
- **Dateianhänge** -- Upload-Unterstützung mit konfigurierbarem Speicher und Größenlimits
- **Aktivitätszeitachse** -- Vollständiges Audit-Log jeder Aktion an jedem Ticket
- **Webhooks** -- HMAC-SHA256-signiert mit Wiederholungslogik
- **API-Tokens** -- Bearer-Authentifizierung mit fähigkeitsbasierter Bereichseingrenzung
- **Rollen und Berechtigungen** -- Feingranulare Zugriffskontrolle
- **Audit-Protokollierung** -- Alle Mutationen mit alten/neuen Werten aufgezeichnet
- **Importsystem** -- Mehrstufiger Assistent mit steckbaren Adaptern
- **Nebenkonversationen** -- Interne Team-Threads an Tickets
- **Ticket-Zusammenführung und -Verknüpfung** -- Doppelte Tickets zusammenführen und Vorgänge verknüpfen
- **Ticket-Aufteilung** -- Eine Antwort in ein neues Ticket aufteilen
- **Ticket-Schlummerfunktion** -- Bis zu einem zukünftigen Datum zurückstellen mit automatischem Aufweck-Hintergrunddienst
- **E-Mail-Threading** -- In-Reply-To/References/Message-ID-Header für korrektes Threading
- **Gespeicherte Ansichten** -- Persönliche und geteilte Filter-Voreinstellungen
- **Einbettbare Widget-API** -- Öffentliche Endpoints für KB-Suche, Gast-Tickets, Statusabfrage
- **Echtzeit-Updates** -- SignalR-Hubs für Live-Ticket-Updates (optional)
- **Kapazitätsverwaltung** -- Arbeitslastlimits pro Agent und Kanal
- **Skill-basiertes Routing** -- Agenten per Skill-Tags Tickets zuordnen
- **CSAT-Bewertungen** -- Zufriedenheitsumfragen bei gelösten Tickets
- **2FA** -- TOTP-Einrichtung und -Verifizierung mit Wiederherstellungscodes
- **Gastzugang** -- Anonyme Ticket-Erstellung mit Magic-Token-Suche
- **Inertia.js + Vue 3 UI** -- Geteiltes Frontend über [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Voraussetzungen

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite oder PostgreSQL
- Node.js 18+ (für Frontend-Assets)

## Schnellstart

### 1. Paket Installieren

```bash
dotnet add package Escalated
```

### 2. Services Registrieren

```csharp
// Program.cs
using Escalated.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEscalated(builder.Configuration, options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Escalated")));

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.MapEscalated();

app.Run();
```

### 3. Konfigurieren

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Escalated": "Server=localhost;Database=MyApp;Trusted_Connection=true;"
  },
  "Escalated": {
    "RoutePrefix": "support",
    "TicketReferencePrefix": "ESC",
    "DefaultPriority": "medium",
    "AllowCustomerClose": true,
    "AutoCloseResolvedAfterDays": 7,
    "Sla": {
      "Enabled": true,
      "BusinessHoursOnly": false,
      "BusinessHours": {
        "Start": "09:00",
        "End": "17:00",
        "Timezone": "UTC",
        "Days": [1, 2, 3, 4, 5]
      }
    },
    "EnableRealTime": false
  }
}
```

### 4. Migrationen Ausführen

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Besuchen Sie `/support` -- es ist live.

## Frontend-Integration

Escalated liefert eine Vue-Komponentenbibliothek und Standardseiten über das npm-Paket [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Integrieren Sie mit Inertia.js für nahtloses SPA-Rendering in Ihrem bestehenden Layout.

```bash
npm install @escalated-dev/escalated
```

## Architektur

```
src/Escalated/
  Models/           # Über 40 EF Core-Entitätsmodelle
  Data/             # EscalatedDbContext mit vollständigem Beziehungsmapping
  Services/         # Geschäftslogik (Ticket, SLA, Zusammenführung, Aufteilung, Schlummern, etc.)
  Controllers/
    Admin/          # Admin-Panel-API (CRUD für alle Einstellungen)
    Agent/          # Ticket-Warteschlange und Agenten-Aktionen
    Customer/       # Kunden-Self-Service-Portal
    Widget/         # Öffentliche Widget-API (KB-Suche, Gast-Tickets)
  Middleware/       # API-Token-Authentifizierung, Berechtigungen, Ratenbegrenzung
  Events/           # Domänen-Events (TicketCreated, SlaBreached, etc.)
  Notifications/    # E-Mail-Benachrichtigungs-Interfaces und -Vorlagen
  Configuration/    # DI-Registrierung, Optionen, Endpoint-Mapping
  Hubs/             # SignalR-Hub für Echtzeit-Updates
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modelle

Escalated enthält über 40 EF Core-Entitäten, die die gesamte Helpdesk-Domäne abdecken:

| Kategorie | Modelle |
|----------|--------|
| Kern | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agenten | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Messaging | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Administration | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Benutzerdefiniert | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Import | ImportJob, ImportSourceMap |
| Konfiguration | EscalatedSettings, SavedView |
| Wissensdatenbank | Article, ArticleCategory |

Alle Modelle haben korrekt konfigurierte Beziehungen, Indizes und Abfragefilter in `EscalatedDbContext`.

## Services

| Service | Verantwortung |
|---------|---------------|
| `TicketService` | Vollständiges Ticket-CRUD, Statusübergänge, Antworten, Tags, Abteilungen |
| `SlaService` | Richtlinienzuweisung, Verletzungserkennung, Warnungsprüfung, Aufzeichnung der ersten Antwort |
| `AssignmentService` | Agenten-Zuweisung, -Aufhebung, Auto-Zuweisung nach Arbeitslast |
| `EscalationService` | Bewertung bedingungsbasierter Regeln, Ausführung von Eskalationsaktionen |
| `AutomationRunner` | Bewertung zeitbasierter Automatisierungen und Aktionsausführung |
| `MacroService` | Makro-Aktionssequenzen auf Tickets anwenden |
| `TicketMergeService` | Quelle in Ziel zusammenführen mit Antwort-Transfer |
| `TicketSplitService` | Antwort in ein neues verknüpftes Ticket aufteilen |
| `TicketSnoozeService` | Schlummern/Aufwecken mit Hintergrund-Aufweckdienst |
| `WebhookDispatcher` | HMAC-signierte Webhook-Zustellung mit Wiederholungslogik |
| `CapacityService` | Gleichzeitige Ticket-Limits pro Agent |
| `SkillRoutingService` | Agenten per Skills Ticket-Tags zuordnen |
| `BusinessHoursCalculator` | Geschäftszeiten-Datumsberechnung mit Feiertagsunterstützung |
| `TwoFactorService` | TOTP-Secret-Generierung, Verifizierung, Wiederherstellungscodes |
| `AuditLogService` | Entitätsmutationen protokollieren und abfragen |
| `KnowledgeBaseService` | Artikel-/Kategorie-CRUD, Suche, Feedback |
| `SavedViewService` | Persönliche und geteilte Filter-Voreinstellungen |
| `SideConversationService` | Interne Thread-Konversationen an Tickets |
| `ImportService` | Mehrstufiger Import mit steckbaren Adaptern |
| `SettingsService` | Schlüssel-Wert-Einstellungsspeicher |

## Ereignisse

Jede Ticket-Aktion löst ein Domänen-Event aus:

| Ereignis | Wann |
|-------|------|
| `TicketCreatedEvent` | Neues Ticket erstellt |
| `TicketStatusChangedEvent` | Statusübergang |
| `TicketAssignedEvent` | Agent zugewiesen |
| `TicketUnassignedEvent` | Agent entfernt |
| `ReplyCreatedEvent` | Öffentliche Antwort hinzugefügt |
| `InternalNoteAddedEvent` | Agenten-Notiz hinzugefügt |
| `SlaBreachedEvent` | SLA-Frist überschritten |
| `SlaWarningEvent` | SLA-Frist nähert sich |
| `TicketEscalatedEvent` | Ticket eskaliert |
| `TicketResolvedEvent` | Ticket gelöst |
| `TicketClosedEvent` | Ticket geschlossen |
| `TicketReopenedEvent` | Ticket wiedereröffnet |
| `TicketPriorityChangedEvent` | Priorität geändert |
| `DepartmentChangedEvent` | Abteilung geändert |
| `TagAddedEvent` | Tag hinzugefügt |
| `TagRemovedEvent` | Tag entfernt |

Implementieren Sie `IEscalatedEventDispatcher`, um diese Events in Ihrer Host-Anwendung zu empfangen:

```csharp
public class MyEventHandler : IEscalatedEventDispatcher
{
    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : class
    {
        if (@event is TicketCreatedEvent created)
        {
            // Handle new ticket
        }
    }
}

// Register in DI
services.AddSingleton<IEscalatedEventDispatcher, MyEventHandler>();
```

## API-Endpoints

### Kunde

| Methode | Route | Beschreibung |
|--------|-------|-------------|
| GET | `/support/tickets` | Kunden-Tickets auflisten |
| POST | `/support/tickets` | Ticket erstellen |
| GET | `/support/tickets/{id}` | Ticket ansehen |
| POST | `/support/tickets/{id}/reply` | Auf Ticket antworten |
| POST | `/support/tickets/{id}/close` | Ticket schließen |
| POST | `/support/tickets/{id}/reopen` | Ticket wiedereröffnen |

### Agent

| Methode | Route | Beschreibung |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Ticket-Warteschlange mit Filtern |
| GET | `/support/agent/tickets/{id}` | Ticket-Detail |
| POST | `/support/agent/tickets/{id}/reply` | Antworten |
| POST | `/support/agent/tickets/{id}/note` | Interne Notiz |
| POST | `/support/agent/tickets/{id}/assign` | Agent zuweisen |
| POST | `/support/agent/tickets/{id}/status` | Status ändern |
| POST | `/support/agent/tickets/{id}/priority` | Priorität ändern |
| POST | `/support/agent/tickets/{id}/macro` | Makro anwenden |
| POST | `/support/agent/tickets/bulk` | Massenaktionen |
| GET | `/support/agent/tickets/dashboard` | Agenten-Arbeitslast |

### Administration

| Methode | Route | Beschreibung |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Abteilungen verwalten |
| GET/POST | `/support/admin/tags` | Tags verwalten |
| GET/POST | `/support/admin/sla-policies` | SLA-Richtlinien verwalten |
| GET/POST | `/support/admin/escalation-rules` | Eskalationsregeln verwalten |
| GET/POST | `/support/admin/webhooks` | Webhooks verwalten |
| GET/POST | `/support/admin/api-tokens` | API-Tokens verwalten |
| GET/POST | `/support/admin/macros` | Makros verwalten |
| GET/POST | `/support/admin/automations` | Automatisierungen verwalten |
| GET/POST | `/support/admin/custom-fields` | Benutzerdefinierte Felder verwalten |
| GET/POST | `/support/admin/business-hours` | Geschäftszeiten |
| GET/POST | `/support/admin/skills` | Skills verwalten |
| GET/POST | `/support/admin/roles` | Rollen verwalten |
| GET | `/support/admin/audit-logs` | Audit-Logs abfragen |
| GET/POST | `/support/admin/settings` | App-Einstellungen |
| POST | `/support/admin/tickets/{id}/merge` | Tickets zusammenführen |
| POST | `/support/admin/tickets/{id}/split` | Ticket aufteilen |
| POST | `/support/admin/tickets/{id}/snooze` | Ticket zurückstellen |
| POST | `/support/admin/tickets/{id}/link` | Tickets verknüpfen |

### Widget (Öffentlich)

| Methode | Route | Beschreibung |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Wissensdatenbank durchsuchen |
| POST | `/support/widget/tickets` | Gast-Ticket erstellen |
| GET | `/support/widget/tickets/{token}` | Per Gast-Token suchen |
| POST | `/support/widget/tickets/{token}/reply` | Gast-Antwort |
| POST | `/support/widget/tickets/{token}/rate` | CSAT-Bewertung abgeben |
| POST | `/support/widget/kb/articles/{id}/feedback` | Artikel-Feedback |

## Echtzeit-Updates

Aktivieren Sie SignalR für Live-Ticket-Updates:

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

Clients treten ticket-spezifischen Gruppen bei, um Updates zu erhalten:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### API-Token-Authentifizierung

Schützen Sie API-Endpoints mit Bearer-Token-Authentifizierung:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Tokens werden als SHA-256-Hashes gespeichert. Erstellen Sie Tokens über den Admin-API-Endpoint.

### Ratenbegrenzung

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Tests

```bash
dotnet test
```

Tests verwenden xUnit mit Moq und dem EF Core InMemory-Provider. Die Abdeckung umfasst:
- Ticket-CRUD und Statusübergänge
- SLA-Verletzungserkennung und Warnungen
- Ticket-Aufteilung, -Zusammenführung und -Schlummern
- Zuweisung und Arbeitslastberechnung
- Webhook-Abonnement-Matching
- 2FA-Secret-Generierung und -Verifizierung
- Kapazitätsverwaltung
- Modellvalidierung und Enum-Verhalten

## Auch Verfügbar Für

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer-Paket
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails Engine
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Wiederverwendbare Django-App
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6 Paket
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core Paket (Sie sind hier)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI-Komponenten

Dieselbe Architektur, dieselbe Vue-UI -- für jedes große Backend-Framework.

## Lizenz

MIT
