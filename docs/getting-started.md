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

### Who a request acts as

The acting user is always the signed-in user: the requester of a ticket a customer
creates, the author of a reply, the causer recorded on ticket activity, the owner
of a mention inbox. Escalated reads the id from the `NameIdentifier` claim, then
`sub`, then `id`. If your user id lives in another claim, set a resolver:

```csharp
builder.Services.Configure<EscalatedOptions>(o =>
    o.UserIdResolver = user => user.FindFirst("employee_id")?.Value);
```

Ids in the query string or the request body (`requesterId`, `agentId`, `userId`,
`currentUserId`, `authorId`, `causerId`, `createdBy`) are not read. A customer
sees and acts on only the tickets they requested. Staff can download any
attachment; a customer can download only attachments on their own tickets, and
never one on an internal note.
