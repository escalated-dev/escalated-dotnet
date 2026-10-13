# Ticket access and routing

All options preserve existing behavior until enabled. Apply the schema migration before enabling them. Tickets gain nullable indexed `AccountType`/`AccountId` columns; SLA policies gain nullable `DepartmentId`/`TicketType` targets. The new `escalated_department_agent` table relates package department IDs to host user IDs without joining the host database.

## Configuration

```csharp
builder.Services.Configure<EscalatedOptions>(options =>
{
    options.Visibility = "department"; // default: "all"
    options.Accounts.Enabled = true;   // default: false
    options.Accounts.ScopeAgents = true; // default: false
    options.Accounts.Resolver = async (reference, cancellationToken) =>
    {
        // Host lookup: reference.Kind is "requester", "subject", or "agent".
        // Type and Id identify the host record. Agent Model is its ClaimsPrincipal.
        var account = await directory.FindAccountAsync(reference.Kind,
            reference.Type, reference.Id, cancellationToken);
        return account == null ? null : new TicketAccount("Company", account.Id);
    };
    options.TargetedSlas = true; // default: false
});
```

`TicketAccount` and `TicketAccountReference` are in `Escalated.Authorization`. Account scope requires accounts enabled and a resolver; invalid visibility options fail at startup. The resolver returns one complete, case-sensitive identity, with each component between 1 and 191 characters. A missing account hides all tickets from that scoped agent. To serve multiple accounts with one agent, use department visibility without single-account scoping.

Use `EscalatedDbContext.DepartmentAgents` to manage `DepartmentAgent { DepartmentId, AgentId }` membership from the host's access system. In department mode, an agent with no memberships sees no tickets. Tickets without a department are also hidden. Department and account restrictions intersect. Existing admin policies grant full access. Requester ownership and guest token authentication continue to apply.

The package uses its existing `escalated-agent` and `escalated-admin` authorization policies. If those policies rely on host claims, configure `TicketAccessUserResolver` to reload a current authenticated `ClaimsPrincipal` by user ID for bearer tokens, assignment, notifications, and live delivery. The default uses the package role tables. Role, department and account changes are rechecked on subsequent requests and deliveries.

## Queries and writes

An async request filter surrounds package MVC execution and serialization. Bearer authentication scopes by the token owner even when a different HTTP user is already authenticated. Global EF filters cover list/search queries, reports, bulk updates/deletes, the widget, and related ticket records. Links require access to both tickets. Package services recheck loaded tickets before mutations; `SaveChanges` also checks existing tickets and direct or indirect child parents.

EF's `Find`/`FindAsync` can return a previously tracked object without querying. Use scoped LINQ queries or `TicketAccess.CanViewAsync` when consuming objects loaded before the scope. Package ticket/chat/conversation lookups use LINQ queries. Keep DbContexts request-scoped and do not reuse tracked graphs across users.

For host minimal APIs or custom job execution on behalf of a user, resolve `TicketAccess` and wrap **all** queries, mutations and serialization in `ForUserAsync(principal, async () => ...)`. MVC routes registered with `AddEscalated` receive the filter automatically. Background work with no actor remains unrestricted. Trusted maintenance code can deliberately use `TicketAccessScope.WithoutVisibilityAsync`; `IgnoreQueryFilters`, raw SQL, and independent host database access are also explicit bypasses and must never be chosen from request input.

Account identity is filled on ticket creation from the requester or on subject attachment when both account fields are empty. Existing account identity is retained and ticket splitting copies it. Populate older tickets from trusted host code before enabling account scope. Agent list filters accept `AccountType` plus `AccountId`; a partial pair matches nothing. The host owns company models, event-driven intake, subject mapping, and membership synchronization.

## Rules and SLA policies

Escalations and automations accept conditions such as:

```json
[
  {"field":"ticket_type","operator":"equals","value":"incident"},
  {"field":"account","operator":"equals","value":{"type":"Company","id":"42"}}
]
```

Workflows use their existing `{"all":[...]}` or `{"any":[...]}` envelope with the same conditions. Account equality compares both components; `not_equals` also requires a complete expected pair. Malformed account conditions never match. Type conditions and existing department conditions allow per-stream routing.

With `TargetedSlas` enabled, active matching policies are selected in this order: department plus type, department, type, then untargeted default. Lowest policy ID breaks ties. With it disabled, targeted policies are excluded from automatic selection. SLA support must also be enabled through the existing `Sla.Enabled` option.

With targeted SLAs enabled, creation through `TicketService.CreateAsync` selects an SLA before dispatching the creation event. Changing department or type preserves the attached policy and clock. Explicitly call `SlaService.AttachDefaultPolicyAsync` to reselect; due times remain based on the ticket's original creation time. Admin SLA create/update endpoints accept both target fields.

## Notifications and SignalR

Assignment, automatic assignment, chat assignment and mention delivery reject agents outside the ticket's scope. Host follower notification code should use `FollowerRecipients.ResolveAsync` and resolve recipients when delivering queued notifications. The original synchronous helper only deduplicates IDs and does not authorize delivery.

In restricted mode the hub registers authorized subscriptions in `TicketRealtimeAudience`, instead of shared SignalR groups. Host event publishers must use `TicketBroadcastService.SendAsync(ticketId, room, eventName, payload)`, which rechecks recipients against current roles, departments, account identity or guest tokens. Direct group broadcasting will not deliver to these restricted subscriptions. Ticket-list and chat-queue feeds require staff access and filter each event by its ticket. Guest ticket/chat subscriptions supply `guest_token` in the SignalR handshake; authenticated agents cannot use it to bypass their own scope.

The default audience registry is process-local. A host with multiple SignalR nodes must register a shared implementation of `TicketRealtimeAudience` or route ticket events to each node. Default unrestricted mode keeps the existing group behavior.

## Migration and verification

`20261013000001_TicketAccessAndRouting` upgrades and rolls back the new schema without deleting existing ticket rows. As with the existing package schema, hosts that own their EF migration assembly should generate and review the corresponding migration using their configured provider. Retain binary account column collation (`BINARY` on SQLite, `C` on PostgreSQL, `Latin1_General_100_BIN2` on SQL Server), and apply migrations before restarting workers on the new package.

Integration coverage exercises independent list, search, detail, bulk, widget, report and bearer API paths; related records and preloaded writes; concurrent requests; account identity and resolver hooks; all three rule engines; SLA selection and clock preservation; recipient revocation; and migration upgrade/rollback. CI runs the full suite against SQLite and PostgreSQL. SQL Server migration generation is checked without claiming a live SQL Server execution.
