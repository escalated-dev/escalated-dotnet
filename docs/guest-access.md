# Verified guest access

Guests prove they own an email address before they open a ticket, start a live
chat or look a ticket up. They then get an expiring access grant for that ticket.
This follows escalated-laravel's `docs/guest-access.md`.

## Setting it up

- **Outbound mail.** Codes are emailed through `IEscalatedMailTransport`: configure
  `Escalated:Email:Smtp` (see [notification emails](notification-emails.md)) or
  register your own transport. Without one, no code can arrive.
- **A shared secret.** Set `Escalated:GuestAccess:Secret` to the same value on every
  instance. Emails and codes are stored only as HMAC digests under it, and it falls
  back to `Escalated:Email:InboundSecret`. With neither set, each process uses a
  random key and logs a warning: codes then do not survive a restart and do not work
  across instances.
- **Run the `VerifiedGuestAccess` migration** (`MigrateEscalatedDatabaseAsync()`).

| Option (`Escalated:GuestAccess:`) | Default | |
|---|---|---|
| `TtlMinutes` | `1440` | Grant lifetime, clamped to 5 minutes through 7 days |
| `LegacyTokens` | `false` | Accept the permanent guest tokens issued before this release |
| `ChallengesPerClientPerHour` | `3` | Codes one client IP may request for one mailbox per hour |
| `ChallengesPerMailboxPerHour` | `10` | Codes one mailbox may be sent per hour, from all IPs |

## Email codes

`POST verification` is served on `support/widget` and `support/guest`. It takes
`{ "email": "gail@example.com", "purpose": "ticket" }`, where `purpose` is one of
`ticket`, `chat` or `lookup`.

It answers 202 `{ "verification_id", "expires_in": 600, "message" }`. The
eight-digit code goes only to the mailbox.

A code is:
- bound to the normalized (trimmed, lower-cased) email and to the purpose;
- valid for 10 minutes;
- allowed 5 guesses, each of which counts;
- spent once. If the operation it authorized then fails, it is released, so the
  guest can retry.

When a mailbox's budget is spent, the answer is 429 with `Retry-After`.
`verification` and `lookup` also have a per-IP limit
(`Escalated:GuestRateLimit:VerificationsPerMinute`, default 10). Expired codes are
removed as new ones are made.

`ticket` and `lookup` codes need guest tickets to be on.

Host code can call `GuestAccessService.IssueCodeAsync` to make a code and deliver it
some other way.

## Tickets and grants

`POST support/widget/tickets` takes `name`, `email`, `subject`, `description`,
`department_id?`, `metadata?`, `verification_id` and `verification_code`. It
answers 201:

```json
{ "message": "Ticket created successfully.", "reference": "ESC-00042", "guest_access_token": "…", "expires_at": "2026-10-22T10:00:00Z" }
```

The token is an opaque bearer credential. Only its SHA-256 is stored on the ticket,
and issuing a new grant (on lookup) revokes the previous one.
`GuestAccessService.RevokeAsync(ticket)` revokes it from host code.

- `GET support/widget/tickets/{reference}` needs `Authorization: Bearer <token>` for
  that reference. It answers the Status tab's fields (`reference`, `subject`,
  `status`, `status_label`, `status_color`, `created_at`, `department`) and the 20
  latest public `replies`.
- `POST .../reply` (or `.../replies`) and `POST .../rate` take the same header.
- The SignalR hub accepts the token as `guest_token` when joining the ticket's room.

Guest answers carry `Cache-Control: no-store, private` and
`Referrer-Policy: no-referrer`.

## Lookup

`POST lookup` (on `support/widget` and `support/guest`) takes `email`, `reference`,
`verification_id` and `verification_code` from a `lookup` code. It answers
`{ "data": [{ "reference", "subject", "guest_access_token", "expires_at" }] }`.

- A reference alone unlocks nothing: the verified email must be the ticket's guest
  email.
- The code is spent even when nothing matches.

## Guest pages

With `Escalated:Ui:Enabled` on, the shared `Guest/Create` and `Guest/Show` pages
run the same flow in a browser (see [inertia-ui.md](inertia-ui.md)):
`GET support/guest/create` (404 while `guest_tickets_enabled` is off) posts to
`POST support/guest` with `guest_name`, `guest_email`, `subject`, `description`,
`priority`, `department_id`, `attachments[]` and a `ticket` code, and redirects
to the private link `GET support/guest/{token}`. That page takes `reply` and
`rate` posts, and serves the ticket's files (never an internal note's) at
`support/guest/{token}/attachments/{id}`, all until the grant expires.

## Live chat

`POST support/widget/chat/start` needs `email` and a `chat` code. It is throttled
per IP like ticket creation. The chat's own visitor token (`id`) then reaches the
chat routes. Like Laravel's chat grant it expires `Escalated:GuestAccess:TtlMinutes`
after the chat starts (`expires_at`) and only its SHA-256 is stored; after that
every chat route answers 404 and the visitor starts a new chat.

## Switches

| Setting | Default | Effect when `0` / `false` |
|---|---|---|
| `widget_enabled` | on | Every widget route answers 403; `/config` answers `{ "enabled": false }` |
| `guest_tickets_enabled` | on | Guest ticket creation, `ticket`/`lookup` codes and lookups answer 403; `/config` reports `false` |

## Existing tokens

The permanent tokens issued before this release no longer open tickets. Set
`LegacyTokens` to `true` to accept them while widgets and links move over. Ticket
creation then also returns the old `guestToken`. Guests can always verify their
email and look a ticket up by reference to get a new link.
