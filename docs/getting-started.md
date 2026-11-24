# Getting started

## Requirements

- .NET 8.0+
- Entity Framework Core 9.0.x (9.0.21+)
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

// Your authentication: ASP.NET Core Identity, OpenID Connect, JWT bearer, cookies.
// Escalated authorizes whoever it signs in (see Authorization below).
builder.Services.AddAuthentication(/* ... */);

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

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
    "Email": {
      "Domain": "support.yourapp.com",
      "InboundSecret": "a-long-random-value"
    }
  }
}
```

The `Email.InboundSecret` is symmetric -- it signs outbound `Reply-To` addresses *and* verifies inbound webhook requests, so forged emails targeting a stolen reply address are rejected via timing-safe HMAC.

Guests verify their email with an emailed code before they open a ticket, start a chat or look a ticket up, and get an expiring access grant; see [docs/guest-access.md](guest-access.md). That needs working outbound mail.

Notification emails (new ticket, replies, assignment, status, escalation, SLA breach, mentions) go out once `Email.Smtp.Host` is set; until then nothing is sent and that is logged once. See [docs/notification-emails.md](notification-emails.md) for the recipients, settings and threading.

### 4. Create the tables

The package ships its migrations for SQLite, PostgreSQL and SQL Server. Apply them at startup:

```csharp
var app = builder.Build();
await app.Services.MigrateEscalatedDatabaseAsync();
```

or from the command line with `dotnet ef database update --context EscalatedDbContext`.
A database created earlier with `EnsureCreated()` is adopted: the baseline is recorded
as applied and only later migrations run.

Visit `/support` -- you're live.

### Required: schedule the recurring jobs

Time-based work only happens when a job runs it. `AddEscalated()` registers each job
as a hosted service, so they start with your app and need no scheduler:

| Job (hosted service) | Interval | What it does |
|---|---|---|
| `SlaMonitorBackgroundService` | every minute | SLA warnings and breaches; records the heartbeat |
| `EscalationBackgroundService` | every 5 minutes | Escalation rules |
| `AutomationBackgroundService` | every 5 minutes | Automations |
| `WorkflowDelayedActionBackgroundService` | every minute | Delayed workflow actions |
| `TicketSnoozeBackgroundService` | every minute | Wakes snoozed tickets |
| `ChatCleanupBackgroundService` | every minute | Closes idle and abandoned chats |
| `RetentionPurgeBackgroundService` | daily | Data retention purges |

They only run while the app process runs. On IIS, turn off the application pool's
idle time-out (or use the AlwaysRunning start mode), or they stop between requests.
If you remove these hosted services, for example to run them in a separate worker
process, call each one's `RunOnceAsync(ct)` on the interval above.

Each completed SLA check records a heartbeat (the `scheduler_sla_check_at` row in
the settings table, shared by every instance). When it is missing or older than 15
minutes:

- the admin Settings, Reports and Reports dashboard pages get a `scheduler` prop
  with `healthy: false` and the frontend shows a warning naming the jobs above. The
  same object (`{healthy, last_run_at, message, commands: [{command, interval}]}`)
  is served as JSON at `GET /support/admin/settings/scheduler`;
- the `escalated-scheduler` health check reports Degraded. Map health checks to see
  it: `app.MapHealthChecks("/health");`
- a warning is logged (at most once every 15 minutes per process) whenever the
  status is read.

Right after the first start the heartbeat is missing until the first SLA check
finishes, a few seconds later.

### Separate databases

`EscalatedDbContext` has its own connection string, so Escalated's tables go
wherever `ConnectionStrings:Escalated` points. Nothing requires that to be your
application's database — it can be a schema shared with a legacy system, a
separate reporting store, or simply a database you would rather not mix support
data into:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    "Escalated": "Server=support-db;Database=Support;Trusted_Connection=true;"
  }
}
```

Nothing else changes. `AddEscalated` already resolves that string, and
`dotnet ef database update --context EscalatedDbContext` migrates only that
database — your own `DbContext` keeps its own migrations.

The two connections need not even be the same provider:

```csharp
builder.Services.AddEscalated(builder.Configuration, options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Escalated")));
```

#### Your users stay where they are

Escalated owns no `User` entity. The admin users page reads your users through
`IUserDirectory`, which you implement against **your** `DbContext`, with your
query, on your connection:

```csharp
builder.Services.AddSingleton<IUserDirectory, MyUserDirectory>();
```

Registered before `AddEscalated`, your implementation wins; without one, a
`NullUserDirectory` returns an empty page rather than querying a database that
may have no users table in it.

That is also why no Escalated entity declares a foreign key to your users:
`Ticket.RequesterId`, `Ticket.AssignedTo` and `Reply.AuthorId` are plain
unconstrained columns, so the two databases need never meet. Tests fail the
build if an entity, a foreign key or any raw SQL in the package names a
host-owned table.

No query joins the two, because no database can join across two connections. The
cost is that Escalated cannot filter or sort its tables by a column that lives on
your user — assignment, skill routing and agent load all resolve ids from
Escalated's tables first, then ask your directory for the people.

### Host user keys (UUID / string support)

Escalated stores references to your app's users (ticket requester, assignee,
reply author, etc.) as **strings**, so it works whether your user primary key
is an `int`, a `Guid`, or any other string. A numeric id is simply stored and
exchanged as its string form.

Your `IUserDirectory` implementation therefore exchanges user ids as `string`:

```csharp
public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default)
{
    // look up your user by its (string) id and return:
    //   new UserDirectoryEntry(user.Id.ToString(), user.Name, user.Email)
}
```

