<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <a href="README.it.md">Italiano</a> •
  <a href="README.ja.md">日本語</a> •
  <a href="README.ko.md">한국어</a> •
  <b>Nederlands</b> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated voor ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Een volledig uitgerust, inbedbaar supportticketsysteem voor ASP.NET Core. Voeg het toe aan elke app en krijg een complete helpdesk met SLA-tracking, escalatieregels, agentworkflows en een klantenportaal. Geen externe diensten vereist.

> **[escalated.dev](https://escalated.dev)** -- Meer informatie, bekijk demo's en vergelijk Cloud- en Self-Hosted-opties.

## Functies

- **Ticketlevenscyclus** -- Aanmaken, toewijzen, beantwoorden, oplossen, sluiten, heropenen met configureerbare statusovergangen
- **SLA-engine** -- Respons- en oplossingsdoelen per prioriteit, berekening van kantooruren, automatische schendingsdetectie
- **Escalatieregels** -- Voorwaardelijke regels die automatisch escaleren, herprioriteren, hertoewijzen of notificeren
- **Automatiseringen** -- Tijdgebaseerde regels met voorwaarden en acties
- **Agentdashboard** -- Ticketwachtrij met filters, bulkacties, interne notities, standaardantwoorden
- **Klantenportaal** -- Zelfbediening voor ticketaanmaak, antwoorden en statustracking
- **Beheerpaneel** -- Beheer afdelingen, SLA-beleid, escalatieregels, tags en meer
- **Macro's en standaardantwoorden** -- Batchacties en herbruikbare antwoordsjablonen
- **Aangepaste velden** -- Dynamische metadata met conditionele weergavelogica
- **Kennisbank** -- Artikelen, categorieën, zoeken en feedback
- **Bestandsbijlagen** -- Uploadondersteuning met configureerbare opslag en groottelimieten
- **Activiteitstijdlijn** -- Volledig auditlogboek van elke actie op elk ticket
- **Webhooks** -- HMAC-SHA256-ondertekend met herhaalpoginglogica
- **API-tokens** -- Bearer-authenticatie met op capaciteiten gebaseerde scoping
- **Rollen en machtigingen** -- Fijnmazig toegangsbeheer
- **Auditlogboek** -- Alle mutaties bijgehouden met oude/nieuwe waarden
- **Importsysteem** -- Meerstaps-wizard met pluggable adapters
- **Zijgesprekken** -- Interne teamthreads op tickets
- **Ticket samenvoegen en koppelen** -- Duplicaattickets samenvoegen en problemen relateren
- **Ticket splitsen** -- Een antwoord afsplitsen naar een nieuw ticket
- **Ticket snoozen** -- Snoozen tot een toekomstige datum met achtergrond-wekservice
- **E-mailthreading** -- In-Reply-To/References/Message-ID-headers voor correcte threading
- **Opgeslagen weergaven** -- Persoonlijke en gedeelde filterpresets
- **Inbedbare widget-API** -- Publieke endpoints voor KB-zoeken, gasttickets, statusopvraag
- **Realtime-updates** -- SignalR-hubs voor live ticketupdates (opt-in)
- **Capaciteitsbeheer** -- Werklastlimieten per agent per kanaal
- **Skill-gebaseerde routering** -- Agents matchen aan tickets op basis van skilltags
- **CSAT-beoordelingen** -- Tevredenheidsenquêtes bij opgeloste tickets
- **2FA** -- TOTP-instelling en -verificatie met herstelcodes
- **Gasttoegang** -- Anonieme ticketaanmaak met magische tokenopzoeking
- **Inertia.js + Vue 3 UI** -- Gedeelde frontend via [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Vereisten

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite of PostgreSQL
- Node.js 18+ (voor frontend-assets)

## Snelstart

### 1. Pakket Installeren

```bash
dotnet add package Escalated
```

### 2. Services Registreren

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

### 3. Configureren

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

### 4. Migraties Uitvoeren

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Bezoek `/support` -- het draait.

## Frontend-integratie

Escalated levert een Vue-componentbibliotheek en standaardpagina's via het npm-pakket [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Integreer met Inertia.js voor naadloze SPA-rendering binnen uw bestaande layout.

```bash
npm install @escalated-dev/escalated
```

## Architectuur

```
src/Escalated/
  Models/           # 40+ EF Core-entiteitsmodellen
  Data/             # EscalatedDbContext met volledige relatiemapping
  Services/         # Bedrijfslogica (ticket, SLA, samenvoegen, splitsen, snoozen, enz.)
  Controllers/
    Admin/          # Beheerpaneel-API (CRUD voor alle instellingen)
    Agent/          # Ticketwachtrij en agentacties
    Customer/       # Zelfbedieningsportaal voor klanten
    Widget/         # Publieke widget-API (KB-zoeken, gasttickets)
  Middleware/       # API-tokenauthenticatie, machtigingen, snelheidsbeperking
  Events/           # Domeingebeurtenissen (TicketCreated, SlaBreached, enz.)
  Notifications/    # E-mailnotificatie-interfaces en -sjablonen
  Configuration/    # DI-registratie, opties, endpointmapping
  Hubs/             # SignalR-hub voor realtime-updates
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modellen

Escalated bevat 40+ EF Core-entiteiten die het volledige helpdeskdomein dekken:

| Categorie | Modellen |
|----------|--------|
| Kern | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agents | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Berichten | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Beheer | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Aangepast | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Import | ImportJob, ImportSourceMap |
| Configuratie | EscalatedSettings, SavedView |
| Kennisbank | Article, ArticleCategory |

Alle modellen hebben correct geconfigureerde relaties, indexen en queryfilters in `EscalatedDbContext`.

## Services

| Service | Verantwoordelijkheid |
|---------|---------------|
| `TicketService` | Volledig ticket-CRUD, statusovergangen, antwoorden, tags, afdelingen |
| `SlaService` | Beleidskoppeling, schendingsdetectie, waarschuwingscontrole, registratie eerste respons |
| `AssignmentService` | Agenttoewijzing, -ontoewijzing, automatische toewijzing op werklast |
| `EscalationService` | Evaluatie van voorwaardelijke regels, uitvoering van escalatieacties |
| `AutomationRunner` | Evaluatie van tijdgebaseerde automatiseringen en actie-uitvoering |
| `MacroService` | Macroactiesequenties toepassen op tickets |
| `TicketMergeService` | Bron samenvoegen in doel met antwoordoverdracht |
| `TicketSplitService` | Antwoord afsplitsen naar een nieuw gekoppeld ticket |
| `TicketSnoozeService` | Snoozen/wekken met achtergrond-wekservice |
| `WebhookDispatcher` | HMAC-ondertekende webhooklevering met herhaalpoginglogica |
| `CapacityService` | Gelijktijdige ticketlimieten per agent |
| `SkillRoutingService` | Agents matchen op skills aan tickettags |
| `BusinessHoursCalculator` | Kantoortijden-datumberekening met feestdagondersteuning |
| `TwoFactorService` | TOTP-geheimgeneratie, verificatie, herstelcodes |
| `AuditLogService` | Entiteitsmutaties loggen en opvragen |
| `KnowledgeBaseService` | Artikel-/categorie-CRUD, zoeken, feedback |
| `SavedViewService` | Persoonlijke en gedeelde filterpresets |
| `SideConversationService` | Interne threadgesprekken op tickets |
| `ImportService` | Meerstapsimport met pluggable adapters |
| `SettingsService` | Sleutel-waarde instellingenopslag |

## Gebeurtenissen

Elke ticketactie verstuurt een domeingebeurtenis:

| Gebeurtenis | Wanneer |
|-------|------|
| `TicketCreatedEvent` | Nieuw ticket aangemaakt |
| `TicketStatusChangedEvent` | Statusovergang |
| `TicketAssignedEvent` | Agent toegewezen |
| `TicketUnassignedEvent` | Agent verwijderd |
| `ReplyCreatedEvent` | Publiek antwoord toegevoegd |
| `InternalNoteAddedEvent` | Agentnotitie toegevoegd |
| `SlaBreachedEvent` | SLA-deadline overschreden |
| `SlaWarningEvent` | SLA-deadline nadert |
| `TicketEscalatedEvent` | Ticket geëscaleerd |
| `TicketResolvedEvent` | Ticket opgelost |
| `TicketClosedEvent` | Ticket gesloten |
| `TicketReopenedEvent` | Ticket heropend |
| `TicketPriorityChangedEvent` | Prioriteit gewijzigd |
| `DepartmentChangedEvent` | Afdeling gewijzigd |
| `TagAddedEvent` | Tag toegevoegd |
| `TagRemovedEvent` | Tag verwijderd |

Implementeer `IEscalatedEventDispatcher` om deze gebeurtenissen in uw hostapplicatie te ontvangen:

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

## API-endpoints

### Klant

| Methode | Route | Beschrijving |
|--------|-------|-------------|
| GET | `/support/tickets` | Klanttickets weergeven |
| POST | `/support/tickets` | Ticket aanmaken |
| GET | `/support/tickets/{id}` | Ticket bekijken |
| POST | `/support/tickets/{id}/reply` | Ticket beantwoorden |
| POST | `/support/tickets/{id}/close` | Ticket sluiten |
| POST | `/support/tickets/{id}/reopen` | Ticket heropenen |

### Agent

| Methode | Route | Beschrijving |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Ticketwachtrij met filters |
| GET | `/support/agent/tickets/{id}` | Ticketdetail |
| POST | `/support/agent/tickets/{id}/reply` | Beantwoorden |
| POST | `/support/agent/tickets/{id}/note` | Interne notitie |
| POST | `/support/agent/tickets/{id}/assign` | Agent toewijzen |
| POST | `/support/agent/tickets/{id}/status` | Status wijzigen |
| POST | `/support/agent/tickets/{id}/priority` | Prioriteit wijzigen |
| POST | `/support/agent/tickets/{id}/macro` | Macro toepassen |
| POST | `/support/agent/tickets/bulk` | Bulkacties |
| GET | `/support/agent/tickets/dashboard` | Agentwerklast |

### Beheer

| Methode | Route | Beschrijving |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Afdelingen beheren |
| GET/POST | `/support/admin/tags` | Tags beheren |
| GET/POST | `/support/admin/sla-policies` | SLA-beleid beheren |
| GET/POST | `/support/admin/escalation-rules` | Escalatieregels beheren |
| GET/POST | `/support/admin/webhooks` | Webhooks beheren |
| GET/POST | `/support/admin/api-tokens` | API-tokens beheren |
| GET/POST | `/support/admin/macros` | Macro's beheren |
| GET/POST | `/support/admin/automations` | Automatiseringen beheren |
| GET/POST | `/support/admin/custom-fields` | Aangepaste velden beheren |
| GET/POST | `/support/admin/business-hours` | Kantooruren |
| GET/POST | `/support/admin/skills` | Skills beheren |
| GET/POST | `/support/admin/roles` | Rollen beheren |
| GET | `/support/admin/audit-logs` | Auditlogs opvragen |
| GET/POST | `/support/admin/settings` | App-instellingen |
| POST | `/support/admin/tickets/{id}/merge` | Tickets samenvoegen |
| POST | `/support/admin/tickets/{id}/split` | Ticket splitsen |
| POST | `/support/admin/tickets/{id}/snooze` | Ticket snoozen |
| POST | `/support/admin/tickets/{id}/link` | Tickets koppelen |

### Widget (Publiek)

| Methode | Route | Beschrijving |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Kennisbank doorzoeken |
| POST | `/support/widget/tickets` | Gastticket aanmaken |
| GET | `/support/widget/tickets/{token}` | Opzoeken via gasttoken |
| POST | `/support/widget/tickets/{token}/reply` | Gastantwoord |
| POST | `/support/widget/tickets/{token}/rate` | CSAT-beoordeling indienen |
| POST | `/support/widget/kb/articles/{id}/feedback` | Artikelfeedback |

## Realtime-updates

Activeer SignalR voor live ticketupdates:

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

Clients treden toe tot ticketspecifieke groepen om updates te ontvangen:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### API-tokenauthenticatie

Bescherm API-endpoints met Bearer-tokenauthenticatie:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Tokens worden opgeslagen als SHA-256-hashes. Maak tokens aan via het beheer-API-endpoint.

### Snelheidsbeperking

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Testen

```bash
dotnet test
```

Tests gebruiken xUnit met Moq en de EF Core InMemory-provider. Dekking omvat:
- Ticket-CRUD en statusovergangen
- SLA-schendingsdetectie en waarschuwingen
- Ticket splitsen, samenvoegen en snoozen
- Toewijzing en werklastberekening
- Webhookabonnement-matching
- 2FA-geheimgeneratie en -verificatie
- Capaciteitsbeheer
- Modelvalidatie en enum-gedrag

## Ook Beschikbaar Voor

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer-pakket
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails-engine
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Herbruikbare Django-app
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6-pakket
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core-pakket (u bent hier)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI-componenten

Dezelfde architectuur, dezelfde Vue-UI -- voor elk groot backend-framework.

## Licentie

MIT
