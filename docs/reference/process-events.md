# Process events and triggers

Detailed reference for the `raiseEvent` step, starts from triggers, event delivery and
schedules. The `event` and `trigger` resources are in
[integration-resources.md](integration-resources.md#events-and-triggers).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows
[D26](../decisions/d26-events-and-triggers.md). The
[event resource](integration-resources.md#event-resource) and its compile checks are
built. So is the `raiseEvent` step, which stores each occurrence, see
[Raise event step](#raise-event-step). The event
[trigger resource](integration-resources.md#trigger-resource) and its compile checks
are built too, and so is the delivery of each occurrence to the `async` event
triggers, see [Event delivery](#event-delivery). So is the start of each `sync`
event trigger in the transaction of the `raiseEvent` step, see
[Raise event step](#raise-event-step). Data change triggers fire on record API
creates and updates, on an inbound endpoint's record create, on task form
saves, on `updateRecord` and `createRecord` steps, on connector outputs and on
package operation outputs, see [Starts from triggers](#starts-from-triggers). The worker runs
schedule triggers too, once for each occurrence and for each record of the
trigger's data source, or once for each occurrence when the trigger has no data
source, see [Schedules](#schedules) and [Scheduled jobs](#scheduled-jobs).
A `raiseEvent` step raises an
[event](integration-resources.md#event-resource) for its subject record. A
[trigger](integration-resources.md#trigger-resource) starts a process for one record
when an event is raised, when a record is created or updated, or when a
schedule is due. Every start happens at most once for the
same cause, even after a worker crash.

Event delivery also wakes the instances that wait for an
event, which is built ([D69](../decisions/d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md)).

A delete starts a process without a subject through a delete trigger
([D70](../decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md)).
See [Delete triggers](#delete-triggers).

Deliveries to `async` triggers and schedule fan-outs that keep failing back off,
stop after a limit and become dead letters, and so do failed emails
([D72](../decisions/d72-a-delivery-limit-and-dead-letters.md)). See
[Dead letters](#dead-letters).

## Raise event step

`NoteClose` in the E2E fixture can announce that a note was closed. `close`
names the new step as its `next`:

```json
{
  "name": "close",
  "type": "operation",
  "operation": "updateRecord",
  "set": { "status": "'closed'" },
  "next": "announceClosed"
},
{
  "name": "announceClosed",
  "type": "operation",
  "operation": "raiseEvent",
  "event": "NoteClosed",
  "fields": { "priority": "priority", "amount": "amount" },
  "next": "end"
}
```

The fixture files do not change until the build issues add events.

- `event` names the event to raise.
- `fields` maps payload fields to expressions over the subject record.
- `next` names the step that follows.

The compiler reports these as compile errors.

- `event` names a loaded event, ignoring letter case (`AXC0125` at
  `/steps/{i}/event`). A name whose event file was not loaded because of its
  own errors is not reported again. The event's entity is the subject entity
  (`AXC0126` at `/steps/{i}/event`). That is not checked when either entity
  is unknown.
- With a known event, each key of `fields` names a payload field of the
  event, ignoring letter case, that no earlier key of the step names
  (`AXC0127` at `/steps/{i}/fields/{key}`). Each payload field of the event
  has a key (`AXC0128` at `/steps/{i}/fields`, once for each missing field).
  A field with no key is an error, not an implicit `null`.
- Each expression fits its payload field's type, with the same scope as a
  decision's `when`. A problem is reported at `/steps/{i}/fields/{key}` with
  its expression code, such as `AXC0048` for a value of the wrong type.
- A `raiseEvent` step has `event`, `fields` and `next`, and no `set`,
  `connector`, `input`, `output` or `retry` (`AXC0004`).

A compiled step holds the event's declared name, and its payload in the
event's declaration order.

**The step's transaction** runs as other steps do, see
[Running a step](process-execution.md#running-a-step), and writes these together:

1. It evaluates each `fields` expression on the subject record. Dates and
   date-times take the text forms of the
   [record API](record-api.md#record-shape). A run-time error, such as a
   division by zero, fails the step, and no occurrence is stored.
2. It stores the occurrence in `axis.events`, with the subject record, the
   payload and the release the instance is pinned to. The occurrence is kept
   even when no trigger listens, so a later wait for event step can match
   early events. A step that finds the instance at another revision stores
   none, so a step run again after a lost lease stores one occurrence.
3. It runs the start of each `sync` event trigger for the event, in the
   order of the release's triggers, see
   [Starts from triggers](#starts-from-triggers). The triggers come from the
   application's active release, not from the release the instance is pinned
   to, and each new instance is pinned to the active release. With no active
   release, or when it does not declare the event, no `sync` trigger runs. A
   skipped start lets the step commit. Any other start error fails the step,
   so the occurrence, the new instances and the step's other writes roll back.
   A `sync` start writes no receipt to `axis.trigger_starts`, because the
   step's revision check already raises the event once.
4. It queues one `event.deliver` work item for the occurrence, even when no
   `async` trigger listens yet. See [Event delivery](#event-delivery).
5. It writes the step's history row, with output `{ "next", "eventId" }`.
6. It writes `process.stepCompleted`, as other operations do. Raising an
   event writes no audit record of its own.
7. It inserts the work item of the next step.

## Starts from triggers

A start from a trigger follows the [start endpoint](processes.md#start-endpoint)'s
transaction, steps 2 to 6. It runs as `system`, so no policy is checked. The
user whose write fired a trigger needs no `start` grant.

1. It locks the subject record and reads it. A record that no longer exists
   skips the start.
2. It skips the start when a `running` or `waiting` instance of the process
   exists for the record. This is no error, and it is the guard against
   loops. The instance insert relies on the partial unique index of
   `axis.process_instances`, so a concurrent start is skipped too, and never
   fails the transaction.
3. It evaluates the process's `startCondition`. A `false`, a `null` or a
   run-time error skips the start.
4. It inserts the instance as `running` at revision 1, pinned to the release
   active at the start, as every start does.
5. It inserts the instance's first work item.
6. It writes the audit record `process.started`, with the actor `system`. Its
   details are `{ "processId", "releaseId", "trigger" }`, where `trigger` is
   the trigger's name. A start from an event adds `"eventId"`. A start from a
   schedule adds `"occurrenceAt"`, the occurrence time in UTC, such as
   `"2026-01-01T05:00:00Z"`.

A trigger's `when` is evaluated before these steps. A `false`, a `null` or a
run-time error there skips the start too. A skip never fails the write or
the step that fired the trigger, which still commits. For a `sync` event
trigger, any other error in the start fails the `raiseEvent` step, which rolls
back with its event, and the raising instance becomes `failed`.

Every skip for a `when`, a `startCondition`, a receipt or a process that
already runs is logged at Information level with the trigger, the record and
the reason. A run-time error is logged with its kind, such as
`DivisionByZero`, never its message, which can quote a value. A data change
trigger that does not list the write in `on`, or whose `fields` an update did
not change, is passed over without a log line, because it was never a
candidate. A skip is never tried again, so a process never starts long after
its cause.

Triggers come from the release that is active when the trigger fires. For an
`async` event trigger, that is when the event is delivered, not when it was
raised. For a data change trigger on a record API or inbound endpoint write,
it is the release the write's request resolved when it began. The write and
its triggers then use the same model, even when a release activates while the
request runs. A system-side write, which is a task form save, an
`updateRecord` step, a `createRecord` step, a connector output or a package
operation output, takes
its triggers from the active release at the time of the write, while the write
itself uses the release the task or instance is pinned to. When the two
differ, the record is read again with the active release's model before the
triggers run, so a `when` and a start condition see the field types they were
compiled against. A start condition, and the `when` of a data change trigger,
read the record with any [pending computed
field](migrations.md#recomputing-computed-fields) computed by that release, and
store nothing. An event trigger's `when` reads the event's payload, not the
record.

| Trigger | Start commits in | Skipped when |
| --- | --- | --- |
| Event, `async` | The `event.deliver` transaction, one for each occurrence and trigger | The trigger already handled the occurrence, the `when` is not true, the record no longer exists, the process already runs for the record, or the `startCondition` is not true |
| Event, `sync` | The transaction of the `raiseEvent` step, so both commit or neither does | The `when` is not true, the record no longer exists, the process already runs for the record, or the `startCondition` is not true |
| Data change | The write's transaction: a record API create or update, an inbound endpoint's create, a task form save, an `updateRecord` step, a `createRecord` step, a connector output or a package operation output. For a delete trigger, the transaction of a record API or Studio scenario delete, after `record.deleted` | The write is not in `on`, an update changed none of the `fields`, the `when` is not true, the process already runs for the record, or the `startCondition` is not true. For a delete trigger: the `when` is not true or fails, a parameter fails or is `null`, the process already runs, or the `startCondition` is not true |
| Schedule | The `trigger.fanOut` transaction of one batch, which holds the starts of up to 100 records. Without a data source, the one start of the occurrence | The record was already handled for the occurrence, the record no longer exists, the process already runs for the record, or the `startCondition` is not true. Without a data source: the occurrence was already handled, the process is already running, or the `startCondition` is not true |

A `when` or a `startCondition` is not true when it gives `false` or `null`,
or hits a run-time error.

A data change trigger sees the record as the write left it. A create always
passes `fields`, because every field is new. An update passes `fields` when
the stored value of a listed field differs from the value before the update.
A value that is only computed on the spot for a pending field is not a change
by the write.
A child collection in `fields` counts as changed whenever the update body
sets it, because an update replaces all its rows. The triggers run in the
order of the release, after the record and its `record.created` or
`record.updated` audit record are written and before the commit. For an
inbound endpoint call, they run before the endpoint's own start. For an
`updateRecord` step, a connector output or a package operation output, they run
after its `record.updated` audit record. For a `createRecord` step, they run
after its `record.created` audit record. For a task form save, they run after
the `task.completed` audit record, once the task is closed and its instance
resumes, so the instance of the task's own process is already running. A start that
fails for any reason but a skip, such as a database error, fails the write,
which rolls back with everything the triggers started. The record is locked
by the write, so a data change start does not lock it again. Suppose `NoteClose` had a data
change trigger on `Note` updates. Its own `close` step would fire it, and the
start would be skipped, because `NoteClose` already runs for the note.

## Event delivery

An `event.deliver` work item delivers one occurrence to the `async` event
triggers of the active release:

1. The worker takes the record's event lock, then reads the triggers for the
   event's name, ignoring letter case, and the instances that wait for the event.
   With no trigger, or when the application has no active release, and no
   waiting instance, the item is deleted with no other write. An item that
   names a trigger, one queued by a [redeliver](#dead-letters), keeps only that
   trigger and wakes no instance. When the active release
   does not have it as an `async` trigger on the event, the item is deleted
   with no other write, and the worker logs a warning. An item that names an
   instance, queued by the redeliver of a wake, wakes only that instance and
   delivers to no trigger. It is deleted with no other write when the instance
   no longer waits for the event.
2. For each trigger, one transaction does this:
   1. It checks the item's claim and extends its lease.
   2. It inserts a receipt into `axis.trigger_starts`, keyed by the trigger,
      the event id and the subject record, with the outcome `skipped`. When
      that key is taken, the trigger already handled the occurrence and is
      skipped.
   3. It evaluates the trigger's `when` on the stored payload, then runs the
      [start](#starts-from-triggers). The payload is read with the field types
      of the event in the active release. A value that no longer fits its
      type counts as a run-time error.
   4. It sets the receipt's outcome to `started`, with the instance, when the
      start happened.
3. The item is deleted.

A worker that crashes before a commit leaves no receipt for that trigger. The
next worker claims the item after its lease expires and runs it once. The
receipts of the triggers that committed make it skip them, even when the
instance they started has already ended.

A failure while the worker starts one trigger's process does not stop the item.
A separate transaction counts it for that trigger and the event, the other
triggers of the occurrence are still delivered in the same pass, and the item is
then due again after a backoff instead of after its lease. After
`Worker:DeliveryAttempts` failures the trigger gets a dead letter and a
`deadLettered` receipt, and delivery carries on without it. See
[Dead letters](#dead-letters).

A transaction that fails for another reason, the first read or the last
completion, leaves the item claimed until its lease expires, and the delivery is
then tried again. These failures are not counted.

**Waking waiting instances.** Step 1 takes the record's event lock, the same
advisory lock a `waitEvent` step takes before it looks for an occurrence, and
lists the instances that wait in a `waitEvent` step for this event on the
occurrence's record. An instance counts only when it was created at or before
the occurrence's `raised_at`. An item that names a trigger wakes no instance.
After the triggers, the item delivers the occurrence to each of these
instances. Each instance gets its own transaction, which checks the item's
claim, then matches and resumes the instance as
[Wait for event step](process-flow.md#wait-for-event-step) describes, on the
release the instance is pinned to. The unique key of `axis.process_event_matches`
makes a repeat after a crash do nothing. Every occurrence already gets an
`event.deliver` item, so an occurrence with no trigger, or in an application
with no active release, still wakes its waiting instances. Step 1's early
delete then happens only when there is no trigger and no waiting instance. A
failed wake is counted for the instance and the occurrence, as a failed trigger
is: the other instances and triggers of the pass are still delivered, and the
item is then due again after the backoff. After `Worker:DeliveryAttempts`
failures the wake becomes a dead letter, see [Dead letters](#dead-letters). A
later pass of the item skips an instance that has such a dead letter for the
event. Delivering 100 occurrences
that each wake one waiting instance, and running the steps they wake, finishes
within 30 s, and `EventWakePerformanceTests` fails past that. The lookup for
waiting instances reaches the instances of a record by an index and never reads
the whole instance table.

## Event tables

`axis.events` holds one row per event occurrence, see
[Raise event step](#raise-event-step). Rows are append-only and kept, even
when no trigger listens. An index on `process_instance_id` finds the
events an instance raised, and one on (`subject_id`, `raised_at`) finds the
events about a record for a `waitEvent` step.

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID, the event's id |
| `application_id` | `uuid` | The application |
| `event` | `text` | The event's name |
| `release_id` | `uuid` | The release the raising instance is pinned to |
| `entity_id` | `uuid` | The event's entity |
| `subject_id` | `uuid` | The record the occurrence names |
| `payload` | `jsonb` | The evaluated payload fields |
| `process_instance_id` | `uuid` | The instance that raised the event |
| `raised_at` | `timestamptz` | The start of the step's transaction, `now()` |

`axis.process_event_matches` holds one row for each use of an event occurrence by
a `waitEvent` step, see
[Wait for event step](process-flow.md#wait-for-event-step). The key is
(`process_instance_id`, `event_id`), so an instance uses an occurrence at
most once. An index on `event_id` finds the instances that used an
occurrence.

| Column | Type | Meaning |
| --- | --- | --- |
| `process_instance_id` | `uuid` | The instance that used the occurrence |
| `event_id` | `uuid` | The occurrence, a row of `axis.events` |
| `step` | `text` | The `waitEvent` step that used it |
| `matched_at` | `timestamptz` | The start of the step's transaction, `now()` |

## Schedules

**Schedule state.** Activating a release writes the schedule state of each
schedule trigger for the tenant, in `axis.trigger_schedules`, and its
`trigger.schedule` work item. The state keeps the `schedule` and `timeZone`
the next occurrence was computed from. Activation computes the next occurrence
from the activation time only for a new trigger, or one whose `schedule` or
`timeZone` changed. A trigger with the same id, `schedule` and `timeZone` keeps
its stored next occurrence, even a past one, and its work item is due then. So
after a server restart the worker catches up, as the occurrence steps below
describe. A trigger's data source, parameters and
process are read when the occurrence runs, so a change only to them keeps the
next occurrence. Triggers that the release removes lose their state and their
work item. The server's startup activation writes this state right after
the release is active.

**Occurrences.** Each schedule trigger has one `trigger.schedule` work item,
due at its next occurrence. Workers claim it with `FOR UPDATE SKIP LOCKED`,
so only one worker runs each occurrence per tenant. The claim's transaction
does this:

1. It records the occurrence as the trigger's last occurrence. It changes the
   state only while the stored next occurrence is still the one this item
   ran. When several occurrences were missed, for example while no worker ran,
   only the latest one at or before now runs, once. The worker logs how many
   it skipped.
2. It queues a `trigger.fanOut` work item for the occurrence.
3. It computes the next occurrence after now, and sets the item's `due_at` to
   it.

**Daylight saving.** Occurrences follow the trigger's `timeZone`. A local
time that does not exist runs at the next valid local time. A local time that
happens twice runs once, at the first. For example, in `Europe/Berlin`,
`30 2 * * *` runs at 03:00 on the day clocks go forward, and once at the
first 02:30 on the day they go back.

**Fan-out.** A `trigger.fanOut` item starts the process for the records of
the data source:

1. It reads the trigger and its data source from the release that was active
   at the occurrence.
2. It runs the data source as `system`, with the trigger's fixed parameters,
   ordered by record id. The data source's `pageSize` and `sort` do not apply.
   Each claim reads one batch of 100 records, by record id after the item's
   stored cursor. A record created while the fan-out runs is included only if
   its id sorts after the cursor.
3. For each record, it inserts a receipt into `axis.trigger_starts`, keyed by
   the trigger, the occurrence time and the record. The time is in UTC, such
   as `2026-01-01T05:00:00Z`. When that key is taken, the record is skipped.
   Otherwise it runs the [start](#starts-from-triggers). The process, the
   record and the release the instance is pinned to come from the release that
   is active when the fan-out runs. When the application has no active release,
   or the active release has no such process, the occurrence starts nothing
   and the reason is logged.
4. The transaction commits the batch's starts and receipts together with the
   item. A full batch of 100 stores the last record id as the item's `cursor`
   and makes the item due again at once. A shorter batch deletes the item.

A trigger without a data source is a [scheduled job](#scheduled-jobs). Its
fan-out runs one start in one transaction, with no data source, cursor or
batches. The receipt uses the nil UUID as the record, and the start passes the
trigger's parameter values.

**Failures.** A fan-out transaction that fails rolls the batch back. A
separate transaction counts the failure for the trigger and the occurrence, and
the item is due again after the [backoff](#dead-letters). After
`Worker:DeliveryAttempts` failures the occurrence becomes a `trigger.fanOut`
dead letter and the item is deleted. The receipts make each record start at most
once per occurrence. A data error in the data
source's filter, an SQLSTATE of class 22, is different. It is logged as a
warning and the item is deleted, because trying again would fail the same way.
A fan-out whose data source reads a
[pending field](migrations.md#recomputing-computed-fields) in its `filter` or its
grouping is different again. The worker starts nothing and writes no receipt.
It logs a warning, keeps the item and its cursor, and makes the item due again
one lease later. There is no HTTP `409`, because no request is waiting. The
item is not deleted, because deleting it would drop the schedule run, and
starting from old values is the failure the pending state removes. The fan-out
ignores the data source's `sort`, so a pending sort field does not stop it.

A worker that stops in the middle of the occurrence loses only its open batch.
The next worker claims the item and continues after the stored cursor. Each
record still starts at most once per occurrence.

A fan-out that began before a new release was activated finishes with the
trigger and data source of the release it started under, and starts the
process in the release that is active when each batch runs. A release that
removes the trigger deletes its state and its `trigger.schedule` item, so no
further occurrence runs, but a running fan-out finishes.

## Scheduled jobs

A schedule trigger may leave out `dataSource`. It then
starts a [process without a subject](process-flow.md#processes-without-a-subject)
once per occurrence, see
[D70](../decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md):

```json
{
  "id": "5f0e2d1c-3b4a-4c5d-8e6f-7a8b9c0d1e21",
  "kind": "trigger",
  "name": "EveryMorning",
  "formatVersion": 1,
  "type": "schedule",
  "schedule": "0 8 * * 1-5",
  "timeZone": "Asia/Ho_Chi_Minh",
  "process": "DailyOverdueReminder",
  "parameters": { "daysOverdue": 1 }
}
```

- Without `dataSource`, the process must have no subject, and `parameters`
  gives each of the process's parameters a literal value, in the JSON forms
  the [trigger resource](integration-resources.md#trigger-resource) uses for a
  data source's parameters. With `dataSource`, the process must have a subject
  and `parameters` keeps its meaning, the data source's parameters. A job
  whose process has a subject is `AXC0249`. A parameter that is unknown,
  repeated, mistyped or missing is `AXC0165`, `AXC0166` or `AXC0167`, as for a
  data source. A `null` is a mistyped value, because a process parameter has no
  optional flag.
- Occurrences, daylight saving and downtime work as in [Schedules](#schedules).
- The `trigger.fanOut` item of the occurrence starts the process once, in one
  transaction. Its receipt in `axis.trigger_starts` uses the nil UUID as
  `subject_id`, so the occurrence starts at most once. The instance holds the
  trigger's parameter values.
- A start that finds the process still running from an earlier occurrence is
  skipped and logged at Information level. The receipt stays `skipped`, and the
  log names no record.
- The process comes from the release that is active when the fan-out runs. When
  that process has a subject now, or its parameters no longer match the
  trigger's values, the start is skipped and logged.

## Delete triggers

A data change trigger with `"on": ["delete"]` starts a
process without a subject when a record is deleted:

```json
{
  "id": "9b8a7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c31",
  "kind": "trigger",
  "name": "TicketDeleted",
  "formatVersion": 1,
  "type": "dataChange",
  "entity": "Ticket",
  "on": ["delete"],
  "process": "TellLeadTicketDeleted",
  "parameters": { "number": "number", "title": "title" }
}
```

- `delete` is listed alone. With `create` or `update` it is `AXC0254`, and a
  `fields` list is `AXC0255`.
- The process must have no subject, otherwise it is `AXC0256`. `parameters`
  maps each process parameter to an expression over the record as it was before
  the delete, of the parameter's type, with the scope of a data change trigger's
  `when`. So does `when`. An unknown or repeated key is `AXC0165`, a value that
  is not a string is `AXC0166`, a parameter the trigger does not give is
  `AXC0167`, and an expression of another type has the expression checker's own
  code.
- It fires on every delete through the record command: the record API and a
  Studio test scenario. Activation, tenant clean-up and a raw SQL delete start
  nothing. The start commits in the delete's transaction, after the
  `record.deleted` audit record, as `system` and pinned to the active release.
- The `when` and the parameters are worked out after the row lock and the
  `409` check, and before the row is deleted, so they see the record whole,
  with its child rows and pending computed fields.
- A `when` that is not true, or that fails at run time, skips the start. So
  does a parameter that fails at run time, is `null` or does not fit its type.
  Each skip is logged at Information level with the trigger, the process and
  the record id, and the delete still commits. A process without a subject runs
  one instance at a time, so a delete while an instance of the process is still
  open skips its start and logs it.
- A delete that is refused starts nothing and logs nothing. This covers the
  `409` for a record that a `running` or `waiting` instance names, which comes
  first, the `409` for a referenced record, a denied delete and a delete
  outside the filters.
- A delete takes the application's start lock shared, as the first statement of
  its transaction, and pins the active release, as a create and an update do. When
  activation changed the release, the delete runs again on the new release, and
  it answers `503` after three changes.

## Dead letters

Deliveries that keep failing stop after a limit and are kept as dead letters, see
[D72](../decisions/d72-a-delivery-limit-and-dead-letters.md). The counting, the
backoff, the limit, the two tables, the log, the metric and the
[routes](#routes) are built for the start of an `async` trigger and the wake of
a waiting instance from `event.deliver`, for `trigger.fanOut` and for
`notification.email`.

**What counts.** Three kinds of work item deliver something, and each
failure is counted for its own target:

| Kind | Target |
| --- | --- |
| `event.deliver` | One occurrence and one `async` trigger, or one occurrence and one waiting instance |
| `trigger.fanOut` | One occurrence of a schedule trigger |
| `notification.email` | One email in the outbox |

**Counting.** When a delivery's transaction fails, a separate transaction
adds one to the target's count in `axis.delivery_failures`, with the error as
a fixed text, and makes the item due again after the backoff. Targets that
already committed, such as the other triggers of an occurrence, are skipped by
their receipts. A data error that would fail the same way again, as the
fan-out's SQLSTATE class 22, still ends the item at once, as today.

**Backoff and limit.** Attempt *n* waits 30 seconds times 2 to the power
*n* − 1, at most one hour. After `Worker:DeliveryAttempts` failures, default 5,
the counting transaction writes a dead letter instead. For `event.deliver` it
also writes the receipt with the outcome `deadLettered`, so delivery carries on
with the other targets. A fan-out item is deleted, and an email is set aside.

For an `async` trigger, the failure is counted in `axis.delivery_failures` under
the kind `event.deliver` and the target `{triggerId}/{eventId}`. When more than
one trigger fails in the same pass, the item is due again after the shortest
backoff among them. The count stays after a later success or a dead letter, and
only a redeliver clears it. The scenario runner of
[test scenarios](test-scenarios.md) makes the item due at once, so a delivery
that keeps failing runs 5 times there and is quietly dead-lettered.

For a schedule fan-out, the failure is counted under the kind `trigger.fanOut`
and the target `{triggerId}/{occurrenceAt}`, with the occurrence in UTC as the
receipts write it, such as `2026-01-01T05:00:00Z`. The count belongs to the
occurrence, so a failure in any batch adds to it. The stored cursor is kept while
the item backs off. The dead letter names the application, the trigger's process,
the trigger and `occurrence_at`.

For the wake of a waiting instance, the failure is counted under the kind
`event.deliver` and the target `{processInstanceId}/{eventId}`. The dead letter
names the application, the instance's process, the instance in
`process_instance_id` and the occurrence in `event_id`, and has no `trigger_id`.
No receipt is written, because `axis.trigger_starts` is keyed by trigger. The
dead letter itself stands for it: a pass of the item skips an instance that has
an `event.deliver` dead letter for the occurrence, in any state, so a discarded
wake is not tried again. Other waiting instances and the `async` triggers of the
occurrence still get it.

For an email, the failure is counted under the kind `notification.email` and
the target `{emailId}`, the id of its row in `axis.email_outbox`. At the limit
the item is deleted and the row stays `pending`, so nothing more is sent until
an operator redelivers. The dead letter names the application, the process of
the instance whose `notify` step staged the email, that instance in
`process_instance_id` and the row in `email_id`. The process is read through the
item's instance, since the outbox row does not record it.

**The counter.** `axis.delivery_failures` holds one row per target:

| Column | Type | Meaning |
| --- | --- | --- |
| `kind` | `text` | The work item kind. Part of the primary key |
| `target` | `text` | The target as a text key, so each kind builds its own. Part of the primary key |
| `attempts` | `integer` | The failures counted |
| `last_error` | `text` | The fixed text of the last failure |
| `updated_at` | `timestamptz` | The start of the transaction of the last failure, `now()` |

**The table.** `axis.dead_letters` holds one row per dead letter:

| Column | Type | Meaning |
| --- | --- | --- |
| `id` | `uuid` | A version 7 UUID |
| `kind` | `text` | `event.deliver`, `trigger.fanOut` or `notification.email` |
| `application_id` | `uuid` | The application |
| `process_id` | `uuid` | The process whose grants govern it: the trigger's process, the waiting instance's process or the process that sent the email |
| `event_id`, `trigger_id`, `process_instance_id`, `email_id` | `uuid`, null | What failed, as the kind needs |
| `occurrence_at` | `timestamptz`, null | The schedule occurrence, for a fan-out |
| `attempts` | `integer` | The failures counted |
| `last_error` | `text` | A fixed text. It holds no value, body, address or exception message. For a database error it is the exception type with its SQLSTATE, as `Npgsql.PostgresException with SQLSTATE P0001`, and for another error just the exception type |
| `state` | `text` | `open`, `redelivered` or `discarded` |
| `reason` | `text`, null | The operator's reason |
| `settled_by` | `text`, null | The user who redelivered or discarded it |
| `created_at`, `settled_at` | `timestamptz` | When it was written and settled |

### Routes

```http
GET /api/apps/{app}/processes/{process}/dead-letters?state=open
POST /api/apps/{app}/processes/{process}/dead-letters/{id}/redeliver
POST /api/apps/{app}/processes/{process}/dead-letters/{id}/discard
Content-Type: application/json

{ "reason": "The supplier system is back." }
```

- Listing needs the `inspect` grant on the process. Redeliver and discard need
  its `retry` grant. The `reason` follows the rule of the
  [operator commands](process-inspection.md#operator-commands). Listing does not
  check whether the user may read the event's record, because a dead letter
  holds ids and a fixed error text, never a record value.
- **Redeliver** clears the target's count, removes a `deadLettered` receipt,
  and queues the delivery again for that target only. For a waiting instance,
  that is an `event.deliver` item that names the event and the instance and needs
  nothing from the active release: it wakes the instance once if it still waits
  for the event, and otherwise does nothing. **Discard** closes the dead letter
  and leaves the instance waiting. Either one on a dead letter that is not `open` is a `409`.
- The audit records are `deadLetter.redelivered` and `deadLetter.discarded`,
  with the reason, see [audit records](storage.md#audit-records-and-sequence-counters).
- The [process inspector widget](process-inspector-widget.md) lists the open
  dead letters of its process, with Redeliver and Discard for a user with the
  grant.

**The list.** It shows the dead letters of the process, newest first, paged as
the instance list is, with `page`, `pageSize`, `totalCount` and `items`. The
optional `state` is `open`, `redelivered` or `discarded`. Any other value is a
`400` keyed `state`, and without it every state is listed. A request is checked
in this order: session (`401`), application and process (`404`), the `inspect`
grant (`403`), then the query (`400`). Each item has `id`, `kind`, `eventId`,
`triggerId`, `processInstanceId`, `emailId`, `occurrenceAt`, `attempts`,
`lastError`, `state`, `reason`, `settledBy`, `createdAt`, `settledAt` and
`commands`, with times in UTC and null for what its kind lacks. `commands` is
`["redeliver", "discard"]` for an `open` dead letter when the user holds the
`retry` grant, and empty otherwise.

**The commands.** A command is checked in this order: session (`401`),
application and process (`404`), the `retry` grant (`403`), content type
(`415`), then the body (`400`), which is exactly `{ "reason": "<text>" }`. An
`id` that is not a hyphenated UUID, or that names no dead letter of this
process, is a `404` titled "No dead letter of this process exists with this
id.". One transaction then locks the row and runs the command. A dead letter
that is not `open` is a `409` titled "The dead letter is not open.". A refused
command writes nothing. A success answers `200` with the dead letter in the
list's shape, now settled with its reason and the user in `settledBy`, and
with empty `commands`.

- **`event.deliver`.** A redeliver needs the trigger as an `async` event trigger
  in the active release. Otherwise it is a `409` titled "The trigger of the dead
  letter is not in the active release.". It clears the count of the trigger and
  the event, removes the trigger's `deadLettered` receipt and queues an
  `event.deliver` item that names the trigger. The worker then delivers to that
  trigger alone, see [event delivery](#event-delivery). A trigger that already
  started its process for the occurrence is not run again.
- **`trigger.fanOut`.** A redeliver needs the schedule trigger in the active
  release, with the same `409` otherwise. It clears the count of the occurrence
  and queues the fan-out on the active release. The receipts skip the records
  that already started for that occurrence.
- **`notification.email`.** A redeliver needs nothing from the active release,
  since the email is already rendered and stored. It clears the count of the
  email and queues a `notification.email` item for the same row and instance.
  The worker then sends it as before, even when the instance was cancelled
  meanwhile.
- **Other kinds.** A redeliver of a kind that is not built is a `409` titled
  "This kind of dead letter cannot be redelivered.". Discard closes a dead
  letter of any kind.
- **Audit.** Each success writes `deadLetter.redelivered` or `deadLetter.discarded`
  in the same transaction. It names the application and no entity, record or
  instance, and its details are `deadLetterId`, `kind`, `triggerId`, `eventId`,
  `occurrenceAt` and `reason`, with null for what the kind lacks.

**Logs and metrics.** The worker logs each dead letter at Warning level with
its kind and target ids, and counts it in `axis.dead_letters.created`, with
`axis.tenant` and `axis.work_item.kind` as attributes, as the other
[worker metrics](process-execution.md#worker-metrics) do. The limit is the
`Worker:DeliveryAttempts` [worker setting](process-execution.md#worker-settings).
