# Process tables

This page lists the tables that `Axis.Processes` owns, with what each column holds. The process
resource, its steps and its execution are in [processes](processes.md).

## Tables

`Axis.Processes` owns these tables in the `axis` schema of each tenant database, with their migration
history in `axis.__processes_migrations`, see [storage](storage.md). Times in every table use the
database clock.

### Process instances

`axis.process_instances` holds one row per instance, with its process, subject record, release, state
and revision, and `step`, the step it runs next.

- Its `created_at` is the start of the start transaction. Its `ended_at` is the time it became
  `completed`, `failed` or `cancelled`, and is null before. A retry clears it.
- A partial unique index on application, process and subject record, where the state is `running` or
  `waiting`, enforces one start per submission. The index treats a null subject as one value
  (`NULLS NOT DISTINCT`), so a process without a subject has one open instance.
- The subject columns are null for an instance of a process without a subject, and its `parameters`
  `jsonb` column holds the values it started with, see
  [process flow](process-flow.md#processes-without-a-subject).
- An instance that a sub-process step started holds the `parent_instance_id` and the `parent_step` of
  its parent, and both are null for any other instance. An index on `parent_instance_id` finds the
  children of a parent.
- An `accountable_user` `text` column, null for none, holds the instance's
  [accountable user](module-contracts.md#the-accountable-user): its starter at the start, then the
  user of each task completion, written in the completion's transaction. A `decision_task` `uuid`
  column, null before any task, holds the [deciding task](module-contracts.md#the-deciding-task),
  replaced with it. A deadline, an escalation, a retry and a cancel keep both. A schedule's instance
  starts with null, and a sub-process's with its parent's values. An instance that a data change
  trigger or an event trigger starts holds the accountable user of the write or event that fired it,
  and no deciding task.
- Three more indexes serve the inspector's instance list: one by application, process and start time,
  newest first, one by application, process, state and start time, and one by subject record.

### Step history

`axis.process_step_history` holds one row per step occurrence.

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID |
| `process_instance_id` | `uuid` | The instance |
| `step` | `text` | The declared name of the step |
| `revision` | `bigint` | The instance revision the step ran at |
| `input` | `jsonb` | `{ "subjectId", "subjectVersion" }`: the subject record and the version the step read. For a task completion, the version its values were written over, or `null` without values. It holds no field values |
| `output` | `jsonb`, null | What the step wrote, by step and event, as [History output](#history-output) lists. Null for a failed step |
| `decision` | `text`, null | The branch a decision took: its zero-based index, such as `0`, or `otherwise`. The outcome's declared name for a task completion, and `deadline` for a task its deadline expired. Null for other steps and failed steps |
| `error` | `text`, null | The error of a failed step. The exception type, or a fixed text that names the step, part, field pointer or message key. Never the exception message or a value, see [Step errors](process-inspection.md#step-errors-in-inspection) |
| `started_at` | `timestamptz` | The start of the step's transaction, `now()` |
| `finished_at` | `timestamptz` | When the row was written, `clock_timestamp()` |
| `trace_id` | `text`, null | The 32-hex trace id of the run that wrote the row. For a task completion through the server, it is the request's trace. Null when no span was current, such as a row written by the scenario runner, and for rows that existed before the column |

#### History output

The `output` column holds what each step or event wrote:

| Step or event | `output` |
| --- | --- |
| `decision` | `{ "next" }` |
| `updateRecord` operation | `{ "next", "subjectVersion" }`, with the version it wrote |
| `task` step | `{ "state": "waiting", "taskId" }`, with the task it created |
| Task completion | `{ "taskId", "outcome", "next" }`, plus `subjectVersion` when it wrote values |
| Task whose deadline expired it | `{ "taskId", "next" }` |
| Task escalation | `{ "escalation" }`, the escalation's index, plus `notificationId` when it notified, see [Escalations and deadline](task-deadlines.md#escalations-and-deadline) |
| `end` step | `{ "state": "completed" }` |
| `callConnector` or package operation step that staged its call | `{ "state": "waiting", "operationId" }` |
| Call that succeeds | `{ "operationId", "outcome", "next" }`, plus `subjectVersion` when it wrote output fields |
| Call that fails | `{ "operationId", "outcome": "failed" }` |
| Connector call left unknown with no lookup, and package call left unknown | `{ "operationId", "outcome": "unknown" }` |
| `raiseEvent` step | `{ "next", "eventId" }`, with the occurrence it stored |
| `timer` step | `{ "state": "waiting", "dueAt" }`, and `{ "next" }` when its timer resumes the instance |
| `waitEvent`, `subProcess` and `createRecord` steps | As [process flow](process-flow.md) says for each step |
| Operator cancel | `{ "command": "cancel" }` |
| Operator retry | `{ "command": "retry" }` |
| Call an operator resolved as succeeded | `{ "operationId", "outcome", "next", "resolved": true }`, plus `subjectVersion` when it wrote output fields |
| Call an operator resolved as failed | `{ "operationId", "outcome": "failed", "resolved": true }` |
| Failed step | Null |

### Work items

`axis.process_work_items` holds the ready work, which workers claim and run.

- Each item has an `id`, its `tenant_id`, its `kind`, its `due_at` time, and the `lease_expires_at`
  and `claim_token` of its current claim. Lease times use the database clock.
- A worker claims only items of the tenant it polls and only kinds it has a handler for. The claim
  compares `due_at` with the transaction time, which a test scenario's `axis.now` fixes, and a worker
  claims the earliest due item first.
- Each item also has a `seq`, its queue position. The database sets it when the item is queued, and it
  only grows and never changes. The test scenario runner claims the due item with the lowest `seq`,
  so a clock that steps back cannot reorder its work.
- Every item also has the `trace_id` and `span_id` of the span whose transaction queued it, or null
  when no span was current. A run starts a new trace with a link to that span, see
  [Telemetry](../architecture.md#telemetry). A rescheduled item keeps them, and activation that
  queues a `trigger.schedule` item again replaces them.
- Completed and failed items are deleted.
- The other columns belong to some kinds only: `process_instance_id`, `revision`, null for every
  kind but `process.timer`, `task_id`, a foreign key to the task, the nullable integer `escalation`,
  `connector_call_id`, `package_call_id`, `event_id`, `trigger_id`, `release_id`, `occurrence_at`,
  `cursor`, `email_id`, `webhook_delivery_id` and `entity_id`.

| Kind | Its own columns | What it does |
| --- | --- | --- |
| `process.step` | `process_instance_id` | Runs the instance's next step, see [Running a step](process-execution.md#running-a-step) |
| `process.timer` | `process_instance_id` and the `revision` its step left | Resumes one instance that waits on a `timer` step, or on a `waitEvent` step's timeout, only while the instance waits at that revision. Its `due_at` is the time of the timer. See [Timer step](process-flow.md#timer-step) |
| `process.resume` | `process_instance_id` of the child | Resumes the parent of a completed child, which the handler finds from the child's `parent_instance_id`. See [Sub-process step](process-flow.md#sub-process-step) |
| `connector.call` | `process_instance_id` and `connector_call_id` | Sends or reconciles one connector call. Its `due_at` gives the backoff. See [Connector calls](process-execution.md#connector-calls) |
| `package.call` | `process_instance_id` and `package_call_id` | Runs one package call. See [Package operations](process-execution.md#package-operations) |
| `event.deliver` | `event_id`, and `trigger_id` when an operator [redelivered](process-events.md#routes) a dead letter | Delivers one event occurrence to the `async` event triggers, or to the one trigger it names. See [Event delivery](process-events.md#event-delivery) |
| `trigger.schedule` | `trigger_id` | Runs the occurrences of one schedule trigger. Its `due_at` is the next occurrence, and a partial unique index allows one such item for each trigger. See [Schedules](process-events.md#schedules) |
| `trigger.fanOut` | `trigger_id`, `release_id` and `occurrence_at` | Starts the process for the records of one occurrence. `release_id` is the release that was active at the occurrence. Its `cursor` is the last record id it handled, and is null before its first batch |
| `task.deadline` | `task_id` and `process_instance_id` | Expires one task at its deadline. Its `due_at` is the task step's time plus the deadline's `after`. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline) |
| `task.escalate` | `task_id`, `process_instance_id` and `escalation`, the index in file order | Runs one escalation of one task. Its `due_at` is the step's time plus the escalation's `after` |
| `notification.email` | `email_id` | Sends one email of `axis.email_outbox` |
| `webhook.deliver` | `webhook_delivery_id`, without a foreign key | Sends one event occurrence to one webhook, from a row of `axis.webhook_deliveries`. See [Webhook delivery](process-events.md#webhook-delivery) |
| `computed.backfill` | `entity_id` | Recomputes the out-of-date rows of one entity, one batch of up to 500 rows per claim. See [Recomputing computed fields](migrations.md#recomputing-computed-fields) |
| `records.importCheck` | None: the import row names the item in its `work_item_id` | Dry-runs one uploaded record import. See [Import](import-export.md#import) |
| `records.importCommit` | None: the import row names the item in its `work_item_id` from the commit request until the commit ends | Commits one checked import. See [Import](import-export.md#import) |

### Receipts

`axis.process_start_receipts` holds the stored `201` response of each `Idempotency-Key` of a
[process start](processes.md#start-endpoint), unique per application, process and key. The subject is
null for a process without a subject.

`axis.endpoint_receipts` holds the record and the instance, null for an endpoint without a `process`,
of each endpoint call's `Idempotency-Key`, unique per application, endpoint, caller and key, with a
hash of the input. See [Idempotency](inbound-endpoints.md#idempotency).

### Tasks

`axis.process_tasks` holds one row per task. An index on application, state, assignee kind and
assignee finds the open tasks of a user id or a role name.

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
| `claimed_by` | `text`, null | The user who claimed the task. Null when unclaimed |
| `claimed_at` | `timestamptz`, null | When it was claimed |
| `created_at` | `timestamptz` | The start of the step's transaction, `now()` |

### Connector calls

`axis.connector_calls` holds one row per staged connector call, see
[Connector calls](process-execution.md#connector-calls). A unique index on (`process_instance_id`,
`step`, `staged_revision`) lets a step stage one call per pass of a [loop](process-flow.md#loops).
There is no column for a request or response body. Each call attempt row stores the `trace_id` of its
attempt, see [Telemetry](../architecture.md#telemetry).

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

### Call attempts

`axis.process_call_attempts` holds one row per attempt and per lookup of a connector or package call,
see [Attempts](process-inspection.md#attempts). The outcome transaction of an attempt or a lookup
writes its row. A call found still `sending` has no row for its lost attempt, so the next worker that
finds it writes that row, with the outcome `unknown`. A unique index on (`call_id`, `kind`, `number`)
keeps each number once. The call is in one of two tables, so `call_id` has no foreign key. There is no
column for a request or response body.

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

### Package calls

`axis.package_calls` holds one row per staged package call, see
[Package operations](process-execution.md#package-operations). A unique index on
(`process_instance_id`, `step`, `staged_revision`) lets a step stage one call per pass of a
[loop](process-flow.md#loops). There is no column for an output. Each call attempt row stores the
`trace_id` of its attempt, see [Telemetry](../architecture.md#telemetry).

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

### Events

`axis.events` and `axis.process_event_matches` hold the event occurrences and the uses of them by
`waitEvent` steps, see [Event tables](process-events.md#event-tables).

### Notifications

`axis.notifications` holds one row per in-app notification and recipient, which a `notify` step
stores, see [In-app notifications](notifications.md#in-app-notifications). The key is (`id`,
`recipient_kind`, `recipient`), so the rows of one step run share the `id`. An index on
(`application_id`, `recipient_kind`, `recipient`, `created_at`) finds a recipient's notifications. A
filtered index of the same columns without `application_id`,
`ix_notifications_tenant_recipient_created_at`, finds the tenant notices, which have no application.

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID, the notification's id. Every recipient's row has it |
| `application_id` | `uuid`, null | The application. Null for a tenant notice |
| `release_id` | `uuid`, null | The release the notifying instance is pinned to. It resolves the text keys and the page. Null for a tenant notice, whose text keys are platform texts |
| `recipient_kind` | `text` | `user` or `role`. A tenant notice uses `platformRole` |
| `recipient` | `text` | The user id or the role name. For `platformRole`, the platform role kind, `administrator` |
| `subject_key` | `text` | The text key of the subject |
| `body_key` | `text` | The text key of the body |
| `values` | `jsonb` | The raw evaluated placeholder values, keyed by the placeholders' names. Dates are ISO text, as in an event payload. They are formatted when read |
| `open_page` | `text`, null | The declared name of the page the notification opens. Null when the step has no `open` |
| `open_parameters` | `jsonb`, null | The raw evaluated page parameters, keyed by the parameters' declared names. Null when the step has no `open` |
| `created_at` | `timestamptz` | The start of the step's transaction, `now()` |

`axis.notification_reads` holds one row per notification and user who read it, see
[In-app notifications](notifications.md#in-app-notifications). It has no foreign key to
`axis.notifications`, because that table's key has three columns.

| Column | Type | Meaning |
| --- | --- | --- |
| `notification_id` | `uuid` | The notification's id |
| `user_id` | `text` | The user who read it |
| `read_at` | `timestamptz` | When the user read it, `now()` |

`axis.email_outbox` holds one row per email that a `notify` step with the `email` channel staged, see
[Email](notifications.md#email).

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID. The row's `notification.email` item names it in `email_id`, without a foreign key |
| `application_id`, `release_id`, `notification_id`, `step` | `uuid`, `uuid`, `uuid`, `text` | The application, the release the notifying instance is pinned to, which gave the texts, the step run's notification id, which all its emails share, and the name of the `notify` step |
| `address`, `locale` | `text`, `text` null | The address the email goes to, not validated when staged, and the locale of the texts. The locale is null when the release has none |
| `subject`, `body`, `message_id`, `values` | `text`, `text`, `text`, `jsonb` | The rendered subject, the plain text body with the page link if any, the `Message-ID` header, which a unique index keeps once, and the raw placeholder values of the step run as in `axis.notifications`, `{}` before the column existed |
| `state`, `created_at`, `sent_at` | `text`, `timestamptz`, `timestamptz` null | `pending` or `sent`, `pending` at first. The start of the step's transaction, `now()`. When the email was sent, null while it is pending |

### Webhook deliveries

`axis.webhook_deliveries` holds one row per event occurrence and webhook, which a `raiseEvent` step
staged for each webhook of the event, see [Webhook delivery](process-events.md#webhook-delivery). It
holds no body.

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID. The row's `webhook.deliver` item names it in `webhook_delivery_id`, without a foreign key |
| `application_id`, `release_id`, `webhook_id`, `event_id` | `uuid` | The application, the release the raising instance is pinned to, the webhook of that release and the occurrence in `axis.events`. A unique index keeps (`event_id`, `webhook_id`) once |
| `state`, `attempts`, `status_code` | `text`, `integer`, `integer` null | `pending` or `delivered`, `pending` at first. The attempts made, 0 at first. The status of the last attempt, null when none was made or the last got no answer |
| `created_at`, `delivered_at` | `timestamptz`, `timestamptz` null | The start of the step's transaction, `now()`. When a receiver answered `2xx`, null while the row is pending |

### Triggers

`axis.trigger_schedules` holds the schedule state, one row per schedule trigger of the tenant, see
[Schedules](process-events.md#schedules). Activation writes it.

| Column | Type | Meaning |
| --- | --- | --- |
| `trigger_id` | `uuid` | The trigger |
| `application_id` | `uuid` | The application. Activation uses it to find the triggers a new release removed |
| `release_id` | `uuid` | The release that wrote the state |
| `schedule` | `text`, null | The cron text the next occurrence was computed from. Null for a row written before it was stored |
| `time_zone` | `text`, null | The time zone id the next occurrence was computed in. Null for a row written before it was stored |
| `next_occurrence_at` | `timestamptz` | The next occurrence |
| `last_occurrence_at` | `timestamptz`, null | The last occurrence that ran. Null before the first |

`axis.trigger_starts` holds one receipt per trigger, cause and record, see
[Starts from triggers](process-events.md#starts-from-triggers). Its primary key on (`trigger_id`,
`source`, `subject_id`) makes each start happen at most once. Event delivery and schedule fan-out
write it.

| Column | Type | Meaning |
| --- | --- | --- |
| `trigger_id` | `uuid` | The trigger |
| `source` | `text` | The event id for an event trigger, or the occurrence time for a schedule trigger |
| `subject_id` | `uuid` | The record |
| `outcome` | `text` | `started`, `skipped` or `deadLettered` |
| `process_instance_id` | `uuid`, null | The instance it started. Null when skipped |
| `created_at` | `timestamptz` | The start of the transaction, `now()` |
