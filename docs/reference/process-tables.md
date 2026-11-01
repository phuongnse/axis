# Process tables

Detailed reference for the tables that `Axis.Processes` owns. The process
resource, its steps and its execution are in [processes.md](processes.md). The
conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

## Tables

`Axis.Processes` owns these tables in the `axis` schema of each tenant
database, with history in `axis.__processes_migrations` (see
[storage](storage.md)):

- `axis.process_instances`: one row per instance, with its process, subject
  record, release, state, revision and `step`, the step it runs next. Its
  `created_at` is the start of the start transaction. Its
  `ended_at` is the time it became `completed`, `failed` or `cancelled`,
  and is null before. A retry clears it.
  A partial unique index on
  application, process and subject record, where the state is `running` or
  `waiting`, enforces one start per submission. The index treats a null subject
  as one value (`NULLS NOT DISTINCT`), so a process without a subject has one
  open instance. The subject columns are null for such an instance, and its
  `parameters` `jsonb` column holds the values it started with, see
  [process flow](process-flow.md#processes-without-a-subject).
  An instance that a sub-process step started also holds the `parent_instance_id`
  and the `parent_step` of its parent, and both are null for any other
  instance. An index on `parent_instance_id` finds the children of a parent.
  An `accountable_user` `text` column, null for none, holds the instance's
  [accountable user](module-contracts.md#the-accountable-user): its starter at the start, then the
  user of each task completion, written in the completion's transaction. A `decision_task` `uuid`
  column, null before any task, holds the [deciding task](module-contracts.md#the-deciding-task),
  replaced with it. A deadline, an escalation, a retry and a cancel keep both. A schedule's instance
  starts with null, a sub-process's with its parent's values, and an instance that an event trigger
  starts holds null until events carry an accountable user _(planned for M24)_.
  Three more indexes serve the inspector's instance list: one by application,
  process and start time, newest first, one by application, process, state and
  start time, and one by subject record.
- `axis.process_step_history`: one row per step occurrence. Times use the
  database clock.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `process_instance_id` | `uuid` | The instance |
  | `step` | `text` | The declared name of the step |
  | `revision` | `bigint` | The instance revision the step ran at |
  | `input` | `jsonb` | `{ "subjectId", "subjectVersion" }`: the subject record and the version the step read. For a task completion, the version its values were written over, or `null` without values. It holds no field values |
  | `output` | `jsonb`, null | `{ "next" }` for a decision, `{ "next", "subjectVersion" }` for an operation, with the version it wrote, `{ "state": "waiting", "taskId" }` for a task step, with the task it created, `{ "taskId", "outcome", "next" }` for a task completion, plus `subjectVersion` when it wrote values, `{ "taskId", "next" }` for a task whose deadline expired it, `{ "state": "completed" }` for an end step, null for a failed step. `{ "state": "waiting", "operationId" }` for a `callConnector` or package operation step that staged its call. `{ "state": "waiting", "dueAt" }` for a `timer` step, and `{ "next" }` when its timer resumes the instance. `{ "operationId", "outcome", "next" }` when the call succeeds, plus `subjectVersion` when it wrote output fields. `{ "operationId", "outcome": "failed" }` when the call fails. `{ "operationId", "outcome": "unknown" }` for a connector call left unknown with no lookup, and for a package call left unknown. `{ "next", "eventId" }` for a `raiseEvent` step, with the occurrence it stored. `{ "command": "cancel" }` for an operator cancel. `{ "command": "retry" }` for an operator retry. `{ "operationId", "outcome", "next", "resolved": true }` for a call an operator resolved as succeeded, plus `subjectVersion` when it wrote output fields, and `{ "operationId", "outcome": "failed", "resolved": true }` for one resolved as failed |
  | `decision` | `text`, null | The branch a decision took: its zero-based index, such as `0`, or `otherwise`. The outcome's declared name for a task completion, and `deadline` for a task its deadline expired. Null for other steps and failed steps |
  | `error` | `text`, null | The error of a failed step. The exception type, or a fixed text that names the step, part, field pointer or message key. Never the exception message or a value, see [Step errors](process-inspection.md#step-errors-in-inspection) |
  | `started_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `finished_at` | `timestamptz` | When the row was written, `clock_timestamp()` |
  | `trace_id` | `text`, null | The 32-hex trace id of the run that wrote the row. For a task completion through the server, it is the request's trace. Null when no span was current, such as a row written by the scenario runner, and for rows that existed before the column |
- `axis.process_work_items`: the ready work. Each item has an `id`, its
  `tenant_id`, its `kind`, its `due_at` time, and the `lease_expires_at` and
  `claim_token` of its current claim. A step's item also has the
  `process_instance_id` of its instance. A `process.timer` item also has the
  `revision` of the instance that its timer step left, null for other kinds.
  The item resumes the instance only while it waits at that revision. A worker claims only items of the
  tenant it polls and only kinds it has a handler for. The claim compares
  `due_at` with the transaction time, which a test scenario's `axis.now` fixes.
  A worker claims the earliest due item first. Each item also has a `seq`, its
  queue position. The database sets it when the item is queued, it only grows
  and never changes. The test scenario runner claims the due item with the
  lowest `seq`, so a clock that steps back cannot reorder its work.
  Lease times use the database clock. Completed and failed items are deleted. A `connector.call`
  item sends or reconciles one connector call. It has the
  `process_instance_id` of the call's instance and the `connector_call_id` of
  the call. See [Connector calls](process-execution.md#connector-calls). The item's `due_at`
  gives the backoff. A `package.call` item runs one package call. It has the
  `process_instance_id` of the call's instance and the `package_call_id` of
  the call. See [Package operations](process-execution.md#package-operations). An `event.deliver` item delivers one event occurrence to
  the `async` event triggers, see [Event delivery](process-events.md#event-delivery). It has
  the `event_id` of the occurrence, and the `trigger_id` of the one trigger it delivers
  to when an operator [redelivered](process-events.md#routes) a dead letter. Every item also has the `trace_id` and
  `span_id` of the span whose transaction queued it, or null when no span was
  current. A run starts a new trace with a link to that span, see
  [Telemetry](../architecture.md#telemetry). A rescheduled item keeps them.
  Activation that queues a `trigger.schedule` item again replaces them.
  A `process.timer` item resumes one instance that waits on a `timer` step. It
  has the `process_instance_id` of the instance and the `revision` the timer
  step left, and its `due_at` is the time of the timer. See
  [Timer step](process-flow.md#timer-step).
  Three more kinds come with [schedules](process-events.md#schedules), one with
  [sub-processes](process-flow.md#sub-process-step), one with
  [task deadlines](task-deadlines.md), and one with
  [computed fields](migrations.md#recomputing-computed-fields):
  - `trigger.schedule` runs the occurrences of one schedule trigger. Its item
    also has the `trigger_id`, and its `due_at` is the next occurrence. A
    partial unique index allows one such item for each trigger.
  - `trigger.fanOut` starts the process for the records of one occurrence.
    Its item also has the `trigger_id`, the `release_id` that was active at
    the occurrence and the `occurrence_at` time. Its `cursor` is the last
    record id it handled, and is null before its first batch.
  - `process.resume` resumes the parent of a completed child. Its item also
    has the `process_instance_id` of the child, and the handler finds the
    parent from the child's `parent_instance_id`. See
    [Sub-process step](process-flow.md#sub-process-step).
  - `task.deadline` expires one task at its deadline. Its item also has the
    `task_id` of the task and the `process_instance_id` of its instance, and
    its `due_at` is the task step's time plus the deadline's `after`. See
    [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
  - `task.escalate` runs one escalation of one task. Its item also has the
    `task_id`, the `process_instance_id` and the `escalation` index in file
    order. Its `due_at` is the step's time plus the escalation's `after`. See
    [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
  - `notification.email` sends one email of `axis.email_outbox`. Its item also
    has the `email_id` of the row.
  - `webhook.deliver` sends one event occurrence to one webhook, from a row of
    `axis.webhook_deliveries`. Its item also has the `webhook_delivery_id` of the
    row, without a foreign key. See
    [Webhook delivery](process-events.md#webhook-delivery).
  - `computed.backfill` recomputes the out-of-date rows of
    one entity. Its item also has the `entity_id`, and each claim runs one
    batch of up to 500 rows. See
    [Recomputing computed fields](migrations.md#recomputing-computed-fields).
  - `records.importCheck` dry-runs one uploaded record import. It has no
    other id column: the import row names the item in its `work_item_id`. See
    [Import](import-export.md#import).
  - `records.importCommit` commits one checked import. It has no other id
    column either: the import row names the item in its `work_item_id` from
    the commit request until the commit ends. See
    [Import](import-export.md#import).
- `axis.process_start_receipts`: the stored `201` response of each
  `Idempotency-Key`, unique per application, process and key. The subject is
  null for a process without a subject.
- `axis.endpoint_receipts`: the record and instance (null for an endpoint without a `process`) of
  each endpoint call's `Idempotency-Key`, unique per application, endpoint, caller and key, with a
  hash of the input. See [Idempotency](inbound-endpoints.md#idempotency).
- `axis.process_tasks`: one row per task. Times use the database clock. An
  index on application, state, assignee kind and assignee finds the open tasks
  of a user id or a role name.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `process_instance_id` | `uuid` | The instance |
  | `process_id` | `uuid` | The process |
  | `release_id` | `uuid` | The release the instance is pinned to |
  | `application_id` | `uuid` | The application |
  | `step` | `text` | The declared name of the task step |
  | `subject_entity_id` | `uuid` | The subject entity |
  | `subject_id` | `uuid` | The subject record |
  | `assignee_kind` | `text` | `user` or `role` |
  | `assignee` | `text` | The user id that the `user` expression gave, or the role name |
  | `form_id` | `uuid` | The id of the step's form |
  | `due_at` | `timestamptz`, null | `created_at` plus `dueIn`, or null when the step has no `dueIn` |
  | `state` | `text` | `open`, `completed`, `cancelled` or `expired` |
  | `outcome` | `text`, null | The outcome the task was completed with. Null while it is `open`, and for a `cancelled` or `expired` task |
  | `completed_by` | `text`, null | The user who completed the task. Null while it is `open`, and for a `cancelled` or `expired` task |
  | `completed_at` | `timestamptz`, null | When the task was completed. Null while it is `open`, and for a `cancelled` or `expired` task |
  | `expired_at` | `timestamptz`, null | When the deadline expired the task. Null unless the task is `expired`. The task API does not return it |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
- `axis.connector_calls`: one row per staged connector
  call, see [Connector calls](process-execution.md#connector-calls). A unique index on
  (`process_instance_id`, `step`, `staged_revision`) lets a step stage one call
  per pass of a [loop](process-flow.md#loops). Times use
  the database clock. There is no column for a request or response body.
  Each call attempt row stores the `trace_id` of its attempt, see
  [Telemetry](../architecture.md#telemetry).

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID, the call's operation identity. Every attempt and every lookup sends it as the idempotency key |
  | `process_instance_id` | `uuid` | The instance |
  | `step` | `text` | The declared name of the `callConnector` step |
  | `staged_revision` | `bigint` | The instance revision the step ran at. A retry restage sets it to the new run's revision |
  | `release_id` | `uuid` | The release the instance is pinned to |
  | `connector` | `text` | The connector's name |
  | `operation` | `text` | The operation's name |
  | `state` | `text` | `pending`, `sending`, `unknown`, `succeeded`, `failed` or `cancelled` |
  | `attempts` | `integer` | The attempts made so far, the first one included. An operator retry resets it to 0 |
  | `lookups` | `integer` | The failed lookups since the call last became `unknown`. It sets the delay before the next lookup. Reset to 0 when the call goes back to `pending` |
  | `input` | `jsonb`, null | The evaluated input, sent again by every retry. Kept after the call settles, but cleared when the call is cancelled. Never copied to history, audit records or logs, and shown only through [inspection](process-inspection.md#values-and-masking) |
  | `last_status` | `integer`, null | The HTTP status of the last attempt or lookup. Null when none answered |
  | `last_error` | `text`, null | The error of the last attempt or lookup, such as a timeout. It holds no body, secret, exception message or value |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `settled_at` | `timestamptz`, null | When the call became `succeeded` or `failed`. Null before |
- `axis.process_call_attempts`: one row per attempt and per
  lookup of a connector or package call, see
  [Attempts](process-inspection.md#attempts). Times use the database clock. The outcome transaction
  of an attempt or a lookup writes its row. A call found still `sending` has
  no row for its lost attempt, so the next worker that finds it writes that
  row, with the outcome `unknown`. A unique index on (`call_id`, `kind`,
  `number`) keeps each number once. The call is in one of two tables, so
  `call_id` has no foreign key. There is no column for a request or
  response body.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `call_kind` | `text` | `connector` or `package` |
  | `call_id` | `uuid` | The call, in `axis.connector_calls` or `axis.package_calls` |
  | `kind` | `text` | `attempt` or `lookup` |
  | `number` | `integer` | The number of the attempt or lookup, counted per `kind`, starting at 1. It carries on across an operator retry |
  | `started_at` | `timestamptz` | When the attempt or lookup started |
  | `finished_at` | `timestamptz` | When its outcome was recorded |
  | `outcome` | `text` | For an attempt: `succeeded`, `retryable`, `unknown` or `failed`. For a lookup: `found`, `notFound` or `failed` |
  | `http_status` | `integer`, null | The HTTP status, when one answered. Null for a package call and when none answered |
  | `error` | `text`, null | A fixed text, such as a timeout. It holds no body, no exception message and no secret |
  | `trace_id` | `text`, null | The 32-hex trace id of the run that made the attempt or lookup. Null when no span was current, and for rows that existed before the column |
- `axis.package_calls`: one row per staged package call, see
  [Package operations](process-execution.md#package-operations). A unique index on
  (`process_instance_id`, `step`, `staged_revision`) lets a step stage one call
  per pass of a [loop](process-flow.md#loops). Times use
  the database clock. There is no column for an output.
  Each call attempt row stores the `trace_id` of its attempt, see
  [Telemetry](../architecture.md#telemetry).

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID, the call's operation identity. It is the `OperationId` of the operation's context |
  | `process_instance_id` | `uuid` | The instance |
  | `step` | `text` | The declared name of the package operation step |
  | `staged_revision` | `bigint` | The instance revision the step ran at. A retry restage sets it to the new run's revision |
  | `release_id` | `uuid` | The release the instance is pinned to |
  | `package` | `text` | The package id |
  | `version` | `text` | The package version the release pins |
  | `operation` | `text` | The operation's name |
  | `state` | `text` | `pending`, `sending`, `unknown`, `succeeded`, `failed` or `cancelled` |
  | `attempts` | `integer` | The attempts made so far, the first one included. An operator retry resets it to 0 |
  | `retry_attempts` | `integer` | The attempts the step's retry policy allows, stored when the call is staged |
  | `retry_backoff` | `interval` | The step's backoff, stored when the call is staged |
  | `idempotent` | `boolean` | Whether the operation is `idempotent`, stored when the call is staged |
  | `input` | `jsonb`, null | The evaluated input. Kept while the call is `unknown`, and cleared when it settles or is cancelled. Never copied to history, audit records or logs |
  | `last_error` | `text`, null | The error of the last attempt: an exception type or a fixed sentence. It holds no exception message and no value |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `settled_at` | `timestamptz`, null | When the call became `succeeded` or `failed`. Null before |
- `axis.events` and `axis.process_event_matches`: the event occurrences and the
  uses of them by `waitEvent` steps, see [Event tables](process-events.md#event-tables).
- `axis.notifications`: one row per in-app notification and recipient, which a
  `notify` step stores, see
  [In-app notifications](notifications.md#in-app-notifications). The key is
  (`id`, `recipient_kind`, `recipient`), so the rows of one step run share the
  `id`. Times use the database clock. An index on (`application_id`,
  `recipient_kind`, `recipient`, `created_at`) finds a recipient's
  notifications.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID, the notification's id. Every recipient's row has it |
  | `application_id` | `uuid` | The application |
  | `release_id` | `uuid` | The release the notifying instance is pinned to. It resolves the text keys and the page |
  | `recipient_kind` | `text` | `user` or `role` |
  | `recipient` | `text` | The user id or the role name |
  | `subject_key` | `text` | The text key of the subject |
  | `body_key` | `text` | The text key of the body |
  | `values` | `jsonb` | The raw evaluated placeholder values, keyed by the placeholders' names. Dates are ISO text, as in an event payload. They are formatted when read |
  | `open_page` | `text`, null | The declared name of the page the notification opens. Null when the step has no `open` |
  | `open_parameters` | `jsonb`, null | The raw evaluated page parameters, keyed by the parameters' declared names. Null when the step has no `open` |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
- `axis.notification_reads`: one row per notification and user who read it,
  see [In-app notifications](notifications.md#in-app-notifications). It has no
  foreign key to `axis.notifications`, because that table's key has three
  columns. Times use the database clock.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `notification_id` | `uuid` | The notification's id |
  | `user_id` | `text` | The user who read it |
  | `read_at` | `timestamptz` | When the user read it, `now()` |
- `axis.email_outbox`: one row per email that a `notify` step with the `email`
  channel staged, see [Email](notifications.md#email). Times use the database clock.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID. The row's `notification.email` item names it in `email_id`, without a foreign key |
  | `application_id`, `release_id`, `notification_id`, `step` | `uuid`, `uuid`, `uuid`, `text` | The application, the release the notifying instance is pinned to, which gave the texts, the step run's notification id, which all its emails share, and the name of the `notify` step |
  | `address`, `locale` | `text`, `text` null | The address the email goes to, not validated when staged, and the locale of the texts. The locale is null when the release has none |
  | `subject`, `body`, `message_id`, `values` | `text`, `text`, `text`, `jsonb` | The rendered subject, the plain text body with the page link if any, the `Message-ID` header, which a unique index keeps once, and the raw placeholder values of the step run as in `axis.notifications`, `{}` before the column existed |
  | `state`, `created_at`, `sent_at` | `text`, `timestamptz`, `timestamptz` null | `pending` or `sent`, `pending` at first. The start of the step's transaction, `now()`. When the email was sent, null while it is pending |
- `axis.webhook_deliveries`: one row per event occurrence and webhook, which a
  `raiseEvent` step staged for each webhook of the event, see
  [Webhook delivery](process-events.md#webhook-delivery). It holds no body. Times
  use the database clock.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID. The row's `webhook.deliver` item names it in `webhook_delivery_id`, without a foreign key |
  | `application_id`, `release_id`, `webhook_id`, `event_id` | `uuid` | The application, the release the raising instance is pinned to, the webhook of that release and the occurrence in `axis.events`. A unique index keeps (`event_id`, `webhook_id`) once |
  | `state`, `attempts`, `status_code` | `text`, `integer`, `integer` null | `pending` or `delivered`, `pending` at first. The attempts made, 0 at first. The status of the last attempt, null when none was made or the last got no answer |
  | `created_at`, `delivered_at` | `timestamptz`, `timestamptz` null | The start of the step's transaction, `now()`. When a receiver answered `2xx`, null while the row is pending |
- `axis.trigger_schedules`: the schedule state, one row per
  schedule trigger of the tenant, see [Schedules](process-events.md#schedules). Activation
  writes it.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `trigger_id` | `uuid` | The trigger |
  | `application_id` | `uuid` | The application. Activation uses it to find the triggers a new release removed |
  | `release_id` | `uuid` | The release that wrote the state |
  | `schedule` | `text`, null | The cron text the next occurrence was computed from. Null for a row written before it was stored |
  | `time_zone` | `text`, null | The time zone id the next occurrence was computed in. Null for a row written before it was stored |
  | `next_occurrence_at` | `timestamptz` | The next occurrence |
  | `last_occurrence_at` | `timestamptz`, null | The last occurrence that ran. Null before the first |
- `axis.trigger_starts`: one receipt per trigger, cause and record, see
  [Starts from triggers](process-events.md#starts-from-triggers). Its primary key on
  (`trigger_id`, `source`, `subject_id`) makes each start happen at most once.
  Event delivery and schedule fan-out write it.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `trigger_id` | `uuid` | The trigger |
  | `source` | `text` | The event id for an event trigger, or the occurrence time for a schedule trigger |
  | `subject_id` | `uuid` | The record |
  | `outcome` | `text` | `started`, `skipped` or `deadLettered` |
  | `process_instance_id` | `uuid`, null | The instance it started. Null when skipped |
  | `created_at` | `timestamptz` | The start of the transaction, `now()` |
