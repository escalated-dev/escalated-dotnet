<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <b>Italiano</b> •
  <a href="README.ja.md">日本語</a> •
  <a href="README.ko.md">한국어</a> •
  <a href="README.nl.md">Nederlands</a> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated per ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Un sistema completo di ticket di supporto integrabile per ASP.NET Core. Aggiungilo a qualsiasi app e ottieni un helpdesk completo con tracciamento SLA, regole di escalation, workflow degli agenti e portale clienti. Nessun servizio esterno richiesto.

> **[escalated.dev](https://escalated.dev)** -- Scopri di più, guarda le demo e confronta le opzioni Cloud e Self-Hosted.

## Funzionalità

- **Ciclo di vita del ticket** -- Creare, assegnare, rispondere, risolvere, chiudere, riaprire con transizioni di stato configurabili
- **Motore SLA** -- Obiettivi di risposta e risoluzione per priorità, calcolo delle ore lavorative, rilevamento automatico delle violazioni
- **Regole di escalation** -- Regole basate su condizioni che escalano, ripriorizzano, riassegnano o notificano automaticamente
- **Automazioni** -- Regole basate sul tempo con condizioni e azioni
- **Dashboard dell'agente** -- Coda ticket con filtri, azioni di massa, note interne, risposte predefinite
- **Portale clienti** -- Creazione ticket self-service, risposte e tracciamento dello stato
- **Pannello di amministrazione** -- Gestire reparti, policy SLA, regole di escalation, tag e altro
- **Macro e risposte predefinite** -- Azioni in blocco e modelli di risposta riutilizzabili
- **Campi personalizzati** -- Metadati dinamici con logica di visualizzazione condizionale
- **Base di conoscenza** -- Articoli, categorie, ricerca e feedback
- **Allegati** -- Supporto caricamento con archiviazione configurabile e limiti di dimensione
- **Timeline delle attività** -- Log di audit completo di ogni azione su ogni ticket
- **Webhooks** -- Firmati con HMAC-SHA256 con logica di retry
- **Token API** -- Autenticazione Bearer con ambito basato sulle capacità
- **Ruoli e permessi** -- Controllo degli accessi granulare
- **Log di audit** -- Tutte le mutazioni registrate con valori vecchi/nuovi
- **Sistema di importazione** -- Procedura guidata multi-step con adattatori collegabili
- **Conversazioni laterali** -- Thread interni del team sui ticket
- **Unione e collegamento ticket** -- Unire ticket duplicati e collegare problemi
- **Divisione ticket** -- Dividere una risposta in un nuovo ticket
- **Sospensione ticket** -- Sospendere fino a una data futura con servizio di risveglio in background
- **Threading email** -- Header In-Reply-To/References/Message-ID per un threading corretto
- **Viste salvate** -- Preset di filtri personali e condivisi
- **API widget integrabile** -- Endpoint pubblici per ricerca KB, ticket ospiti, consultazione stato
- **Aggiornamenti in tempo reale** -- Hub SignalR per aggiornamenti ticket in diretta (opzionale)
- **Gestione della capacità** -- Limiti di carico di lavoro per agente e per canale
- **Routing basato sulle competenze** -- Assegnare agenti ai ticket per tag di competenze
- **Valutazioni CSAT** -- Sondaggi di soddisfazione sui ticket risolti
- **2FA** -- Configurazione e verifica TOTP con codici di recupero
- **Accesso ospite** -- Creazione anonima di ticket con ricerca tramite token magico
- **Inertia.js + Vue 3 UI** -- Frontend condiviso tramite [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Requisiti

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite o PostgreSQL
- Node.js 18+ (per le risorse frontend)

## Avvio Rapido

### 1. Installare il Pacchetto

```bash
dotnet add package Escalated
```

### 2. Registrare i Servizi

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

### 3. Configurare

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

### 4. Eseguire le Migrazioni

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Visita `/support` -- sei online.

## Integrazione Frontend

Escalated fornisce una libreria di componenti Vue e pagine predefinite tramite il pacchetto npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Integra con Inertia.js per un rendering SPA senza interruzioni nel tuo layout esistente.

```bash
npm install @escalated-dev/escalated
```

## Architettura

```
src/Escalated/
  Models/           # Oltre 40 modelli di entità EF Core
  Data/             # EscalatedDbContext con mappatura completa delle relazioni
  Services/         # Logica di business (ticket, SLA, unione, divisione, sospensione, ecc.)
  Controllers/
    Admin/          # API del pannello admin (CRUD per tutte le impostazioni)
    Agent/          # Coda ticket e azioni dell'agente
    Customer/       # Portale self-service del cliente
    Widget/         # API pubblica del widget (ricerca KB, ticket ospiti)
  Middleware/       # Autenticazione token API, permessi, limitazione del tasso
  Events/           # Eventi di dominio (TicketCreated, SlaBreached, ecc.)
  Notifications/    # Interfacce e modelli di notifica email
  Configuration/    # Registrazione DI, opzioni, mappatura endpoint
  Hubs/             # Hub SignalR per aggiornamenti in tempo reale
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modelli

Escalated include oltre 40 entità EF Core che coprono l'intero dominio helpdesk:

| Categoria | Modelli |
|----------|--------|
| Principale | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agenti | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Messaggistica | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Amministrazione | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Personalizzato | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Importazione | ImportJob, ImportSourceMap |
| Configurazione | EscalatedSettings, SavedView |
| Base di Conoscenza | Article, ArticleCategory |

Tutti i modelli hanno relazioni, indici e filtri di query correttamente configurati in `EscalatedDbContext`.

## Servizi

| Servizio | Responsabilità |
|---------|---------------|
| `TicketService` | CRUD completo dei ticket, transizioni di stato, risposte, tag, reparti |
| `SlaService` | Assegnazione policy, rilevamento violazioni, verifica avvisi, registrazione prima risposta |
| `AssignmentService` | Assegnazione agenti, rimozione assegnazione, auto-assegnazione per carico di lavoro |
| `EscalationService` | Valutazione regole basate su condizioni, esecuzione azioni di escalation |
| `AutomationRunner` | Valutazione automazioni basate sul tempo ed esecuzione azioni |
| `MacroService` | Applicare sequenze di azioni macro ai ticket |
| `TicketMergeService` | Unire origine in destinazione con trasferimento risposte |
| `TicketSplitService` | Dividere una risposta in un nuovo ticket collegato |
| `TicketSnoozeService` | Sospendere/risvegliare con servizio di risveglio in background |
| `WebhookDispatcher` | Consegna webhook firmati HMAC con logica di retry |
| `CapacityService` | Limiti di ticket simultanei per agente |
| `SkillRoutingService` | Assegnare agenti per competenze ai tag dei ticket |
| `BusinessHoursCalculator` | Calcoli date in ore lavorative con supporto festivi |
| `TwoFactorService` | Generazione secret TOTP, verifica, codici di recupero |
| `AuditLogService` | Registrare e interrogare mutazioni delle entità |
| `KnowledgeBaseService` | CRUD articoli/categorie, ricerca, feedback |
| `SavedViewService` | Preset di filtri personali e condivisi |
| `SideConversationService` | Conversazioni interne con thread sui ticket |
| `ImportService` | Importazione multi-step con adattatori collegabili |
| `SettingsService` | Archivio impostazioni chiave-valore |

## Eventi

Ogni azione del ticket emette un evento di dominio:

| Evento | Quando |
|-------|------|
| `TicketCreatedEvent` | Nuovo ticket creato |
| `TicketStatusChangedEvent` | Transizione di stato |
| `TicketAssignedEvent` | Agente assegnato |
| `TicketUnassignedEvent` | Agente rimosso |
| `ReplyCreatedEvent` | Risposta pubblica aggiunta |
| `InternalNoteAddedEvent` | Nota dell'agente aggiunta |
| `SlaBreachedEvent` | Scadenza SLA superata |
| `SlaWarningEvent` | Scadenza SLA in avvicinamento |
| `TicketEscalatedEvent` | Ticket escalato |
| `TicketResolvedEvent` | Ticket risolto |
| `TicketClosedEvent` | Ticket chiuso |
| `TicketReopenedEvent` | Ticket riaperto |
| `TicketPriorityChangedEvent` | Priorità modificata |
| `DepartmentChangedEvent` | Reparto modificato |
| `TagAddedEvent` | Tag aggiunto |
| `TagRemovedEvent` | Tag rimosso |

Implementa `IEscalatedEventDispatcher` per ricevere questi eventi nella tua applicazione host:

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

## Endpoint API

### Cliente

| Metodo | Percorso | Descrizione |
|--------|-------|-------------|
| GET | `/support/tickets` | Elencare i ticket del cliente |
| POST | `/support/tickets` | Creare un ticket |
| GET | `/support/tickets/{id}` | Visualizzare il ticket |
| POST | `/support/tickets/{id}/reply` | Rispondere al ticket |
| POST | `/support/tickets/{id}/close` | Chiudere il ticket |
| POST | `/support/tickets/{id}/reopen` | Riaprire il ticket |

### Agente

| Metodo | Percorso | Descrizione |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Coda ticket con filtri |
| GET | `/support/agent/tickets/{id}` | Dettaglio del ticket |
| POST | `/support/agent/tickets/{id}/reply` | Rispondere |
| POST | `/support/agent/tickets/{id}/note` | Nota interna |
| POST | `/support/agent/tickets/{id}/assign` | Assegnare agente |
| POST | `/support/agent/tickets/{id}/status` | Cambiare stato |
| POST | `/support/agent/tickets/{id}/priority` | Cambiare priorità |
| POST | `/support/agent/tickets/{id}/macro` | Applicare macro |
| POST | `/support/agent/tickets/bulk` | Azioni di massa |
| GET | `/support/agent/tickets/dashboard` | Carico di lavoro dell'agente |

### Amministrazione

| Metodo | Percorso | Descrizione |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Gestire i reparti |
| GET/POST | `/support/admin/tags` | Gestire i tag |
| GET/POST | `/support/admin/sla-policies` | Gestire le policy SLA |
| GET/POST | `/support/admin/escalation-rules` | Gestire le regole di escalation |
| GET/POST | `/support/admin/webhooks` | Gestire i webhooks |
| GET/POST | `/support/admin/api-tokens` | Gestire i token API |
| GET/POST | `/support/admin/macros` | Gestire le macro |
| GET/POST | `/support/admin/automations` | Gestire le automazioni |
| GET/POST | `/support/admin/custom-fields` | Gestire i campi personalizzati |
| GET/POST | `/support/admin/business-hours` | Orari di lavoro |
| GET/POST | `/support/admin/skills` | Gestire le competenze |
| GET/POST | `/support/admin/roles` | Gestire i ruoli |
| GET | `/support/admin/audit-logs` | Consultare i log di audit |
| GET/POST | `/support/admin/settings` | Impostazioni dell'applicazione |
| POST | `/support/admin/tickets/{id}/merge` | Unire ticket |
| POST | `/support/admin/tickets/{id}/split` | Dividere ticket |
| POST | `/support/admin/tickets/{id}/snooze` | Sospendere ticket |
| POST | `/support/admin/tickets/{id}/link` | Collegare ticket |

### Widget (Pubblico)

| Metodo | Percorso | Descrizione |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Cercare nella base di conoscenza |
| POST | `/support/widget/tickets` | Creare ticket ospite |
| GET | `/support/widget/tickets/{token}` | Cercare per token ospite |
| POST | `/support/widget/tickets/{token}/reply` | Risposta ospite |
| POST | `/support/widget/tickets/{token}/rate` | Inviare valutazione CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Feedback articolo |

## Aggiornamenti in Tempo Reale

Abilita SignalR per aggiornamenti ticket in diretta:

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

I client si uniscono a gruppi specifici per ticket per ricevere aggiornamenti:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Autenticazione Token API

Proteggi gli endpoint API con autenticazione token Bearer:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

I token vengono archiviati come hash SHA-256. Crea i token tramite l'endpoint di amministrazione API.

### Limitazione del Tasso

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Test

```bash
dotnet test
```

I test utilizzano xUnit con Moq e il provider InMemory di EF Core. La copertura include:
- CRUD dei ticket e transizioni di stato
- Rilevamento violazioni e avvisi SLA
- Divisione, unione e sospensione dei ticket
- Assegnazione e calcolo del carico di lavoro
- Corrispondenza sottoscrizioni webhook
- Generazione e verifica secret 2FA
- Gestione della capacità
- Validazione modelli e comportamento enum

## Disponibile Anche Per

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Pacchetto Composer per Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Engine Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- App riutilizzabile Django
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Pacchetto AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Pacchetto ASP.NET Core (sei qui)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Componenti UI Vue 3 + Inertia.js

La stessa architettura, la stessa UI Vue -- per ogni principale framework backend.

## Licenza

MIT
