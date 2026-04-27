<p align="center">
  <a href="docs/translations/README.ar.md">العربية</a> •
  <a href="docs/translations/README.de.md">Deutsch</a> •
  <b>English</b> •
  <a href="docs/translations/README.es.md">Español</a> •
  <a href="docs/translations/README.fr.md">Français</a> •
  <a href="docs/translations/README.it.md">Italiano</a> •
  <a href="docs/translations/README.ja.md">日本語</a> •
  <a href="docs/translations/README.ko.md">한국어</a> •
  <a href="docs/translations/README.nl.md">Nederlands</a> •
  <a href="docs/translations/README.pl.md">Polski</a> •
  <a href="docs/translations/README.pt-BR.md">Português (BR)</a> •
  <a href="docs/translations/README.ru.md">Русский</a> •
  <a href="docs/translations/README.tr.md">Türkçe</a> •
  <a href="docs/translations/README.zh-CN.md">简体中文</a>
</p>

# Escalated for ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A full-featured, embeddable support ticket system for ASP.NET Core. Drop it into any app -- get a complete helpdesk with SLA tracking, escalation rules, agent workflows, and a customer portal. No external services required.

> **[escalated.dev](https://escalated.dev)** -- Learn more, view demos, and compare Cloud vs Self-Hosted options.

## Features

- **Ticket lifecycle** -- Create, assign, reply, resolve, close, reopen with configurable status transitions
- **SLA engine** -- Per-priority response and resolution targets, business hours calculation, automatic breach detection
- **Escalation rules** -- Condition-based rules that auto-escalate, reprioritize, reassign, or notify
- **Automations** -- Time-based rules with conditions and actions
- **Agent dashboard** -- Ticket queue with filters, bulk actions, internal notes, canned responses
- **Customer portal** -- Self-service ticket creation, replies, and status tracking
- **Admin panel** -- Manage departments, SLA policies, escalation rules, tags, and more
- **Macros and canned responses** -- Batch actions and reusable reply templates
- **Custom fields** -- Dynamic metadata with conditional display logic
- **Knowledge base** -- Articles, categories, search, and feedback
- **File attachments** -- Upload support with configurable storage and size limits
- **Activity timeline** -- Full audit log of every action on every ticket
- **Webhooks** -- HMAC-SHA256 signed with retry logic
- **API tokens** -- Bearer auth with ability-based scoping
- **Roles and permissions** -- Fine-grained access control
- **Audit logging** -- All mutations tracked with old/new values
- **Import system** -- Multi-step wizard with pluggable adapters
- **Side conversations** -- Internal team threads on tickets
- **Ticket merging and linking** -- Merge duplicate tickets and relate issues
- **Ticket splitting** -- Split a reply into a new ticket
- **Ticket snooze** -- Snooze until a future date with auto-wake background service
- **Email threading** -- In-Reply-To/References/Message-ID headers for proper threading
- **Inbound email** -- Single webhook endpoint with Postmark + Mailgun + AWS SES parsers, signed Reply-To verification, and Message-ID-based ticket resolution
- **Saved views** -- Personal and shared filter presets
- **Embeddable widget API** -- Public endpoints for KB search, guest tickets, status lookup
- **Real-time updates** -- SignalR hubs for live ticket updates (opt-in)
- **Capacity management** -- Per-agent workload limits by channel
- **Skill-based routing** -- Match agents to tickets by skill tags
- **CSAT ratings** -- Satisfaction surveys on resolved tickets
- **2FA** -- TOTP setup and verification with recovery codes
- **Guest access** -- Anonymous ticket creation with magic token lookup
- **Inertia.js + Vue 3 UI** -- Shared frontend via [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Requirements

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite, or PostgreSQL
- Node.js 18+ (for frontend assets)

## Quick Start

### 1. Install the Package

```bash
dotnet add package Escalated
```

### 2. Register Services

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

### 3. Configure

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
    "EnableRealTime": false,
    "Mail": {
      "Domain": "support.yourapp.com",
      "InboundSecret": "a-long-random-value"
    }
  }
}
```

The `Mail.InboundSecret` is symmetric -- it signs outbound `Reply-To` addresses *and* verifies inbound webhook requests, so forged emails targeting a stolen reply address are rejected via timing-safe HMAC.

### 4. Run Migrations

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Visit `/support` -- you're live.

## Inbound email

Point your Postmark, Mailgun, or AWS SES (via SNS HTTP subscription) inbound webhook at:

```
POST /support/webhook/email/inbound?adapter=postmark
POST /support/webhook/email/inbound?adapter=mailgun
POST /support/webhook/email/inbound?adapter=ses
```

The adapter can be selected via the query parameter or the `X-Escalated-Adapter` header. Your provider must attach the shared secret as an `X-Escalated-Inbound-Secret` header.

The service resolves inbound messages to existing tickets via, in order: canonical `Message-ID` headers, signed `Reply-To` verification, and subject-reference tags. Unmatched messages with real content create a new ticket; SNS subscription confirmations and empty body+subject messages are skipped.

See the [inbound email docs](https://docs.escalated.dev/inbound-email) for provider setup, the response shape, and a ready-to-paste curl test recipe.

## Frontend Integration

Escalated ships a Vue component library and default pages via the [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) npm package. Integrate with Inertia.js for seamless SPA rendering inside your existing layout.

```bash
npm install @escalated-dev/escalated
```

## Architecture

```
src/Escalated/
  Models/           # 40+ EF Core entity models
  Data/             # EscalatedDbContext with full relationship mapping
  Services/         # Business logic (ticket, SLA, merge, split, snooze, etc.)
  Controllers/
    Admin/          # Admin panel API (CRUD for all settings)
    Agent/          # Agent ticket queue and actions
    Customer/       # Customer self-service portal
    Widget/         # Public widget API (KB search, guest tickets)
  Middleware/       # API token auth, permissions, rate limiting
  Events/           # Domain events (TicketCreated, SlaBreached, etc.)
  Notifications/    # Email notification interfaces and templates
  Configuration/    # DI registration, options, endpoint mapping
  Hubs/             # SignalR hub for real-time updates
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Models

