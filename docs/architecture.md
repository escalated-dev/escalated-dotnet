# Architecture

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
| `TicketCustomActionTriggeredEvent` | Agent triggered a custom ticket action |

Every event is delivered, in order, to subscribed outbound webhooks, to matching
Workflows, and then to each `IEscalatedEventDispatcher` your host application
registers. Implement one to receive these events:

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

// Register in DI, before or after AddEscalated
services.AddSingleton<IEscalatedEventDispatcher, MyEventHandler>();
```

Registering a dispatcher adds a listener. It does not replace Escalated's own
handling, so webhooks and Workflows keep running, and you can register several.

### Webhook events

Webhooks subscribe to event names (or `*` for all of them):

| Webhook event | Domain event |
|---------------|--------------|
| `ticket.created` | `TicketCreatedEvent` |
| `ticket.updated` | `TicketUpdatedEvent` |
| `ticket.status_changed` | `TicketStatusChangedEvent` |
| `ticket.resolved` | `TicketResolvedEvent` |
| `ticket.closed` | `TicketClosedEvent` |
| `ticket.reopened` | `TicketReopenedEvent` |
| `ticket.assigned` | `TicketAssignedEvent` |
| `ticket.unassigned` | `TicketUnassignedEvent` |
| `ticket.escalated` | `TicketEscalatedEvent` |
| `ticket.priority_changed` | `TicketPriorityChangedEvent` |
| `ticket.department_changed` | `DepartmentChangedEvent` |
| `ticket.tag_added` | `TagAddedEvent` |
| `ticket.tag_removed` | `TagRemovedEvent` |
| `reply.created` | `ReplyCreatedEvent` |
| `note.created` | `InternalNoteAddedEvent` |
| `sla.breached` | `SlaBreachedEvent` |
| `sla.warning` | `SlaWarningEvent` |
| `chat.started` | `ChatSessionStartedEvent` |
| `chat.assigned` | `ChatSessionAcceptedEvent` (an agent accepted, or routing assigned one at start) |
| `chat.message` | `ChatMessageEvent` |
| `chat.ended` | `ChatSessionEndedEvent` |

These are the shared event vocabulary (escalated-developer-context ADR
2026-10-21, `EventNames.Canonical`). Workflow triggers use the same names. Names
from earlier releases are still read: a webhook subscribed to `ticket.tagged`,
`ticket.untagged` or `internal_note.added`, or a Workflow saved with
`ticket.replied`, keeps working and receives the canonical name
(`EventNames.Legacy`). Creating a webhook stores canonical names.

Workflows are managed under `support/admin/workflows`; see [docs/workflows.md](workflows.md).

Each delivery is a signed `POST` (`X-Escalated-Event`, and `X-Escalated-Signature`
when the webhook has a secret) with the body
`{ "event", "payload", "timestamp" }`. `payload.ticket` carries `id`,
`reference`, `subject`, `status` and `priority`; reply events add `payload.reply`,
tag events `payload.tag`, and `ticket.assigned` adds `payload.agent_id`. Chat
events carry `payload.chat` (`session_id`, `ticket_id`, `status`, `agent_id`,
`department_id`; `chat.message` adds `reply_id` and `author_type`), never the
visitor's token or email. Every
attempt is recorded as a `WebhookDelivery`.

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

### Guest endpoint rate limits

The public guest endpoints are rate-limited per client IP out of the box, with
no middleware to add: `POST /support/widget/tickets` allows 5 tickets per IP per
minute and `POST /support/widget/tickets/{token}/reply` 10 replies, each with its
own counter. Every guest route (widget, guest pages and the mobile guest API) also
shares a per-IP budget of 60 requests a minute. A request over the limit gets `429`
with `Retry-After`. Replies are counted before the guest token is checked, so
wrong-token guesses count too.

```json
"Escalated": {
  "GuestRateLimit": { "Enabled": true, "TicketsPerMinute": 5, "RepliesPerMinute": 10, "VerificationsPerMinute": 10, "RequestsPerMinute": 60 }
}
```

The client IP is `HttpContext.Connection.RemoteIpAddress`. **Behind a reverse
proxy or load balancer, configure `ForwardedHeadersOptions` with your trusted
proxies and call `app.UseForwardedHeaders()`**, or every guest shares the proxy's
address. Counters are kept in memory per process; a multi-instance deployment
should register its own shared `IGuestRateLimiter` (e.g. Redis-backed). Set
`Enabled` to `false` only when you already throttle these routes upstream.
