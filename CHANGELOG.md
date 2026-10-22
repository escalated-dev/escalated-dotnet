# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- The admin Import wizard, as escalated-laravel's Admin\ImportController, on its paths and props (UI on): `Escalated/Admin/Import/Index` (`GET support/admin/import`, page requests; the JSON API keeps the rest), `Connect` (`GET support/admin/import/{platform}/connect`, with `POST .../test-connection` answering `{success[, error]}`, 422 when the source throws), `Mapping` (`GET .../mapping`), `Review` (`POST .../review`, which writes nothing), `start` (`POST .../start`) and `Progress` (`GET support/admin/import/progress/{job}`, polled through `GET .../status/{job}`, with `POST support/admin/import/{pause,resume,cancel}/{job}`). Admin only.
  - `IImportAdapter` gains `DisplayName`, `CredentialFields`, `DefaultFieldMappings` and `AvailableSourceFieldsAsync`, each with a default, so existing adapters still compile. Escalated ships no adapter: register yours (a Zendesk or Freshdesk one) as `IImportAdapter`.
  - `ImportService` now runs as Laravel's does: under `ImportContext` (an `AsyncLocal`, so only the run is in import mode), each entity type resumed from its saved cursor and counters, progress and an error log saved per batch, and a stop between batches when the job is paused or cancelled; a completed job drops its credentials. It imports contacts (host users found through `IUserDirectory`, others as Escalated contacts on guest tickets), agents, tags, departments, tickets (description, requester, assignee, department, tags and source timestamps) and replies (author and timestamps). Unknown statuses and priorities fall back to open and medium.
  - Credentials are sealed with ASP.NET Data Protection: between test-connection and start in an HttpOnly cookie scoped to the import paths, then on the job. Started and resumed imports run in the background (`IImportDispatcher`; register your own to use a queue, and call `ImportService.RunAsync(jobId)` from it).
  - Import mode holds back SLA policy attachment, breach and warning checks, escalation rules and automations.
  - Custom fields: a `custom_fields` record (`name`, `type`, `options`) is found by slug or created, as Laravel's `persistCustomField`; the type must be one the custom field admin accepts and a select needs options. A ticket record's `custom_fields` object (`{key: value}`) becomes the ticket's values: the wizard's ticket field mapping decides each key (`"custom_fields.<slug>"` lands on that field, `""` skips it), else the key is a custom field source id from the same import, else a slug. Each value is checked as a ticket update checks it; one that fails is left out and logged under `custom_field_values` with the ticket's source id, and the ticket is kept. Fields a record does not carry are not required.
  - Attachments: an `attachments` record (`parent_type` `ticket` or `reply`, default `reply`; `parent_source_id`, `filename`, `mime_type`, `content` as bytes or base64) is held to the page-upload rules (`Attachments.MaxSizeKb`, the blocked file types, the base name only) and written through `IAttachmentStorage`. Unlike Laravel, a source `path` is never trusted: a record without `content` fails.
  - Satisfaction ratings: a `satisfaction_ratings` record (`ticket_source_id`, `rating` or `score`, `comment`, `created_at`) is written on its imported ticket as a 1-5 integer, one per ticket, comment up to 2,000 characters, with no rater and none of a live rating's rules or effects.