Escalated includes 40+ EF Core entities covering the full helpdesk domain:

| Category | Models |
|----------|--------|
| Core | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agents | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Messaging | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Admin | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Custom | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Import | ImportJob, ImportSourceMap |
| Config | EscalatedSettings, SavedView |
| Knowledge Base | Article, ArticleCategory |

All models have proper relationships, indexes, and query filters configured in `EscalatedDbContext`.

## Services

| Service | Responsibility |
|---------|---------------|
| `TicketService` | Full ticket CRUD, status transitions, replies, tags, departments |
| `SlaService` | Policy attachment, breach detection, warning checks, first response recording |
| `AssignmentService` | Agent assignment, unassignment, auto-assign by workload |
| `EscalationService` | Evaluate condition-based rules, execute escalation actions |
| `AutomationRunner` | Time-based automation evaluation and action execution |
| `MacroService` | Apply macro action sequences to tickets |
| `TicketMergeService` | Merge source into target with reply transfer |
| `TicketSplitService` | Split a reply into a new linked ticket |
| `TicketSnoozeService` | Snooze/unsnooze with background wake service |
| `WebhookDispatcher` | HMAC-signed webhook delivery with retry logic |
| `CapacityService` | Per-agent concurrent ticket limits |
| `SkillRoutingService` | Match agents by skills to ticket tags |
| `BusinessHoursCalculator` | Business hours date math with holiday support |
| `TwoFactorService` | TOTP secret generation, verification, recovery codes |
| `AuditLogService` | Log and query entity mutations |
| `KnowledgeBaseService` | Article/category CRUD, search, feedback |
| `SavedViewService` | Personal and shared filter presets |
| `SideConversationService` | Internal threaded conversations on tickets |
| `ImportService` | Multi-step import with pluggable adapters |
| `SettingsService` | Key-value settings store |

## Events

Every ticket action dispatches a domain event:

| Event | When |
|-------|------|
| `TicketCreatedEvent` | New ticket created |
| `TicketStatusChangedEvent` | Status transition |
| `TicketAssignedEvent` | Agent assigned |
| `TicketUnassignedEvent` | Agent removed |
| `ReplyCreatedEvent` | Public reply added |
| `InternalNoteAddedEvent` | Agent note added |
| `SlaBreachedEvent` | SLA deadline missed |
| `SlaWarningEvent` | SLA deadline approaching |
| `TicketEscalatedEvent` | Ticket escalated |
| `TicketResolvedEvent` | Ticket resolved |
| `TicketClosedEvent` | Ticket closed |
| `TicketReopenedEvent` | Ticket reopened |
| `TicketPriorityChangedEvent` | Priority changed |
| `DepartmentChangedEvent` | Department changed |
| `TagAddedEvent` | Tag added |
| `TagRemovedEvent` | Tag removed |

