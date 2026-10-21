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
saves, on `updateRecord` steps, on connector outputs and on package operation
outputs, see [Starts from triggers](#starts-from-triggers). The worker runs
schedule triggers too, once for each occurrence and for each record of the
trigger's data source, see [Schedules](#schedules).
A `raiseEvent` step raises an
[event](integration-resources.md#event-resource) for its subject record. A
[trigger](integration-resources.md#trigger-resource) starts a process for one record
when an event is raised, when a record is created or updated, or when a
schedule is due. Every start happens at most once for the
same cause, even after a worker crash.

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
`updateRecord` step, a connector output or a package operation output, takes
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
| Data change | The write's transaction: a record API create or update, an inbound endpoint's create, a task form save, an `updateRecord` step, a connector output or a package operation output | The write is not in `on`, an update changed none of the `fields`, the `when` is not true, the process already runs for the record, or the `startCondition` is not true |
| Schedule | The `trigger.fanOut` transaction of one batch, which holds the starts of up to 100 records | The record was already handled for the occurrence, the record no longer exists, the process already runs for the record, or the `startCondition` is not true |

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
after its `record.updated` audit record. For a task form save, they run after
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

1. The worker reads the triggers for the event's name, ignoring letter case.
   With none, or when the application has no active release, the item is
   deleted with no other write.
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

A transaction that fails for another reason leaves the item claimed until its
lease expires, and the delivery is then tried again. There is no limit on
these retries yet.

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

**Failures.** A fan-out transaction that fails leaves the item claimed. It
runs again after the lease expires, as event delivery does, and the receipts
make each record start at most once per occurrence. A data error in the data
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
