# Authoring request storage

This page gives the tables that hold [authoring requests](authoring-requests.md) in each tenant
database. The other system tables are in [storage](storage.md#system-tables).

## Authoring requests

`Axis.Configuration` owns the two tables of [authoring requests](authoring-requests.md#durable-requests)
in the `axis` schema of each tenant database. The server inserts a request and changes its state
when the author approves, changes the plan or drops it. The agent host writes the rest.

- **`axis.authoring_requests`.** One row per request: `id` (`uuid`, a version 7 UUID),
  `application_id` (`uuid`, the application `id`), `author` (`text`, a user id), `text`, `state`
  (`text`, see [states](authoring-requests.md#states)), `binding`, `plan`, `claimed_by` (all `text`,
  null), `draft_id` (`uuid`, null), `round` (`integer`) and `tokens` (`bigint`), both 0 at first,
  `auto_accept` (`boolean`, false at first), `sent_at` (`timestamptz`), and `approved_at`,
  `finished_at` and `claimed_until` (`timestamptz`, null). Indexes on
  `(application_id, sent_at, id)` and `(sent_at)` serve the list and the monthly count.
- **`axis.authoring_request_steps`.** One row per step: `id` (`uuid`), `request_id` (`uuid`, deleted
  with its request), `ordinal` (`integer`, unique per request), `kind`, `input_summary` and `outcome`
  (`text`, null), `tokens` (`bigint`), `messages` (`jsonb`, null), `started_at` (`timestamptz`) and
  `finished_at` (`timestamptz`, null). A comment that asks for another plan is a `planChange` step.
