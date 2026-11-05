# Agent panel data

The shared frontend's agent panel (`@escalated-dev/escalated`, `docs/agent-panel.md`) reads five things this package provides, as escalated-dev/escalated-developer-context ADR `2026-11-05-agent-panel-data` specifies. The `AgentPanelData` migration (SQLite, PostgreSQL and SQL Server; `MigrateEscalatedDatabaseAsync()` or `dotnet ef database update`) adds `escalated_tickets.LastCustomerActivityAt`, `escalated_agent_profiles.ChatStatus` (default `offline`) and the `escalated_ticket_reads`, `escalated_customer_presence` and `escalated_notifications` tables.

## Agent status

- `PUT` or `POST /support/agent/status` (`escalated.agent.status`) with `{"status": "online" | "away" | "offline"}` stores the agent's status on their agent profile (`ChatStatus`; a profile is created when the agent has none), light agents included. A JSON client gets `{"agent_status": "away"}`; an Inertia visit goes back. Anything else is a 422.
- Staff pages share `escalated.agent_status` (`offline` until set) and `escalated.agent_statuses` (`["online", "away", "offline"]`).
- This package's live-chat routing does not read the status yet.

## Knowledge base search

- `GET /support/agent/kb/search?q=<2..200 chars>&limit=<1..20, default 10>` (`escalated.agent.kb.search`, `kb.view` under roles & permissions) answers `{"data": [{id, title, excerpt, url, category: {id, name} | null, updated_at}]}`.
- Published articles only; title or body contains the term, case-insensitive, with `%` and `_` taken literally. Title matches come first, then the most recently updated. `excerpt` is the body as plain text, cut at about 160 characters; `url` is the article's absolute knowledge-base page URL.
- 404 while the knowledge base is off (`Escalated:KnowledgeBase:Enabled`).

## Unread tickets

- A ticket is unread for an agent when the customer was active on it after the agent last opened it, or when the agent marked it unread. Customer activity is a ticket's creation (not an import) and every public reply from the customer side: by the requester, or with no author (a guest, an inbound email, a chat visitor). Agents' replies, internal notes and system replies are not. Existing tickets have no activity, so none turn unread on upgrade.
- Opening a ticket in the agent panel marks it read. `POST /support/agent/tickets/{ticket}/read` and `/unread` (`escalated.agent.tickets.read` / `.unread`) answer `{"is_unread": bool}`; the bulk endpoint takes `mark_read` and `mark_unread` (no value).
- `is_unread` is on every `Agent/TicketIndex` row (one query for the page) and on `Agent/TicketShow`'s ticket (`false`). `inbox.unread` is `{mine, unassigned, all}`, counted with `EXISTS` through the list's own filters and the ticket visibility filter, and cached with the rest of `inbox`.

## Customer presence

- The customer portal (signed-in customers who are not staff), the guest ticket page and the widget's ticket and chat endpoints record when the customer was last seen, at most once every 30 seconds. The subject is the requester, else the ticket's contact, else the ticket itself (a guest chat).
- Agent pages only: each `Agent/TicketIndex` row and `Agent/TicketShow`'s ticket carry `requester_online` (seen within `Escalated:Ui:PresenceOnlineWindowSeconds`, default 120) and `requester_last_seen_at`. Customer, guest and widget responses never include them.

## Notifications

| Type | Who | When |
|---|---|---|
| `ticket_assigned` | the assignee | someone else assigns them a ticket (assigning yourself sends nothing, email included) |
| `ticket_reply` | the assignee and followers | a public reply from the customer side |
| `mention` | the mentioned agent | an @mention in an internal note |
| `sla_warning` | the assignee | an SLA warning |
| `sla_breached` | the assignee | an SLA breach |
| `chat_transferred` | the receiving agent | a chat is transferred to them |

Nobody is notified of their own action.

- `GET /support/agent/notifications?page=&per_page=<1..50, default 20>&unread=1` (`escalated.agent.notifications.index`) answers `{data: [{id, type, title, body, ticket: {id, reference, subject} | null, url, created_at, read_at}], meta: {current_page, last_page, per_page, total}}`, newest first. A notification about a ticket the agent can no longer view is redacted: title "A ticket you can no longer view", `body`, `ticket` and `url` null.
- `POST /support/agent/notifications/{id}/read` answers `{"unread_count": n}` (404 for another agent's notification); `POST /support/agent/notifications/read-all` answers `{"unread_count": 0}`.
- Staff pages share `escalated.notifications.unread_count`.
- With real time on, each new notification is sent as `notification.created` (the feed item) to the agent's SignalR group `escalated.agents.{userId}`, which the hub's `JoinNotifications()` adds the signed-in agent to.
- `Escalated:Ui:AgentNotifications = false` switches the feed off: nothing is stored, the routes answer 404 and the shared count is left out.