> **Upgrade note:** earlier versions used `int` here (`FindAsync(int id)` and
> `UserDirectoryEntry(int Id, ...)`); admin user APIs also now return user ids
> as JSON strings. Update your `IUserDirectory` implementation to the `string`
> signatures shown above. Integer ids keep working — pass them as strings.

## Authorization

Escalated owns no authentication. It authorizes whoever your application signed
in, reading them from `HttpContext.User`. Register your authentication and call
`UseAuthentication()` and `UseAuthorization()` before `MapControllers()`.

Every controller requires one of three policies:

| Policy | Surface | Default rule |
|--------|---------|--------------|
| `EscalatedPolicies.Admin` (`escalated-admin`) | `/support/admin/*`, `/admin/newsletters*` | Signed in and holds the `escalated-admin` role |
| `EscalatedPolicies.Agent` (`escalated-agent`) | `/support/agent/*` | Signed in and holds `escalated-agent` or `escalated-admin` |
| `EscalatedPolicies.Customer` (`escalated-customer`) | `/support/tickets*`, `/support/attachments/*` | Signed in |

The widget, guest, inbound email, newsletter tracking and newsletter ESP webhook
endpoints are public, as is `/support/api/v1/auth/*`. They authenticate with guest
tokens, shared secrets or your `ApiAuth` callbacks instead.

Nothing is allowed by default. An anonymous request gets 401, and a signed-in user
without the role gets 403. An application with no authentication configured gets
an ASP.NET Core configuration error, not an open endpoint.

### Granting access

The roles are rows in Escalated's `RoleUsers` table: the same ones the admin users
page grants and revokes (`PATCH /support/admin/users/{id}/role`). Grant the first
admin directly:

```csharp
var admin = new Role { Name = "Escalated Admin", Slug = AdminUsersController.AdminRoleSlug, IsSystem = true };
db.Roles.Add(admin);
await db.SaveChangesAsync();

db.RoleUsers.Add(new RoleUser { RoleId = admin.Id, UserId = "your-user-id" });
await db.SaveChangesAsync();
```

Or replace a policy with your own rule. A policy registered under the same name,
before or after `AddEscalated`, takes precedence:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(EscalatedPolicies.Admin, p => p.RequireRole("SupportLead"));
    options.AddPolicy(EscalatedPolicies.Agent, p => p.RequireRole("SupportLead", "SupportAgent"));
});
```

With roles and permissions on (`ESCALATED_ROLES_PERMISSIONS`, off by default),
every admin and agent route also needs a permission from the roles the user
holds, and the newsletter pages check `newsletters.manage` / `newsletters.send`.
See [access control](access-control.md). With it off, the policies above
decide everything.

### Checking the setup

When the app starts, Escalated logs a warning (`Escalated setup: ...`) for each of
these, and the `escalated-setup` health check reports Degraded with the same text
until they are fixed (`app.MapHealthChecks("/health")` to see it):

- no `IUserDirectory` is registered, so users show by id and the admin users page is
  empty;
- Escalated's default `escalated-admin` policy is in use and nobody holds the
  `escalated-admin` role, so nobody can open the admin;
- Escalated's default `escalated-agent` policy is in use and nobody holds
  `escalated-agent` or `escalated-admin`.

A policy you registered yourself is not checked.

### Staff-only installs

By default the customer portal and the knowledge base accept any signed-in user
(the knowledge base also anonymous visitors, while the `knowledge_base_public`
setting is on), and the widget and guest routes are public. In an install where
only staff use Escalated, someone you never gave Escalated access could still list
and raise tickets there. To close that:

```json
{
  "Escalated": {
    "Routes": { "CustomerPolicies": [ "escalated-agent" ] },
    "Widget": { "Enabled": false },
    "GuestAccess": { "RoutesEnabled": false }
  }
}
```

- `Escalated:Routes:CustomerPolicies` lists authorization policies every customer
  portal route (`/support`, `/support/create`, `/support/{reference}*`, the JSON
  `/support/tickets*`) and every knowledge base page (`/support/kb*`) must pass as
  well as `escalated-customer`. Any policy name works: Escalated's
  `escalated-agent`, or one of your own. A signed-in user who fails one gets 403;
  an anonymous visitor is challenged, so the knowledge base stops being public.
  Default empty, which changes nothing.
- `Escalated:Widget:Enabled` (`ESCALATED_WIDGET_ENABLED`) and
  `Escalated:GuestAccess:RoutesEnabled` (`ESCALATED_GUEST_ROUTES`) set to `false`
  leave the widget and guest routes out of the route table. Both default to `true`.
  See [guest access switches](guest-access.md#switches).

Escalated has no Slack inbound or cloud webhook routes, so there is nothing else to switch off.

### Who a request acts as

The acting user is always the signed-in user: the requester of a ticket a customer
creates, the author of a reply, the causer recorded on ticket activity, the owner
of a mention inbox. Escalated reads the id from the `NameIdentifier` claim, then
`sub`, then `id`. If your user id lives in another claim, set a resolver:

```csharp
builder.Services.Configure<EscalatedOptions>(o =>
    o.UserIdResolver = user => user.FindFirst("employee_id")?.Value);
```

If a request passes a staff policy (`escalated-admin`, `escalated-agent`, or
`escalated-user` on an embedded admin) but no user id can be read from it, which
happens when you replaced a policy (for example with `RequireRole`) and your user id
lives in a claim Escalated does not read, the request fails with
`EscalatedUserNotResolvedException`, whose message says how to fix it, instead of
acting as nobody. Escalated's default policies refuse such a request with 403.

Ids in the query string or the request body (`requesterId`, `agentId`, `userId`,
`currentUserId`, `authorId`, `causerId`, `createdBy`) are not read. A customer
sees and acts on only the tickets they requested. Staff can download any
attachment; a customer can download only attachments on their own tickets, and
never one on an internal note.
