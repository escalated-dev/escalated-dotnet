# Task: support UUID/string host-app user keys (escalated-dotnet)

The .NET package assumes the host app's user id is `int`. EF Core entity
properties, the `IUserDirectory` host contract, DTOs, and `int.Parse` calls all
hardcode `int`, so a host whose user primary key is a UUID/string (`Guid`/
`string`) cannot use it. Because C#/EF map property types statically (an `int`
column can't hold a UUID, and a property can't be "int or string"), the fix is
to store **host user ids as strings** uniformly. A numeric host id is simply
stored/sent as its string form; nothing else about Escalated's own integer ids
changes.

> This is a deliberate, documented behavior change for host integrations: the
> `IUserDirectory` contract and host-user-id fields become `string`. Escalated's
> OWN ids (ticket id, department id, role id, skill id, agent-profile id, etc.)
> stay `int`/`long`.

## Step 1 — IUserDirectory contract (`src/Escalated/Services/IUserDirectory.cs`)

- `Task<UserDirectoryEntry?> FindAsync(int id, ...)` → `FindAsync(string id, ...)`
- `public record UserDirectoryEntry(int Id, string? Name, string? Email)` → `UserDirectoryEntry(string Id, string? Name, string? Email)`
- Update any other method on this interface that takes/returns a host user id to `string`.
- Update all implementations and call sites accordingly.

## Step 2 — Entity properties (host-user-id only → `string`/`string?`)

Change these from `int`/`int?` to `string`/`string?` (keep nullability). Grep
the `src/Escalated/Models` (or `Entities`) folder to confirm exact names; do NOT
touch Escalated's own int ids.

- `Ticket` — `RequesterId`, `AssignedTo`
- `TicketActivity` — `CauserId`
- `Reply` — `AuthorId`
- `AuditLog` — `UserId`
- `SideConversation` — `CreatedBy`
- `SideConversationReply` — `AuthorId`
- `AgentProfile` — `UserId`
- `AgentCapacity` — `UserId`
- `Macro` — `CreatedBy`
- `Article` — `AuthorId`
- `CannedResponse` — `CreatedBy`
- `Contact` — `UserId`
- `SavedView` — `UserId`
- `Role`/`RoleUser` — `UserId`
- `Skill`/`AgentSkill` — `UserId`
- `ChatSession` — `AgentId`

## Step 3 — EF migrations + model snapshot

For EACH host-user-id column in the EF migrations under
`src/Escalated/Migrations/`, change the column type from integer to a string
type for every provider branch present (the migrations branch on
SQLite / Npgsql(PostgreSQL) / SqlServer):
- SQLite: `TEXT`
- PostgreSQL: `character varying(255)` / `varchar(255)`
- SQL Server: `nvarchar(255)`

Mirror the same property/column type changes in the EF **ModelSnapshot**
(`*ModelSnapshot.cs`) so the snapshot matches the model (otherwise `dotnet ef`
reports model drift). If the toolchain is available, prefer regenerating via
`dotnet ef migrations add SupportStringUserKeys` instead of hand-editing — but
ONLY if it produces a clean, targeted migration; otherwise hand-edit the column
types + snapshot.

(This package appears pre-1.0; editing the column types so fresh installs get
string columns is acceptable. Note it in your report.)

## Step 4 — DTOs / requests

Change host-user-id fields from `int`/`int?` to `string`/`string?` in request
records (grep `src/Escalated` for these):
- `AdminTicketController` — `ReplyRequest.AuthorId`, `AssignRequest.AgentId`, `AssignRequest.CauserId`
- `CustomerTicketController` — `CreateTicketRequest.RequesterId`, `CustomerReplyRequest.RequesterId`
- `AdminSideConversationController` — `SideConversationReplyRequest.AuthorId`
- `AgentTicketController` — `CustomActionRequest.UserId`, `BulkActionRequest.CauserId`
- any other request/DTO with a host user id

## Step 5 — Remove int.Parse on host user ids

Replace `int.Parse(value)` / `Convert.ToInt32/64(value)` on host user ids with
the raw `string value`:
- `services/AutomationRunner.cs` (~141) — `ticket.AssignedTo = value;`
- `controllers/AgentTicketController.cs` (~228) — `AssignAsync(ticket, request.Value, ...)`
- `services/EscalationService.cs` (~104) — `AssignAsync(ticket, value, ...)`; (~107) `ChangeDepartmentAsync` keeps `int.Parse` (department is an internal id).
- `services/MacroService.cs` (~44, ~52) — pass `value` raw for assign actions.
- `services/AssignmentService.cs` (~22) — `AssignAsync(Ticket ticket, string agentId, int? causerId = null, ...)`. NOTE: `causerId` here is a host user id too → make it `string?`.

Keep `int.Parse`/int types for genuinely-internal ids (department id, status id, etc.).

## Step 6 — Compile ripple

Fix every resulting compile error (assignments, comparisons, string interpolation
already works). Anywhere a host user id was compared with `> 0` or `== 0` for a
"set?" check, use `string.IsNullOrEmpty(...)` instead.

## Step 7 — Test

Add a small test (match the repo's test project + framework, xUnit/NUnit) under
the test project verifying an entity round-trips a UUID-style string user id
(e.g. create a `Ticket` with `AssignedTo = "550e8400-..."`, save + reload via the
in-memory/SQLite test context, assert it persists). If the repo has a
`UserDirectory` test double, update it to the `string` signature.

## Step 8 — Build, test, format, commit

From repo root, make all green:

```
dotnet build
dotnet test
dotnet format --verify-no-changes   # if the repo uses dotnet format; else skip
```

Then commit (do NOT push):

```
git add -A
git commit -m "fix(users): support UUID/string host user keys"
```

Do NOT delete UUID_FIX_SPEC.md. Report every file changed and the final
build/test/format status, and flag the IUserDirectory signature change clearly
as a host-facing behavior change.
