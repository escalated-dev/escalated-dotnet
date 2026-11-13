# API endpoints

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
| GET | `/support/widget/config` | Widget settings and departments |
| GET | `/support/widget/embed.js` | Standalone widget script |
| GET | `/support/widget/articles?q=` | Search knowledge base (`{ articles: [{ title, slug, excerpt }] }`) |
| GET | `/support/widget/articles/{slug}` | Published article (`{ title, slug, body, category }`) |
| GET | `/support/widget/kb/search` | Search knowledge base (older shape) |
| POST | `/support/widget/tickets` | Create guest ticket (`department_id` or `departmentId`) |
| GET | `/support/widget/tickets/{token}` | Lookup by guest token |
| POST | `/support/widget/tickets/{token}/reply` | Guest reply |
| POST | `/support/widget/tickets/{token}/rate` | Submit CSAT rating |
| POST | `/support/widget/kb/articles/{id}/feedback` | Article feedback |
| GET | `/support/widget/chat/availability` | Whether live chat is available |
| POST | `/support/widget/chat/start` | Start a chat; `id` is the visitor's chat token |
| GET | `/support/widget/chat/{id}/messages` | Chat transcript (polled) |
| POST | `/support/widget/chat/{id}/messages` | Visitor message `{ body }` |
| POST | `/support/widget/chat/{id}/typing` | Visitor typing indicator |
| POST | `/support/widget/chat/{id}/end` | End the chat |
| POST | `/support/widget/chat/{id}/rate` | Rate an ended chat `{ rating: 1-5, comment? }` |

Chat routes take the token returned as `id` by `chat/start`; numeric session ids
are answered with 404.

To put the widget on another site, list that site's origin and add the script
tag there; see [embedding the widget](embedding-the-widget.md).
