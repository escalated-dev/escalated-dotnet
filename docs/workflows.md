# Workflows

Workflows are admin-defined, event-driven rules: when a [canonical event](../README.md#webhook-events)
fires on a ticket and the Workflow's conditions match, its actions run in order.
The wire format is the shared one in escalated-developer-context
`domain-model/workflow-admin-contract.md`, so the shared admin builder works
unchanged.

## Admin endpoints

All under `support/admin/workflows`, behind the `escalated-admin` policy. They are
JSON endpoints; keys are snake_case.

| Method | Path | Does |
|---|---|---|
| `GET` | `/` | `{ workflows }` in `position` order |
| `GET` | `/options` | `{ trigger_events, action_types, operators }`, each `[{ value, label }]` |
| `GET` | `/{id}` | `{ workflow, trigger_events, action_types, operators }` |
| `POST` | `/` | creates; `201 { workflow }` |
| `PUT` | `/{id}` | updates; `200 { workflow }` |
| `DELETE` | `/{id}` | deletes it, its logs and its held delayed actions; `204` |
| `POST` | `/{id}/toggle` | flips `is_active`; `200 { workflow }` |
| `POST` | `/reorder` | `{ "workflow_ids": [3, 1, 2] }` sets `position` |
| `GET` | `/logs?workflow={id}` | `{ logs, workflows }`: the newest 100 executions |
| `GET` | `/{id}/logs` | the same, for one Workflow |

Create and update take the builder's body, with the keys at the top level:

```json
{
  "name": "Route refunds to billing",
  "description": null,
  "trigger_event": "ticket.created",
  "conditions": { "all": [{ "field": "subject", "operator": "contains", "value": "refund" }] },
  "actions": [
    { "type": "change_priority", "value": "high" },
    { "type": "set_department", "value": "4" }
  ],
  "is_active": true
}
```

- `name`, `trigger_event` and at least one action are required.
- `trigger_event` must be one this package fires (`/options`). A legacy name such
  as `ticket.replied` is stored as its canonical name.
- `conditions` holds exactly one of `all` or `any`. Omitted, it is stored as
  `{"all": []}`, which matches every ticket.
- Each action is `{type, value}` with a scalar value, and its type must be one the
  executor runs.

Anything else answers `422` with `{ message, errors: { field: [..] } }`.

## Conditions

Fields: `status`, `priority`, `ticket_type`, `subject`, `description`, `tags`,
`department_id`, `assigned_to`, `hours_since_created`, `hours_since_updated`,
plus `reference`, `requester_id`, `guest_email`, `guest_name` and `account`.
`tags` is a list: `equals`/`contains` mean the ticket has the tag (by name,
case-insensitive), `not_equals`/`not_contains` that it does not.

Workflows stored in older shapes keep evaluating: a flat list of conditions
(read as `all`) and `{match, rules}`.

## Actions

| `type` | `value` |
|---|---|
| `change_status` | status slug |
| `change_priority` | priority slug |
| `add_tag`, `remove_tag` | tag name (slug or id also work) |
| `set_department` | department id |
| `assign_agent` | host user id |
| `add_note` | note body; `{{subject}}`, `{{reference}}` and other ticket fields are filled in |
| `insert_canned_reply` | reply body, with the same placeholders |
| `add_follower` | host user id |
| `set_type` | ticket type, e.g. `incident` |
| `send_webhook` | URL. POSTs `{ticket: {id, reference, subject, status, priority}}` through the SSRF-guarded webhook client: http(s) to public addresses only, no redirects |
| `delay` | minutes. The actions after it are held and run when due |

`move_department` and `add_internal_note` (older names) still run but are not offered.

### Delays

A `delay` stores the remaining actions in `escalated_delayed_actions`.
`WorkflowDelayedActionBackgroundService` (registered by `AddEscalated`) runs
the due ones every minute through `WorkflowDelayedActionProcessor`. Before
running, it checks the Workflow's conditions again against the ticket as it is
then. It cancels the held actions instead when they no longer match, when the
ticket is resolved or closed, or when the Workflow was disabled or deleted.
Actions run after a delay do not trigger other Workflows, as with any Workflow
action.
