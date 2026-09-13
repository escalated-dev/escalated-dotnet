# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
