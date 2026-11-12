# Authoring request storage

This page gives the tables that hold [authoring requests](authoring-requests.md) in each tenant
database. The other system tables are in [storage](storage.md#system-tables).

## Authoring requests

`Axis.Configuration` owns the two tables of [authoring requests](authoring-requests.md#durable-requests)
in the `axis` schema of each tenant database. The server inserts a request and changes its state
when the author approves, changes the plan or drops it. The publisher moves a `ready` request to
`accepted` when the accept of its draft succeeds. The agent host writes the rest.

- **`axis.authoring_requests`.** One row per request: `id` (`uuid`, a version 7 UUID),
  `application_id` (`uuid`, the application `id`), `author` (`text`, a user id), `text`, `state`
  (`text`, see [states](authoring-requests.md#states)), `binding`, `plan`, `claimed_by` (all `text`,
  null), `draft_id` (`uuid`, null), `round` (`integer`) and `tokens` (`bigint`), both 0 at first,
  `auto_accept` (`boolean`, false at first), `sent_at` (`timestamptz`), and `approved_at`,
  `finished_at` and `claimed_until` (`timestamptz`, null), and `refresh_token` (`bytea`, null). The
  `round` counts the request's moves to `fixing`, its fix rounds, see [fix rounds](authoring-requests.md#budgets). The
  `refresh_token` holds the agent host's refresh token, encrypted with the tenant data key in the
  [sensitive value format](data-encryption.md#sensitive-field-values) and bound to the tenant and the
  request. No route answers it, and a drop, an accept or a failure sets it to null. Indexes on
  `(application_id, sent_at, id)` and `(sent_at)` serve the list and the monthly count.
- **`axis.authoring_request_steps`.** One row per step: `id` (`uuid`), `request_id` (`uuid`, deleted
  with its request), `ordinal` (`integer`, unique per request), `kind`, `input_summary` and `outcome`
  (`text`, null), `tokens` (`bigint`), `messages` (`jsonb`, null), `checks` (`jsonb`, null), `started_at` (`timestamptz`) and
  `finished_at` (`timestamptz`, null). A comment that asks for another plan is a `planChange` step. A step that
  never finished has the `outcome` `interrupted` once a later host closes it, see
  [resuming](authoring-requests.md#durable-requests). A step that found the author's token ended
  has the `outcome` `tokenEnded`, see [acting for the author](authoring-requests.md#acting-for-the-author).
  A planning step that wrote no plan has the `outcome` `noPlan`, see
  [planning calls](authoring-requests.md#budgets). A working step that did not finish the change has the
  `outcome` `unfinished`, see [working calls](authoring-requests.md#budgets), and the working step sets
  `draft_id` once it makes the request's draft. A `fix` step works on that draft, see
  [fixing](authoring-requests.md#states). It has no `outcome` when it moves the request to `checking`, and
  the `outcome` `unfinished` otherwise, and it stores no `checks`. A step stopped by the token budget has the `outcome`
  `overBudget`, and one stopped by the time limit has `timedOut`, see [budgets](authoring-requests.md#budgets). `messages` holds the step's model messages as a JSON
  array of `{ role, content }`, where `content` is a list of `text`, `toolCall` and `toolResult` parts.
  A `check` step has the `outcome` `passed`, `failed` or `unfinished`, see
  [checking](authoring-requests.md#states) and [checking time](authoring-requests.md#budgets), and its `checks` holds
  `{ compile, scenarios }`: each has an `outcome` of `passed` or `failed`, and `scenarios` can also be `notRun`. `compile` has its
  `diagnostics`, and `scenarios` has its `request` id, its `diagnostics` and its `items`, the scenario results of the run.
  A `review` step has the `outcome` `passed`, `findings` or `unfinished`, see [reviewing](authoring-requests.md#states) and
  [review calls](authoring-requests.md#budgets), and its `checks` holds `{ review: { outcome, findings } }`: the `outcome` is `passed` or
  `findings`, and `findings` is the text of the review, null when it passed. A review that is `unfinished` stores no `checks`.
  An `accept` step has the `outcome` `accepted`, `refused`, `conflict` or `notAccepted`, see
  [accepting by the agent](authoring-requests.md#states) and [accepting time](authoring-requests.md#budgets), and it stores
  no `checks`. A `conflict` clears the request's `draft_id` in the commit that moves it to `working`. The publisher
  keeps `claimed_by` and `claimed_until` when it moves a request to `accepted`, so the host's own commit of its `accept`
  step finds its claim, and a step never moves an `accepted` request.
