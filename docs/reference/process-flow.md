# Process flow

Detailed reference for the steps that make a process wait, call another
process or loop, for the `createRecord` operation, and for processes without a
subject. The process resource and the first four step types are in
[processes.md](processes.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

Everything in this file is _(planned for M14)_, except [Loops](#loops) through
a `task` and the calls in them, which are built. The design follows
[D69](../decisions/d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md)
and
[D70](../decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md).
New compile problems are listed as new diagnostics. They get their codes in
the [diagnostic code table](diagnostic-codes.md) when they are built.

## Timer step

A `timer` step waits for a time, then goes on to `next`:

```json
{ "name": "coolDown", "type": "timer", "wait": "PT4H", "next": "remind" }
```

```json
{ "name": "untilDue", "type": "timer", "until": "dueDate", "next": "remind" }
```

- It has exactly one of `wait` and `until`, and a `next`.
- `wait` is a positive fixed-length duration, by the rule of a task's `dueIn`
  (`AXC0084`). The time is the step's transaction time plus `wait`.
- `until` is an expression that gives a `date-time`, with the scope of a
  `when`. A time in the past is due at once. A `null` fails the step.
- The step's transaction sets the instance `waiting` and queues a
  `process.timer` work item due at the time. Its history output is
  `{ "state": "waiting", "dueAt" }`.
- The item checks that the instance still waits at the revision the step left
  it at. Then it resumes the instance at `next` and writes `{ "next" }`. An
  item whose instance moved on, or was cancelled, is deleted with no other
  write.
- A test scenario's `advanceTime` runs due `process.timer` items, as it runs
  other due work.

## Wait for event step

A `waitEvent` step waits until an event about the subject record is raised:

```json
{
  "name": "awaitPayment",
  "type": "waitEvent",
  "event": "PaymentReceived",
  "when": "amount >= 0",
  "timeout": { "after": "P2D", "next": "chase" },
  "next": "markPaid"
}
```

- `event` names an `event` whose entity is the subject entity. Another entity
  is a new diagnostic.
- `when` is optional. It is a boolean expression over the event's payload
  fields, as an event trigger's `when` is.
- `timeout` is optional. `after` is a fixed-length duration, by the rule of
  `dueIn`. `next` names the step taken when no event came in time.
- **Which occurrence.** The step matches an occurrence in `axis.events` that
  names the instance's subject record, was raised at or after the instance's
  `created_at`, has not been used by this instance, and whose `when` is true.
  It takes the earliest one.
- **When the step runs.** Its transaction looks for a match first. With one,
  it records the match and goes to `next` at once. Without one, it sets the
  instance `waiting`. With a `timeout`, it queues a `process.timer` item due
  at the step's time plus `after`.
- **Waking.** [Event delivery](process-events.md#event-delivery) also delivers
  each occurrence to the instances that wait for its event on its record. Each
  instance gets its own transaction. It locks the instance, checks that it
  still waits in this step at the same revision, evaluates `when`, records the
  match and resumes the instance at `next`.
- **One use.** `axis.process_event_matches` holds one row per instance and
  occurrence, with a unique key on both. So an occurrence wakes an instance
  once, also in a later pass of a loop.
- **Timeout.** The `process.timer` item resumes the instance at
  `timeout.next`, by the same revision check. The event and the timeout race,
  and the first to commit wins.
- History output: `{ "state": "waiting" }` when it waits, `{ "next",
  "eventId" }` for a match and `{ "next", "timeout": true }` for a timeout.

Events are raised by `raiseEvent` steps. Raising an event from an inbound
endpoint call waits for M17.

## Sub-process step

A `subProcess` step starts another process and waits until it ends:

```json
{ "name": "fulfil", "type": "subProcess", "process": "OrderFulfilment", "subject": "order", "next": "close" }
```

- `process` names a process of the same application. A process may not reach
  itself through sub-process steps, directly or through others. Such a cycle is
  a new diagnostic.
- `subject` is an expression that gives a reference to a record of the child's
  subject entity. It may be left out when the child's subject entity is the
  parent's. The child then runs for the same record.
- A child without a subject takes `parameters` instead of `subject`, see
  [Processes without a subject](#processes-without-a-subject).
- **Start.** The step's transaction starts the child as a
  [start from a trigger](process-events.md#starts-from-triggers) does, as
  `system`, and sets the parent `waiting`. The child is pinned to the
  parent's release, not the active one. Its instance row holds
  `parent_instance_id` and `parent_step`. The `process.started` audit record
  adds `"parentInstanceId"`.
- **Cannot start.** A child that already runs for that record, or whose
  `startCondition` is not true, fails the parent's step with a fixed error.
- **End.** A child that completes queues a `process.resume` item for its
  parent. The item resumes the parent at `next`, by the revision check.
- **Failure.** A child that fails leaves the parent `waiting`. The inspector
  shows the failed child under the parent, and an operator retries the child.
  A cancelled child fails the parent's step.
- **Cancel.** Cancelling the parent cancels its `running` and `waiting`
  children, in the same transaction.
- History output: `{ "state": "waiting", "childInstanceId" }`, then
  `{ "next", "childInstanceId" }`.

## Loops

A process may loop back to an earlier step when every cycle passes through a
waiting step. A `task` is built as a waiting step. _(Planned for M14)_ A
`timer` and a `waitEvent` are waiting steps too. `AXC0073` reports only a
cycle with no waiting step, which would run for ever. A cycle of `decision` and
`operation` steps is reported, and so is a cycle through a call step with no
task in it. See [Compile checks](processes.md#compile-checks).

- Each pass writes its own history rows, told apart by `revision`.
- **Calls in a loop.** A step stages one connector call or one package call
  per pass. The unique index of `axis.connector_calls` and
  `axis.package_calls` is on (`process_instance_id`, `step`,
  `staged_revision`), where `staged_revision` is the instance revision the
  step ran at. An operator retry stages the latest call of the step again,
  under the same operation identity, and sets its `staged_revision` to the
  revision the step runs at again. Resolve finds its call by id. The
  inspector lists every pass's calls, oldest first.
- **Service desk.** A reopened ticket loops back to the agent's resolve task,
  and its status goes back to `assigned`.

## Create record operation

A `createRecord` operation creates a record of another entity:

```json
{
  "name": "logVisit",
  "type": "operation",
  "operation": "createRecord",
  "entity": "TicketNote",
  "set": { "ticket": "id", "text": "'Reopened by the requester'" },
  "next": "resolve"
}
```

- `entity` names a root entity of the application, not a child entity. Each
  is a new diagnostic.
- `set` maps fields of that entity to expressions with the scope of a `when`.
  The checks of an `updateRecord` `set` apply to its keys and types.
- Process expressions see `id`, the subject record's id, typed as a reference
  to the subject entity.
- The step creates the record through the record create command, as `system`,
  in the step's transaction. Sequences, computed fields, validations and field
  defaults apply. A creator field gets `system`. A validation failure fails the
  step.
- It writes the `record.created` audit record with the actor `system`, and data
  change triggers on `create` fire, as for every write through the record
  command.
- History output: `{ "next", "recordId" }`.

## Processes without a subject

A process may leave out `entity`. It then declares typed `parameters`:

```json
{
  "id": "0c7a4c1e-9f1e-4f43-9b1a-6a3d0b5e2f11",
  "kind": "process",
  "name": "DailyOverdueReminder",
  "formatVersion": 1,
  "parameters": [{ "name": "daysOverdue", "type": "integer" }],
  "start": "remind",
  "steps": [
    {
      "name": "remind",
      "type": "operation",
      "operation": "notify",
      "to": [{ "role": "service-desk-lead" }],
      "channels": ["inApp"],
      "message": {
        "subject": { "textKey": "notifications.overdue.subject" },
        "body": { "textKey": "notifications.overdue.body" },
        "values": { "days": "daysOverdue" }
      },
      "next": "done"
    },
    { "name": "done", "type": "end" }
  ]
}
```

- `parameters` is a list of `{ "name", "type" }`. The types are the scalar
  types of rule parameters. Names are unique, ignoring letter case. A process
  has `entity` or `parameters`, not both. Each is a new diagnostic.
- Expressions see the parameters by name, and the named rules. There is no
  record, so there is no `id` and no field path.
- **Steps.** `decision`, `createRecord`, `callConnector`, package operations,
  `timer`, `subProcess`, `notify` and `end` are allowed. `updateRecord`,
  `raiseEvent`, `task` and `waitEvent` need a subject and are a new
  diagnostic. So is the `output` of a `callConnector` or package operation
  step, because it writes the subject.
- **Instance.** `axis.process_instances` stores `subject_id` as null and the
  values in a `parameters` `jsonb` column. The partial unique index treats a
  null subject as one value (`NULLS NOT DISTINCT`), so one instance of the
  process runs at a time.
- **Start endpoint.** The body is `{ "parameters": { … } }` instead of
  `{ "subjectId" }`. A missing, unknown or mistyped parameter is a `400` keyed
  `/parameters/{name}`. The `startCondition` sees the parameters. A running
  instance is the `409`. The user needs the `start` grant only.
- **Other starts.** A [schedule trigger without a data
  source](process-events.md#scheduled-jobs), a [delete
  trigger](process-events.md#delete-triggers) and a sub-process step give the
  parameters. A start that finds the process running is skipped, as for a
  record.
- **Inspection.** The instance list and detail show "no subject". Inspecting,
  retrying, cancelling and resolving need only the grant.
