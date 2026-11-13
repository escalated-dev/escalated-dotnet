# Features

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
- **Roles and permissions** -- Fine-grained access control, enforced behind the install-level `ESCALATED_ROLES_PERMISSIONS` switch ([access control](access-control.md))
- **Audit logging** -- All mutations tracked with old/new values
- **Import system** -- Multi-step wizard with pluggable adapters
- **Side conversations** -- Internal team threads on tickets
- **Ticket merging and linking** -- Merge duplicate tickets and relate issues
- **Ticket subjects** -- Attach host-app entities (Project, Customer, asset) a ticket is about
- **Ticket splitting** -- Split a reply into a new ticket
- **Ticket snooze** -- Snooze until a future date with auto-wake background service
- **Email threading** -- In-Reply-To/References/Message-ID headers for proper threading
- **Inbound email** -- Single webhook endpoint with Postmark + Mailgun + AWS SES parsers, signed Reply-To verification, and replies accepted only from the ticket's requester
- **Saved views** -- Personal and shared filter presets
- **Embeddable widget API** -- Public endpoints for KB search, guest tickets, status lookup
- **Real-time updates** -- SignalR hubs for live ticket updates (opt-in)
- **Capacity management** -- Per-agent workload limits by channel
- **Skill-based routing** -- Match agents to tickets by skill tags
- **CSAT ratings** -- Satisfaction surveys on resolved tickets
- **2FA** -- TOTP enrolment for staff, a challenge after sign-in and recovery codes, behind the install-level `ESCALATED_TWO_FACTOR` switch ([access control](access-control.md))
- **Single sign-on** -- SAML 2, OAuth 2 / OIDC and JWT sign-in behind the install-level `ESCALATED_SSO` switch ([access control](access-control.md))
- **Guest access** -- Anonymous ticket creation with magic token lookup
- **Inertia.js + Vue 3 UI** -- Shared frontend via [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)
