# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
