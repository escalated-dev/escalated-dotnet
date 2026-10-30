# Access control: roles & permissions, two-factor, SSO

Escalated's roles & permissions, two-factor authentication and single sign-on
are **install-level switches**, off by default
([ADR 2026-10-29](https://github.com/escalated-dev/escalated-developer-context/blob/main/decisions/2026-10-29-configurable-access-features.md)).
While a switch is off its screens and routes answer 404 and nothing is
enforced: the `escalated-admin` / `escalated-agent` policies decide who may do
what, exactly as before. While it is on, the feature is enforced.

The switches are read from `EscalatedOptions.Access` (configuration section
`Escalated:Access`), else the environment. No admin screen or stored setting
changes them.

| Feature | Option | Env var |
|---|---|---|
| Roles & permissions | `Access.RolesPermissions` | `ESCALATED_ROLES_PERMISSIONS` |
| Two-factor authentication | `Access.TwoFactor` | `ESCALATED_TWO_FACTOR` |
| Single sign-on | `Access.Sso` | `ESCALATED_SSO` |

```csharp
builder.Services.Configure<EscalatedOptions>(o => o.Access.RolesPermissions = true);
```

Every page shares them as `escalated.features.roles_permissions`,
`escalated.features.two_factor` and `escalated.features.sso`; the shared
frontend hides the navigation entries of a feature that is off.

## Roles & permissions

### What is enforced

With the switch on, every admin, agent and staff API route (`{prefix}/admin/*`,
`{prefix}/agent/*`, `{prefix}/api/*` except the mobile guest and host-auth
APIs, and `/admin/newsletters*`) needs a permission **in addition to** the
policy for that area. A user's permissions are the union of the permissions of
the roles they hold (`escalated_role_user`). Nothing else grants one: the gate
roles `escalated-admin` and `escalated-agent` (or a host policy) only decide
who reaches an area. Give users who should have everything the `admin` role.

- The route to permission map is `Escalated.Authorization.RoutePermissions`
  (escalated-laravel's `RoutePermissions`, keyed by method and route
  template). A staff route missing from it is refused, and a test checks that
  every staff route in the endpoint table is in it. Reads need `*.view`,
  writes `*.manage` (or the finer `create`/`edit`/`delete`/`publish` slugs).
- A status change needs `ticket.edit`, plus `ticket.close` when the new status
  is `resolved`, `closed` or `reopened`.
- Bulk actions need the permission of the action: `assign` needs
  `ticket.assign`, `delete` needs `ticket.delete`, the rest `ticket.edit`.
- Applying a macro needs `macro.view` and `ticket.edit`.
- API tokens act as their owner (`ApiTokenAuthMiddleware` signs the request in
  as the token's owner when the host has not), so a token request is checked
  against the owner's permissions.
- The newsletter screens check `newsletters.manage` and `newsletters.send`
  only while the switch is on; while it is off the admin policy alone decides.

Failing a permission answers 403. `escalated.permissions` is the user's slugs
while the switch is on, and `['*']` for staff (`[]` for anyone else) while it
is off.

### Seeded roles

`Escalated.Authorization.PermissionSeeder` holds escalated-laravel's permission
set and system roles, and runs at startup whenever the switch is on (no schema
change is needed). Seeding only adds what is missing.

| Role | Permissions |
|---|---|
| `admin` | every permission |
| `agent` | `ticket.*`, `reply.*`, `kb.view`, `report.view`, `macro.view`, `macro.create`, `tag.view`, `custom_field.view`, `audit.view` |
| `light_agent` | `ticket.view`, `reply.create`, `reply.create_internal`, `kb.view`, `macro.view`, `tag.view` |

### Turning it on

1. Seed (start the app once with the switch on, or call
   `PermissionSeeder.SeedAsync()`), and give every staff user a role: at least
   one admin with `PermissionSeeder.AssignRoleAsync(userId, "admin")`
   (escalated-laravel's `escalated:roles:assign`), the rest from the users
   page.
2. Set `ESCALATED_ROLES_PERMISSIONS=true` (or `Escalated:Access:RolesPermissions`).

Users with no permission-granting role have no permissions: turning the switch
on without assigning roles locks staff out of every admin and agent screen.
