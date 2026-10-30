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

## Single sign-on

With `ESCALATED_SSO` on (`Escalated:Access:Sso`), users can sign in through the
provider chosen in Admin > Settings > SSO (`sso_provider`: `none`, `saml`,
`oauth`, `jwt`). The page (`Admin/Settings/SsoSettings`) shows secrets masked as
`********` (posting the mask back keeps the stored value) and lists the
endpoints to give the identity provider. With `none`, `{prefix}/sso/login`
answers 404; a provider's routes answer 404 while another is chosen.

| Route | Use |
|---|---|
| `GET {prefix}/sso/login` | Start sign-in with the configured provider |
| `GET {prefix}/sso/saml/metadata` | SP metadata XML; its URL is the default SP entity ID |
| `POST {prefix}/sso/saml/acs` | SAML assertion consumer (HTTP-POST binding) |
| `GET {prefix}/sso/oauth/callback` | OAuth 2 / OIDC redirect URI |
| `GET\|POST {prefix}/sso/jwt/callback` | JWT hand-off (`jwt` parameter) |

- **SAML** (`System.Security.Cryptography.Xml`): the IdP certificate
  (`sso_certificate`) must be configured; the single assertion must carry a
  signature by it over the whole assertion (a signed Response alone, a key in
  `KeyInfo`, `EncryptedAssertion` and duplicate IDs are refused); `Issuer` must
  be `sso_entity_id`; `Audience` must name the SP entity ID; `Destination` and
  the bearer `Recipient` must be the ACS URL; the validity windows must hold
  (`Sso.ClockSkew`, default 120 s, at most 300); `InResponseTo` must be an
  AuthnRequest issued in the last 10 minutes (IdP-initiated responses only with
  `Sso.AllowIdpInitiated`); the assertion ID is single-use. Issued and used IDs
  live in `escalated_sso_message_ids` (migration `SsoMessageIds`).
- **OAuth 2 / OIDC** (Duende.IdentityModel): authorization code with a
  single-use `state` and a PKCE (`S256`) verifier kept in a data-protected
  cookie for 10 minutes; the code is exchanged at `sso_oauth_token_url` and
  the profile read from `sso_oauth_userinfo_url`. `email_verified: false` is
  refused.
- **JWT** (Microsoft.IdentityModel.JsonWebTokens): `alg` must be
  `sso_jwt_algorithm` (HS* with `sso_jwt_secret`, at least 32 bytes for
  HS256; RS* with the PEM public key or certificate in `sso_jwt_secret`);
  `exp` is required; `nbf`/`iat` hold within the skew; `aud` must be
  `Sso.JwtAudience` (default the install's base URL); `jti` is required and
  single-use.
- **Users.** The email comes from `sso_attr_email` (SAML falls back to an
  email-shaped NameID), the name from `sso_attr_name`, the role from
  `sso_attr_role`. The host user is found by `Sso.FindUserByEmail` (default:
  the `IUserDirectory` entry with that email, case-insensitive). An unknown
  email is refused unless `Sso.CreateUsers` is on and `Sso.CreateUser` creates
  the user (with an unusable password). The user is signed in with
  `Sso.SignIn` (default: `HttpContext.SignInAsync` on the default sign-in
  scheme with a `NameIdentifier` claim).
- **Roles.** With roles & permissions on and `Sso.SyncRoles` on, a role
  attribute naming an existing Escalated role replaces the user's roles (the
  `escalated-admin` / `escalated-agent` gate roles are kept and never granted).
  An unknown value changes nothing.
- On success the two-factor mark is cleared (SSO does not skip the
  challenge) and the browser goes to `Sso.RedirectTo` (default
  `{prefix}/agent`). Every refusal answers a generic 403 and logs the reason.
- Install-level options (each with an `ESCALATED_SSO_*` env fallback):
  `SpEntityId`, `AllowIdpInitiated`, `JwtAudience`, `ClockSkew`,
  `CreateUsers`, `SyncRoles`, `RedirectTo`, `BaseUrl`.

With the switch off every `{prefix}/sso/*` route and the settings page answer
404. Escalated does not turn off the host's own password login.