- **Inertia UI** behind `Escalated:Ui:Enabled` (default `false`; escalated-laravel defaults it to `true`). See [docs/inertia-ui.md](docs/inertia-ui.md). The Inertia protocol: an HTML first visit through a host-replaceable root view (`IEscalatedUiRootView`; the default writes `Ui:HeadHtml`, the page object for Inertia 1/2 and 3, and a `window.route()` helper with escalated-laravel's route names), JSON page objects for `X-Inertia` visits, the asset version check (`Ui:Version`, `409` with `X-Inertia-Location`), partial reloads, `303` after form posts, and flash messages and validation errors carried in a data-protected cookie. Shared props as escalated-laravel shares them: `auth.user`, `flash`, `errors` and `escalated` (`prefix`, `is_agent`, `is_admin`, `permissions`, `features`, the knowledge base and guest ticket switches, `agent_type`); add your own with `IEscalatedUiSharedProps`. Page props are snake_case, as the pages read them; the JSON API keeps its casing.
- Agent pages: `Escalated/Agent/Dashboard` (`GET support/agent`), `TicketIndex` (`GET support/agent/tickets`) and `TicketShow` (`GET support/agent/tickets/{reference}`), with the form posts `reply`, `note` (with file attachments), `assign`, `status`, `priority`, `tags`, `department`, `macro`, `follow`, custom actions, and bulk actions.
- Customer pages: `Escalated/Customer/Index` (`GET support`), `Create` (`GET support/create`, `POST support`) and `Show` (`GET support/{reference}`), with `reply`, `close`, `reopen` and `rate` posts. Customers see only tickets they opened, only public replies, and staff by display name only (escalated-laravel `CustomerTicketPayload`).
- Admin pages: `Escalated/Admin/Settings` (`GET`/`POST support/admin/settings`; `supported_settings` lists only the settings this backend reads from the settings table: `guest_tickets_enabled`, `show_powered_by`, `knowledge_base_public`, `widget_enabled`, `widget_color`, `widget_position`, `widget_greeting`, `widget_allowed_origins`), `Departments/Index` and `Form` (create, edit, update, delete; a deleted department's tickets keep going with none), `Tags/Index` (create, update, delete), and `Workflows/Index` and `Form`, served by the workflow admin routes themselves: a page request gets the page and a page form post a 303 with a flash, or back to the form with its errors.
- Admin ticket pages: `Escalated/Admin/Tickets/Index` (`GET support/admin/tickets`, with `agents` and `savedViews`) and `Admin/Tickets/Show` (`GET support/admin/tickets/{reference}`, with `agents` and `is_snoozed`), with the agent pages' form posts and bulk actions under `support/admin/tickets`, plus `snooze` and `unsnooze`. Both ticket areas also answer `replies/{reply}/pin` (pin or unpin an internal note) and `presence` (the other staff viewing the ticket), which the shared ticket page calls.
- Admin configuration pages: `Escalated/Admin/SlaPolicies/Index` and `Form`, `EscalationRules/Index` and `Form`, `CannedResponses/Index`, `Macros/Index`, `Statuses/Index` and `Form`, and `Webhooks/Index`, `Form` and `DeliveryLog`, with escalated-laravel's routes and props, create/update/delete form posts for each, and webhook delivery retry (`POST support/admin/webhooks/deliveries/{id}/retry`). Escalation rules now run in their `Order`.
- People pages: `Escalated/Admin/Users/Index` (served by the users admin routes for page requests, with the role toggle answering a 303 and a flash), `Roles/Index` and `Form` (create, update, delete; system roles cannot be deleted), and `ApiTokens/Index` (create, flashing the plain token once as `plain_text_token`, and revoke). `api_enabled` reports whether the host's pipeline includes `ApiTokenAuthMiddleware`, which now takes an optional `ApiTokenAuthState`.
- Knowledge base pages: `Escalated/Admin/KnowledgeBase/Articles/Index` and `Form` and `Categories/Index` (`support/admin/kb-articles`, `support/admin/kb-categories`, with create, update and delete), and the public `Customer/KnowledgeBase/Index` and `Article` (`GET support/kb`, `GET support/kb/{slug}`, `POST support/kb/{slug}/feedback`), which follow `Escalated:KnowledgeBase` and the `knowledge_base_public` setting.
- Guest pages: `Escalated/Guest/Create` (`GET support/guest/create`, `POST support/guest` with a verified `ticket` code, redirecting to the private link) and `Guest/Show` (`GET support/guest/{token}`, with `reply` and `rate` posts) on the verified guest flow. The ticket's files are served to the grant holder at `support/guest/{token}/attachments/{id}`; internal notes stay hidden.
- `Escalated/Admin/Workflows/Logs`, served by the workflow logs routes (`GET support/admin/workflows/logs`, `.../{id}/logs`) for page requests.
- Report pages: `Escalated/Admin/Reports` and `Reports/Dashboard`, `AgentMetrics`, `SlaReport`, `CsatReport`, `ResponseTimes`, `ResolutionTimes`, `AgentRanking`, `SlaTrends`, `Cohorts` and `Comparison` under `support/admin/reports`, with escalated-laravel's routes and figures. `GET support/admin/reports`, `/sla` and `/csat` still answer API clients with JSON.
- Custom field and custom object pages, with escalated-laravel's routes and props: `Escalated/Admin/CustomFields/Index` and `Form` (`support/admin/custom-fields`: create, edit, update, delete, and `POST custom-fields/reorder` with `positions: [{id, position}]`) and `Escalated/Admin/CustomObjects/Index`, `Form` and `Records` (`support/admin/custom-objects`: create, edit, update, delete with its records, and `GET`/`POST custom-objects/{id}/records`, `PUT`/`DELETE custom-objects/{id}/records/{record}`). Fields take a `context` (`ticket`, `user`, `organization`), `placeholder` and `description`, and the types `text`, `textarea`, `select`, `multi_select`, `checkbox`, `date` and `number`; a select needs at least one option, and slugs come from the name (`order_number`, then `order_number_2`). Record `data` is checked against the object's `fields_schema` (required keys, numbers, dates, select options, no unknown keys; errors keyed `data.<name>`).
- Ticket custom field values through a `custom_fields` object keyed by field slug, on the customer create page (`POST support`), the customer JSON API (`POST support/tickets`) and a new ticket update route on the agent and admin areas (`PUT support/agent/tickets/{reference}`, `PUT support/admin/tickets/{reference}`, escalated-laravel's `tickets.update`; it saves only `custom_fields`). Only active `ticket` fields count. On create every required field must be filled; on update only the keys sent are checked, an empty optional value clears it, and omitting `custom_fields` changes nothing. Errors are keyed `custom_fields.<slug>` (back to the form, or 422 from the JSON API), and a failed create writes no ticket. `Customer/Create` gets the field definitions as `custom_fields`; `Customer/Show`, `Agent/TicketShow`, `Admin/Tickets/Show` and the JSON ticket detail (`GET support/tickets/{id}`, `support/agent/tickets/{id}`, `support/admin/tickets/{id}`) get `custom_fields` with each field's decoded `value` (`multi_select` an array, `checkbox` a boolean, `number` a number). Guest and widget ticket creation do not take custom fields yet.
- The admin Settings sub-pages, with escalated-laravel's routes, props, validation and flash messages (UI on, admin only): `Escalated/Admin/Settings/CsatSettings` (`support/admin/settings/csat`), `DataRetention` (`support/admin/settings/data-retention`, with `purgePreview`), `EmailSettings` (`support/admin/settings/email`: `email_addresses` normalised to `email`, `display_name`, `department_id`, `dkim_status`, and `default_reply_address`; stored only, as in Laravel), `PublicTickets` (`support/admin/settings/public-tickets`, saving the guest policy keys the widget reads over `POST` or `PUT`) and a read-only `DatabaseConnection` (`support/admin/settings/database`, `POST .../database/test`): the connection is the host's `EscalatedDbContext` registration, so it is shown pinned and a switch is refused. All values live in the settings table; the route names are in `UiRoutes`.
- Data retention, as Laravel's `escalated:purge-expired`: `DataRetentionService.PurgeExpiredAsync(dryRun, force)` soft-deletes closed tickets past `retention_closed_tickets` (`force` deletes them with their replies, attachments, links, followers, ratings and the rest; inbound emails and merged tickets are unlinked), deletes tickets soft-deleted over 30 days ago, attachment rows and audit logs past their periods, and expired guest verification codes. `RetentionPurgeBackgroundService` runs it daily.
- The saved-view API (`support/admin/saved-views`) also accepts `filters` as a JSON object and `is_shared`, which is what the shared views dialogs send; a string `filters` and `isShared` still work.
- `TicketListFilters.TagIds` and `TicketListFilters.FollowedBy`.
- Pages not served yet (each still a JSON endpoint or missing; all names under `Escalated/`): Admin/AuditLog/Index, Admin/Automations/Form, Admin/Automations/Index, Admin/BusinessHours/Form, Admin/BusinessHours/Index, Admin/Capacity/Index, Admin/Plugins/Index, Admin/Reports/AgentDetail, Admin/Settings/Sandbox, Admin/Settings/SsoSettings, Admin/Settings/TwoFactor, Admin/Skills/Form, Admin/Skills/Index, Admin/Workflows/Builder, Auth/TwoFactorChallenge, Error, Plugin/Page.

### Security
- **Stored secrets are no longer sent to admin pages or API clients** (escalated-laravel #258). The webhook edit page received the webhook's signing secret, and the webhook JSON API (`GET support/admin/webhooks`, and the `POST` response) returned it with every webhook. `Webhook.Secret` is now `[JsonIgnore]` and pages and the API see `has_secret` instead. Admin and agent ticket JSON returned a guest ticket's legacy `GuestToken`, which opens the ticket; it is now `[JsonIgnore]`. `ApiToken.TokenHash` is `[JsonIgnore]` too, though no response sent it. The import pages are built from Laravel's explicit field list (`id, platform, status, progress, field_mappings, error_log` (the last 50), `started_at, completed_at, created_at`); they never carried the credentials, which stay sealed and are read server-side by the run.

### Changed
- The newsletter screens render through the Inertia renderer. With the UI off they still answer `{component, props}` JSON, but nested props are now snake_case (`from_name`, `summary_total`, `target_list`), which is what their pages read; they rendered with blank fields before. With the UI on, an Inertia form post answers `303` instead of `{redirect}` JSON.

### Upgrading
- Saving a webhook from the admin form with the secret field left blank (or missing) now keeps the stored signing secret, because the form no longer receives it; it used to clear it. Send `clear_secret: true` to remove it. Any other value replaces it, and create is unchanged.
- The webhook JSON API (`GET`/`POST support/admin/webhooks`) and the webhook pages no longer return `secret`; read `has_secret` instead. `POST support/admin/webhooks` still accepts `url`, `secret`, `description`, `events` and `active`.
- API responses that serialize `Ticket` no longer include `guestToken`; the widget still hands a guest its legacy token explicitly when `GuestAccess:LegacyTokens` is on.
- `AddEscalated()` registers `RetentionPurgeBackgroundService`, which runs the data retention purge once a day. Every period defaults to `never`, so until an admin sets one it only removes expired guest verification codes. Note that it permanently deletes tickets soft-deleted (`DeletedAt`) more than 30 days ago, as Laravel's command does. Remove the hosted service from the container to opt out.
- Apply the `ImportJobUser` migration (all three providers): a nullable `UserId` on `escalated_import_jobs`.
- The JSON import API no longer returns a job's `Credentials`.
- Import adapters that offer attachments must send each file's bytes as `content`; a stored path from the source is refused.
- Nothing changes until you set `Escalated:Ui:Enabled` to `true`. Then the page routes that share a path with the JSON API (`GET support/agent/tickets`, `GET support/admin/tickets`, `POST support/agent/tickets/bulk`, `GET`/`POST support/admin/settings`, the `support/admin/departments`, `support/admin/tags` and `support/admin/workflows` routes) answer Inertia visits and browser navigations (`Accept: text/html`) with the page; API clients that send `Accept: application/json` (or no `Accept`) still get JSON. `AddEscalated()` now calls `AddDataProtection()`, which the flash cookie uses.
- **New migration `AdminPageColumns`** (all three providers): `escalated_escalation_rules` gains `TriggerType` (default `time_based`) and `Order` (default 0), and `escalated_ticket_statuses` gains `Category` (default `open`) and `Description`. Run `MigrateEscalatedDatabaseAsync()` or `dotnet ef database update`.
- The SLA policy, escalation rule, canned response, macro, webhook, users, roles and API token list routes, and their `POST`, `PUT`, `PATCH` and `DELETE` routes the JSON API also answers, now answer Inertia visits and browser navigations with the page; API clients are unchanged.
- **New migration `CustomFieldsAndObjects`** (all three providers): `escalated_custom_fields` gains `Context` (default `ticket`), `Placeholder` and `Description`, field and custom object names may be 255 characters, and `escalated_custom_field_values` gets a unique index on (`CustomFieldId`, `EntityType`, `EntityId`); the migration first deletes all but the newest of any duplicate values. Existing `TicketId` values are kept, and new ticket values set it too.
- With the UI on, `GET` and `POST support/admin/custom-fields` answer Inertia visits and browser navigations with the page; API clients are unchanged, except that the JSON create now makes a unique slug instead of failing on a duplicate.
- A client that read the newsletter screens' nested camelCase props (`fromName`) should read snake_case (`from_name`).

## [0.2.3] - 2026-10-21

### Upgrading
- **Guest tickets and widget chats now need a verified email.** `POST support/widget/tickets` and `POST support/widget/chat/start` need `verification_id` and `verification_code` from `POST verification`, answering 422 without them. Ticket creation answers 201 with `guest_access_token` and `expires_at` instead of `id` and `guestToken`. The bundled widget already sends the code step. Codes are emailed through `IEscalatedMailTransport`, so configure `Escalated:Email:Smtp` (or your own transport); without one, no code can arrive. Set `Escalated:GuestAccess:Secret` (or `Email:InboundSecret`) to the same value on every instance.
- **Permanent guest tokens stop authorizing** the widget's ticket routes and the hub. Set `Escalated:GuestAccess:LegacyTokens` to `true` to accept them during a transition; creation then also returns `guestToken`. Guests with older tickets can verify their email and look the ticket up by reference for a new link.
- **New migration `VerifiedGuestAccess`** (all three providers) adds `escalated_guest_verifications` and the ticket columns `GuestAccessHash` and `GuestAccessExpiresAt`.
- Nothing is emailed until `Escalated:Email:Smtp:Host` is set, so existing installs send nothing new; the first notification logs that emails are off. Agents and followers are emailed through your `IUserDirectory` addresses.
- A host that registered its own `IEscalatedNotificationSender` now has it called for every notifying event, not only mentions. Register `NullNotificationSender` to keep notifications off.
- **New migration `WorkflowDelayedActions`** (all three providers) adds `escalated_delayed_actions`. Run `MigrateEscalatedDatabaseAsync()` or `dotnet ef database update`.
- Event names need no migration. Stored webhook subscriptions and Workflows that use a legacy name (`ticket.tagged`, `ticket.untagged`, `tag.added`, `tag.removed`, `internal_note.added`, `internal_note.created`, `ticket.replied`, `chat.session_started`, `chat.session_ended`) are read as the canonical name and keep firing. The payload's `event` field and the `X-Escalated-Event` header carry the canonical name, so a receiver that switches on the old name should accept the new one. Creating a webhook stores canonical names.

### Added
- **Verified guest access** (escalated-laravel `docs/guest-access.md`; see [docs/guest-access.md](docs/guest-access.md)). `POST verification` and `POST lookup` on `support/widget` and the new `support/guest` prefix. Guests prove their mailbox with an emailed eight-digit code: it is bound to the email and the purpose (`ticket`, `chat`, `lookup`), lasts 10 minutes, allows 5 guesses and is spent once. Code delivery per mailbox is budgeted (3 per client IP and 10 overall per hour, 429 with `Retry-After`). Only HMAC digests of the email and code are stored (`Escalated:GuestAccess:Secret`, falling back to `Email:InboundSecret`).
- Guest tickets get an expiring access grant (`guest_access_token`, `expires_at`; `GuestAccess:TtlMinutes`, default 1440). Only its SHA-256 is stored, and a new grant revokes the previous one. `GET support/widget/tickets/{reference}`, its `reply` and `rate` take it as `Authorization: Bearer`. The SignalR hub accepts it as `guest_token`.
- Widget `/config` answers `guest_verification_required: true` and `status_lookup_enabled: true`, so the widget's Status tab is back.
- The `widget_enabled` setting (default on) switches every widget route off (403, `/config` answers `{enabled: false}`). The `guest_tickets_enabled` setting (default on) refuses guest tickets and lookups and is reported in `/config`.
- `POST support/widget/chat/start` is throttled per IP. `verification` and `lookup` have their own per-IP limit (`GuestRateLimit:VerificationsPerMinute`, default 10).
- **Workflow admin** (`support/admin/workflows`, admin policy): list, options, show, create, update, delete, toggle, reorder and logs, taking the shared builder's body (escalated-developer-context `domain-model/workflow-admin-contract.md`). Validation failures answer 422 with `{message, errors}`. See [docs/workflows.md](docs/workflows.md).
- Workflow actions `set_type`, `send_webhook` (SSRF-guarded, no redirects) and `delay`. A delay holds the remaining actions in the new `escalated_delayed_actions` table; `WorkflowDelayedActionBackgroundService` runs them when due, after checking the conditions again, and cancels them if the ticket was resolved or closed.
- Workflow conditions on `tags` (by name, as a list) and `hours_since_created` / `hours_since_updated`. Workflows stored as a flat condition list or as `{match, rules}` are evaluated.
- **Notification emails out of the box.** The default `IEscalatedNotificationSender` is now `EmailNotificationSender` (was the no-op `NullNotificationSender`), and `NotificationEventDispatcher` calls the registered sender on ticket created, public reply, assignment, status change, escalation, resolution and SLA breach. It never called it before, so a host sender only ever heard about mentions. Recipients follow the Laravel reference's listeners, and nobody is emailed about their own action. Emails are threaded on the ticket's anchor `Message-ID`, carry the signed `Reply-To` when `Email.InboundSecret` is set, and go through `IEscalatedMailTransport`; the default `SmtpMailTransport` uses the new `Email.Smtp` settings plus `Email.FromAddress` / `Email.FromName`. See [docs/notification-emails.md](docs/notification-emails.md).
- `IEscalatedNotificationSender` gains overloads of `SendAssignmentNotificationAsync` and `SendStatusChangeNotificationAsync` that take the causer, with default implementations that call the existing methods.
- Live chat events reach webhooks and Workflows: `chat.started`, `chat.assigned` (an agent accepts, or routing assigns one when the chat starts), `chat.message` (new `ChatMessageEvent`) and `chat.ended`. Payloads carry `chat.session_id`, `ticket_id`, `status`, `agent_id` and `department_id`, never the visitor's token or email.
- Workflows can be triggered by `ticket.resolved`, `ticket.closed`, `ticket.unassigned`, `ticket.escalated`, `ticket.tag_removed`, `note.created` and the chat events. These were raised but never reached Workflows.
- `EventNames`: the canonical names, the legacy names, and `EventNames.Canonicalize`.

### Changed
- `WorkflowEngine.ActionTypes` lists what the executor runs. `send_notification` is no longer offered (nothing sent it); `insert_canned_reply` is now listed.
- `add_tag` / `remove_tag` find the tag by name, which is what the builder sends (slug and id still work). `add_note` fills in `{{field}}` placeholders.
- Tag events are dispatched after the tag is saved, so a Workflow's `tags` condition sees it.
- **Webhook and trigger names follow the shared event vocabulary** (escalated-developer-context ADR 2026-10-21). Internal notes are sent as `note.created` (was `internal_note.added`), tags as `ticket.tag_added` / `ticket.tag_removed` (were `ticket.tagged` / `ticket.untagged`), and the tag trigger is `ticket.tag_added` (was `ticket.tagged`).
- `WorkflowEngine.TriggerEvents` no longer offers `reply.agent_reply`, which nothing ever raised; a Workflow saved with it never ran.

## [0.2.2] - 2026-10-21

### Changed
- The bundled widget is now `@escalated-dev/escalated` 0.12.3. It keeps its script settings when `embed.js` is placed in the page `<head>`, and Enter no longer picks a stale mention suggestion.

### Upgrading
- **Migrations now create the whole schema.** The package ships one baseline migration per provider (SQLite, PostgreSQL, SQL Server) and picks the set for the provider in use. Call `await app.Services.MigrateEscalatedDatabaseAsync()` at startup, or run `dotnet ef database update --context EscalatedDbContext`. The old README step `dotnet ef migrations add` is no longer needed.
- A database created with `EnsureCreated()` (as the demo host did) is adopted by `MigrateEscalatedDatabaseAsync()`: the baseline is recorded as applied and only later migrations run. Tables added to the model after that database was created are not back-filled; compare against the baseline if you skipped versions.
- Hosts that keep Escalated's migrations in their own assembly (`MigrationsAssembly("MyApp")`) are unaffected.
- Inbound email attachments are now stored. Configure `Attachments.LocalRoot` or register your own `IAttachmentStorage`.

### Fixed
- An empty database got no Escalated tables: the bundled migrations only added deltas on top of tables nothing created. `Migrate()` on an empty SQLite database failed in the first migration.
- Inbound email attachments were dropped. Inline attachments are now stored through `IAttachmentStorage` (default: local files under `Attachments.LocalRoot`), provider-hosted ones are downloaded, files over `MaxSizeKb` or with executable extensions are skipped, and at most `MaxPerReply` are kept. Ticket and reply screens now load them (`EscalatedDbContext.LoadAttachmentsAsync`).
- Webhook and attachment-download URLs could reach internal hosts: only literal private IPs were refused. The HTTP handler now resolves the host itself, refuses the connection if any resolved address is loopback, private, link-local or cloud-metadata, connects to the address it checked, never follows redirects and ignores the system proxy.
- The SignalR hub accepted joins without Escalated's services registered, and in the default visibility mode. Every join is now authorized: staff rooms need a staff user, a ticket room needs access to the ticket or its guest token, and a chat room needs access or that chat's visitor token.
- Creating a role whose name slugs onto an existing role ("Escalated Admin", or "A/B" next to "A B") returned 500. Slugs are now suffixed until free, and a duplicate name answers 422.

## [0.2.1] - 2026-10-19

### Changed
- The bundled widget is now `@escalated-dev/escalated` 0.12.2. It hides its Check Status tab when `/config` returns `status_lookup_enabled: false`.

### Security
- Widget chat routes are keyed by an unguessable visitor token instead of the numeric chat session id. Before, anyone could post into or end another visitor's chat by counting ids. `POST /support/widget/chat/start` now returns a random 256-bit token as `id` (only its SHA-256 is stored), and `/chat/{id}/messages` and `/chat/{id}/end` answer 404 for numeric ids. Apply the `ChatSessionVisitorToken` migration; chats started before it cannot be reached from the widget.

### Fixed
- Preserve trailing spaces in account visibility, filters, and routing under SQL Server padded string comparisons.

### Added

- Widget API matches the shared `@escalated-dev/escalated` widget: `GET /support/widget/articles?q=` and `/articles/{slug}`, `GET /chat/{id}/messages`, `POST /chat/{id}/typing` and `/chat/{id}/rate`, `department_id` on ticket create, chat start and availability (active departments only), and `status_lookup_enabled: false` in `/config`. `kb/search` and `departmentId` keep working; knowledge base search now ignores case. See [embedding the widget](docs/embedding-the-widget.md#what-the-shared-widget-calls).
- Embed the widget on other sites. List them in `Escalated:Widget:AllowedOrigins`, `ESCALATED_WIDGET_ALLOWED_ORIGINS` or the `widget_allowed_origins` admin setting (validated on save); listed origins get CORS (preflight included, and on 4xx/429 answers) on every `/support/widget/*` route and always act as guests. Nothing changes until an origin is listed. Adds `GET /support/widget/config` (with `accepts_metadata`), sanitised `metadata` on widget tickets and chats, and `GET /support/widget/embed.js` serving the `@escalated-dev/escalated` 0.12.1 bundle. See [embedding the widget](docs/embedding-the-widget.md).
- Opt-in department/account ticket visibility across MVC, bearer APIs, reports, bulk actions, widget, related records and live delivery, with admin bypass and host resolver hooks.
- Nullable indexed ticket account identity, account/type routing conditions, and optional department/type SLA selection. Department changes preserve the existing SLA clock.
- Additive schema migration and SQLite/PostgreSQL access-path and migration regressions. See [configuration and upgrade notes](docs/ticket-access-and-routing.md).

## [0.2.0] - 2026-10-08

### Upgrading

- **Register an `IUserDirectory` that returns user emails if registered users
  reply by email.** An inbound message becomes a reply only when `From` is the
  ticket's requester: the guest email, the contact's email, or the requester
  user's email from your `IUserDirectory.FindAsync`. Without that, replies to
  tickets raised by registered users open a new ticket.
- **Agents reply in the app.** Mail from anyone other than the requester, an
  agent's address included, opens a new ticket for the sender and leaves the
  matched ticket alone.
- **Set `Escalated:Email:InboundSecret`.** With it set, only the signed
  `Reply-To` address links mail to a ticket; `In-Reply-To` / `References` and
  subject references no longer do.
- **Accepted email replies reopen resolved and closed tickets.**
- **Guest endpoints are rate-limited per client IP** (5 tickets and 10 replies
  a minute). Behind a proxy, configure trusted proxies and call
  `app.UseForwardedHeaders()`, or every guest shares one limit. A multi-instance
  host registers a shared `IGuestRateLimiter`. Configure under
  `Escalated:GuestRateLimit` (`Enabled`, `TicketsPerMinute`, `RepliesPerMinute`).

### Added
- **Per-IP rate limit on guest ticket creation and replies.**
  `POST /support/widget/tickets` allows 5 tickets and
  `POST /support/widget/tickets/{token}/reply` 10 replies per client IP per
  minute, counted separately; beyond that the response is `429` with
  `Retry-After`. The reply limit runs before the guest token is looked up, so
  wrong-token requests count. The counter store is `IGuestRateLimiter`,
  registered with `TryAddSingleton` so a host can replace it (#169).

### Security
- **Inbound email accepted replies from any sender.** A message that matched a
  ticket by subject reference, `In-Reply-To` / `References`, or reply address was
  posted on that ticket whoever sent it. With `InboundSecret` set, only the signed
  `Reply-To` address now links mail to a ticket, and a matched message becomes a
  reply only when `From` is the ticket's requester (guest, contact, or requester
  user email, case-insensitive), posted as that requester. Other senders get a
  new ticket, and only an accepted reply reopens a resolved or closed ticket.

### Fixed
- The README's configuration example put the inbound secret under
  `Escalated:Mail`, which nothing reads. It is `Escalated:Email:InboundSecret`
  (with `Escalated:Email:Domain`).

## [0.1.3] - 2026-09-13

### Security
- **Any admin could change or delete another admin's saved view.**
  `AdminSavedViewController` listed only the signed-in admin's own and shared
  views, but `PUT` and `DELETE /support/admin/saved-views/{id}` acted on whatever
  id they were given. Both now answer 403 unless the view has no owner or belongs
  to the caller, as the Laravel reference does. A shared view can be used by
  everyone but changed only by its owner.

### Fixed
- **Replies, notes and ticket details answered 500 after saving.** Controllers
  return EF entities, and both ends of most relationships were serialized, so
  every loaded graph was a cycle: a reply's `Ticket` holds the reply in its
  `Replies`, a ticket's `Department` holds the ticket in its `Tickets`.
  System.Text.Json threw `JsonException: A possible object cycle was detected`,
  so the customer, agent and admin reply and note endpoints, the three ticket
  show endpoints, and ticket actions that record an activity (changing priority,
  for one) failed once the change was already saved. Roles with permissions,
  business hours with holidays, skills with agents, side conversations with
  replies, knowledge base articles and categories, and newsletter list members
  load graphs of the same shape.
  - The back-reference of each two-way relationship is no longer serialized.
    The collections and references the shared frontend reads (`replies`,
    `activities`, `attachments`, `department`, `tags`, `deliveries`, `holidays`,
    `category`, `children`, `members`) are unchanged.

- **Failed webhook deliveries were never retried.** A failed delivery scheduled
  its retry fire-and-forget on the dispatcher that sent it, whose
  `EscalatedDbContext` belonged to the request's scope (or the per-event scope
  `WebhookEventDispatcher` opens). When the two-minute backoff ended that context
  was disposed, the retry threw `ObjectDisposedException`, and nothing observed
  the task, so no second or third attempt was sent, recorded or logged.
  - Each retry now opens its own scope, reads the webhook again and skips one
    deactivated or deleted during the backoff, and logs a failure instead of
    losing it. Retries still don't hold up the request, and a retry still
    waiting when the host stops is not sent.
- **An Escalated admin was refused every newsletter action.**
  `NewsletterPermissionService` gave every newsletter permission to a role with
  the slug `admin`, but the admin role the admin users page grants, and the
  default admin policy requires, is `escalated-admin`. Nothing in the package
  creates an `admin` role, so behind the default policies nobody could open
  `/admin/newsletters` without a `newsletters.manage` permission attached by
  hand. The `escalated-admin` role now holds `newsletters.manage` and
  `newsletters.send`, and `NewsletterPermissionSeeder` attaches both to it.

### Changed
- These navigation properties are `[JsonIgnore]`: `Reply.Ticket`,
  `TicketActivity.Ticket`, `SideConversation.Ticket`,
  `SideConversationReply.SideConversation`, `SatisfactionRating.Ticket`,
  `ChatSession.Ticket`, `TicketLink.ParentTicket` and `ChildTicket`,
  `Department.Tickets`, `SlaPolicy.Tickets`, `Tag.Tickets`, `Permission.Roles`,
  `RoleUser.Role`, `AgentSkill.Skill`, `SkillRoutingTag.Skill`,
  `SkillRoutingDepartment.Skill`, `Holiday.Schedule`, `ArticleCategory.Parent`
  and `Articles`, `CustomField.Values`, `CustomObjectRecord.Object`,
  `ImportSourceMap.ImportJob`, `NewsletterListMember.List` and
  `WebhookDelivery.Webhook`. A response could only ever have carried them as
  `null` or empty; loaded, they threw.
- `WebhookDispatcher` takes an `IServiceScopeFactory` and a `TimeProvider`, and
  optionally an `IHostApplicationLifetime`. `AddEscalated` registers
  `TimeProvider.System` unless the host has registered one.
- A role with the slug `admin` no longer passes the newsletter permission check
  by its slug. Like any other role, it holds the permissions attached to it.

## [0.1.2] - 2026-09-13

### Security
- **No endpoint was authorized, and the acting user came from the request.**
  Every admin, agent and customer endpoint answered anonymous requests. Endpoints
  that needed the caller took their id from a `requesterId`, `agentId`, `userId`
  or `currentUserId` query parameter, or a `RequesterId`, `AuthorId`, `CauserId`,
  `CreatedBy` or `MergedByUserId` body field, so changing one read another
  customer's ticket, replied or acted as someone else, or read another agent's
  mentions. Attachments downloaded for anyone who guessed an id.
  - Controllers now require the `escalated-admin`, `escalated-agent` or
    `escalated-customer` authorization policy, or are explicitly public (widget,
    guest, inbound email, newsletter tracking and ESP webhooks, API auth).
  - By default the staff policies check the `escalated-admin` and
    `escalated-agent` roles the admin users page manages; hosts can replace any
    policy by name. See "Authorization" in the README.
  - The acting user is the signed-in user, resolved from `NameIdentifier`, `sub`
    or `id`, or through the new `EscalatedOptions.UserIdResolver`.
  - A customer can download only attachments on their own tickets.
- An admin missing a newsletter permission now gets 403 instead of a 500.

### Changed
- **Hosts must configure authentication** (`AddAuthentication`,
  `UseAuthentication`, `UseAuthorization`) and grant roles or replace the
  policies. Without that, Escalated's endpoints are closed.
- Request records no longer carry the acting user: `ReplyRequest.AuthorId`,
  `CauserId` on the ticket action requests, `MergeRequest.MergedByUserId`,
  `CustomerReplyRequest.RequesterId`, `CreateTicketRequest.RequesterId`,
  `CreateSavedViewRequest.UserId`, `CustomActionRequest.UserId`, the chat
  requests' `AgentId`, `CreateSideConversationRequest.CreatedBy`,
  `SideConversationReplyRequest.AuthorId` and `CreateArticleRequest.AuthorId`
  are gone, as are the identity query parameters. JSON clients that still send
  them are unaffected; the values are ignored.
- `AttachmentController` takes an `IAuthorizationService`.
- `NullEventDispatcher` no longer disables Workflows when registered; host
  dispatchers only add listeners. `AgentTicketController` takes
  `EscalatedEventDispatcher` instead of `IEscalatedEventDispatcher`.

### Fixed
- **A host that added the package's controllers served no requests at all.**
  The six newsletter controllers are `[ApiController]`s with no routes, and MVC
  refuses to build its endpoint table when any `[ApiController]` action is not
  attribute routed. It checks on the first request and throws for every
  endpoint, so `GET /support/admin/tickets` failed with `InvalidOperationException`
  on the demo host, newsletters enabled or not.
  - The newsletter controllers now carry the reference routes
    (`/admin/newsletters*`, `/escalated/n/*`, `/escalated/webhooks/newsletter/*`),
    and still return 404 while `EnableNewsletters` is off.
  - `MapEscalated` no longer maps conventional newsletter routes, which could
    never reach attribute-routed actions.
- **Any response containing a ticket failed to serialize.** `Ticket.Subjects`
  and `Ticket.SubjectsPayload` both became `subjects` under MVC's camelCase
  naming, which System.Text.Json rejects for the whole type. The raw links are no
  longer serialized; `subjects` carries the resolved payload the README documents.
- **Outbound webhooks never fired.** Nothing called `WebhookDispatcher`, so a
  webhook an admin configured received no deliveries. Domain events are now
  delivered to every active webhook subscribed to the event's name (or `*`), with
  a `WebhookDelivery` recorded per attempt. The README lists the event names.
- **Registering your own `IEscalatedEventDispatcher` switched Workflows off.**
  The Workflow bridge was the one registered dispatcher, added with `TryAdd`, so a
  host that followed the README to listen for events replaced it without any
  error. Escalated's services now dispatch through `EscalatedEventDispatcher`,
  which runs webhooks, then Workflows, then every dispatcher the host registered,
  in whichever order it was registered relative to `AddEscalated`.

## [0.1.1] - 2026-09-13

### Fixed
- **Both `AddEscalated` overloads left services unregistered.** They kept
  separate lists that had drifted apart, and nothing fails at startup when a
  registration is missing -- a controller is only built when a request reaches
  it.
  - Following the README (`AddEscalated(configuration, configureDb)`), inbound
    email returned 500s, `WebhookDispatcher` could not be resolved, and
    `MentionService` was never registered, so @-mentions in internal notes were
    dropped silently.
  - The parameterless `AddEscalated()` could not build nine controllers,
    including the admin and agent ticket controllers and every newsletter
    controller, and registered none of the background services, so automations,
    SLA monitoring and escalations never ran.

  Both now register the same services, and calling both no longer registers
  every background service twice.

### Changed
- EF Core 9.0.20 in every project, together, and the ASP.NET Core test
  packages to their latest patches.

## [0.1.0] - 2026-09-12

### Fixed
- **`CREATE TABLE escalated_agent_skill` failed on PostgreSQL.** The check
  constraint on `Proficiency` spelled the column unquoted, and PostgreSQL folds
  an unquoted identifier to lower case — so it asked for a column named
  `proficiency`, which does not exist, and refused the statement. The package
  could not create its own schema on one of the two providers it ships a driver
  for. SQLite compares identifiers case insensitively and never noticed.

### Changed
- **The test suite runs on a real database.** It ran on the EF Core InMemory
  provider, which is not a database: no SQL, no types, no constraints. SQLite is
  now the default — real SQL, real types, real constraints, and still nothing to
  install — and `ESCALATED_TEST_DATABASE=postgres` runs the same 384 tests
  against a PostgreSQL server, each with a schema of its own.

  An unrecognised value throws rather than falling back, because a CI leg that
  quietly ran SQLite would report green having tested nothing the matrix exists
  for. `DatabaseProviderTests` is the one test that notices — including that the
  provider is relational at all.

  Two test bugs surfaced immediately, both things InMemory cannot enforce: the
  webhook dispatcher tests wrote a delivery row referencing a webhook that was
  never saved, and a mention test inserted two mentions for one agent on one
  reply, which the unique index forbids.

### Added
- **Configurable database connection, documented and guarded.**
  `EscalatedDbContext` has always had its own connection string, so Escalated's
  tables go wherever `ConnectionStrings:Escalated` points — including a database
  the host application otherwise never touches, and a provider it does not use.
  The README's only example pointed it at the host's own database, and nothing
  stopped the separation from quietly rotting.

  The README now covers it, and tests fail the build if an entity maps to a
  host-owned table, declares a foreign key to one, or any raw SQL in the package
  names one. On a separate database such a read does not throw; it returns
  nothing, which reads as users who do not exist.

  Host user data continues to arrive through `IUserDirectory`, which the host
  implements against its own `DbContext`. `Ticket.RequesterId`,
  `Ticket.AssignedTo` and `Reply.AuthorId` are plain unconstrained columns with
  no foreign key, so no query joins the two — no database can join across two
  connections.
- `AddEscalated()` DI extension method for single-call service registration (#15)
- Consume central translation catalog from the `Escalated.Locale` NuGet
  package. `AddEscalated()` now registers a chained `IStringLocalizer`
  that resolves plugin-local overrides under
  `Resources/Overrides/` first and falls back to the central catalog.

### Fixed
- Include `url` in attachment JSON serialization (#9)
- Include computed ticket fields in ticket JSON serialization (#10)
- Include chat, context panel, and activity fields in ticket serialization (#11)
- Include missing workflow and workflow log computed fields in serialization (#12)

### Internal
- Minimal ASP.NET Core host project under `docker/host-app/` for dev/demo (#13)
- Upgrade `/demo` to click-to-login picker with seeded agents (#16)
- Complete README translations across supported locales (#8)

## [1.0.0] — initial release

ASP.NET Core 8 port of `escalated` reaching feature parity with the Laravel reference: tickets, workflow engine, chat, KB, reports, SLA tracking, and Inertia-driven Vue frontend served through the shared `@escalated-dev/escalated` package. Ships as the `Escalated` NuGet package.