Implement `IEscalatedEventDispatcher` to receive these events in your host application:

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

## API Endpoints

### Customer

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/support/tickets` | List customer tickets |
| POST | `/support/tickets` | Create ticket |
| GET | `/support/tickets/{id}` | View ticket |
| POST | `/support/tickets/{id}/reply` | Reply to ticket |
| POST | `/support/tickets/{id}/close` | Close ticket |
| POST | `/support/tickets/{id}/reopen` | Reopen ticket |

### Agent

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Ticket queue with filters |
| GET | `/support/agent/tickets/{id}` | Ticket detail |
| POST | `/support/agent/tickets/{id}/reply` | Reply |
| POST | `/support/agent/tickets/{id}/note` | Internal note |
| POST | `/support/agent/tickets/{id}/assign` | Assign agent |
| POST | `/support/agent/tickets/{id}/status` | Change status |
| POST | `/support/agent/tickets/{id}/priority` | Change priority |
| POST | `/support/agent/tickets/{id}/macro` | Apply macro |
| POST | `/support/agent/tickets/bulk` | Bulk actions |
| GET | `/support/agent/tickets/dashboard` | Agent workload |

### Admin

| Method | Route | Description |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Manage departments |
| GET/POST | `/support/admin/tags` | Manage tags |
| GET/POST | `/support/admin/sla-policies` | Manage SLA policies |
| GET/POST | `/support/admin/escalation-rules` | Manage escalation rules |
| GET/POST | `/support/admin/webhooks` | Manage webhooks |
| GET/POST | `/support/admin/api-tokens` | Manage API tokens |
| GET/POST | `/support/admin/macros` | Manage macros |
| GET/POST | `/support/admin/automations` | Manage automations |
| GET/POST | `/support/admin/custom-fields` | Manage custom fields |
| GET/POST | `/support/admin/business-hours` | Business schedules |
| GET/POST | `/support/admin/skills` | Manage skills |
| GET/POST | `/support/admin/roles` | Manage roles |
| GET | `/support/admin/audit-logs` | Query audit logs |
| GET/POST | `/support/admin/settings` | App settings |
| GET/PUT | `/support/admin/settings/public-tickets` | Runtime guest-policy mode (unassigned / guest_user / prompt_signup). See [docs.escalated.dev/public-tickets](https://docs.escalated.dev/public-tickets). |
| POST | `/support/admin/tickets/{id}/merge` | Merge tickets |
| POST | `/support/admin/tickets/{id}/split` | Split ticket |
| POST | `/support/admin/tickets/{id}/snooze` | Snooze ticket |
| POST | `/support/admin/tickets/{id}/link` | Link tickets |

### Widget (Public)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Search knowledge base |
| POST | `/support/widget/tickets` | Create guest ticket |
| GET | `/support/widget/tickets/{token}` | Lookup by guest token |
| POST | `/support/widget/tickets/{token}/reply` | Guest reply |
| POST | `/support/widget/tickets/{token}/rate` | Submit CSAT rating |
| POST | `/support/widget/kb/articles/{id}/feedback` | Article feedback |

## Real-time Updates

Enable SignalR for live ticket updates:

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

Clients join ticket-specific groups to receive updates:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### API Token Authentication

Protect API endpoints with bearer token authentication:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Tokens are stored as SHA-256 hashes. Create tokens via the admin API endpoint.

### Rate Limiting

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Testing

```bash
dotnet test
```

Tests use xUnit with Moq and EF Core InMemory provider. Coverage includes:
- Ticket CRUD and status transitions
- SLA breach detection and warnings
- Ticket split, merge, and snooze
- Assignment and workload calculation
- Webhook subscription matching
- 2FA secret generation and verification
- Capacity management
- Model validation and enum behavior

## Also Available For

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer package
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails engine
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Django reusable app
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6 package
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core package (you are here)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI components

Same architecture, same Vue UI -- for every major backend framework.

## License

MIT
