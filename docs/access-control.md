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

## Two-factor authentication

With `ESCALATED_TWO_FACTOR` on (`Escalated:Access:TwoFactor`), staff sign in
with the host's own login and then pass a TOTP challenge before any admin or
agent page.

- **Enrolment** is `{prefix}/admin/settings/two-factor`
  (`Admin/Settings/TwoFactor`), open to every staff user (the agent policy),
  not only admins, and needing no permission. `setup` flashes
  `two_factor_setup` (`qr_uri`, `recovery_codes`), `confirm` takes a 6-digit
  `code` and flashes `two_factor_confirmed`, `disable` removes the enrolment.
  Enrolments live in `escalated_two_factors` (migration `TwoFactor`, all three
  providers). The secret and recovery codes are encrypted with ASP.NET Core
  Data Protection and never sent in props; keep the host's key ring stable
  (and shared across instances), or existing enrolments cannot be read and
  their challenges fail.
- **TOTP** is RFC 6238 (SHA-1, 6 digits, 30 s) through Otp.NET, one step of
  drift either side. The accepted time step is recorded, and that step or any
  earlier one is refused, so a code works once.
- **The challenge.** A staff user with a confirmed enrolment who has not passed
  it in this browser session is redirected from every admin and agent page to
  `GET {prefix}/two-factor/challenge` (`Auth/TwoFactorChallenge`), which posts
  `code` or `recovery_code` back to the same path. Success marks the browser
  session (a data-protected, HTTP-only `escalated_two_factor` cookie holding
  the user id, so no session middleware is needed) and returns to the page
  asked for. Failures are errors on the field sent; 5 attempts per user per
  minute, then 429. JSON clients get `403 {"two_factor_required": true}`. API
  token requests are not challenged.
- **Every new login is challenged.** A cookie-authentication sign-in or
  sign-out on any scheme clears the mark (Escalated hooks
  `CookieAuthenticationEvents`). A host that signs users in some other way
  calls `TwoFactorSession.Forget(HttpContext)` on login.
- **Recovery codes.** Eight single-use codes are issued at setup.
- **Required.** `Escalated:TwoFactor:Required`
  (`ESCALATED_TWO_FACTOR_REQUIRED`, default false), or the
  `two_factor_required` setting (the "Require for all staff" switch on the
  two-factor page posts only that key to Admin > Settings; the setting wins
  when present). Staff without an enrolment are then sent to enrol and reach
  nothing else. Shared as `escalated.two_factor_required`.
- **Lost device.** `TwoFactorService.ResetAsync(userId)` removes an enrolment.

With the switch off nobody is challenged, the enrolment and challenge routes
answer 404, and stored enrolments are kept.
