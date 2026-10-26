# Process flow

Detailed reference for the steps that make a process wait, call another
process or loop, for the `createRecord` operation, and for processes without a
subject. The process resource and the first four step types are in
[processes.md](processes.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

Everything in this file is _(planned for M14)_, except the
[Timer step](#timer-step), [Loops](#loops) through a `task` or a `timer` and the
calls in them, and the compile checks of the
[sub-process step](#sub-process-step), which are built. The design follows
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

- It has exactly one of `wait` and `until` (`AXC0254`), and a `next`.
- `wait` is a positive fixed-length duration, by the rule of a task's `dueIn`
  (`AXC0084`, reported at `/steps/{i}/wait`). The time is the step's
  transaction time plus `wait`.
- `until` is an expression that gives a `date-time`, with the scope of a
  `when`. A time in the past is due at once. A `null` fails the step.
- The step's transaction sets the instance `waiting` and queues a
  `process.timer` work item due at the time. The item stores the revision the
  step left in its `revision` column. Its history output is
  `{ "state": "waiting", "dueAt" }`. The step writes no audit record while it
  waits.
- The item checks that the instance still waits at the revision the step left
  it at. Then it resumes the instance at `next`, writes `{ "next" }` and the
  `process.stepCompleted` audit record, and queues the instance's next
  `process.step` item. An item whose instance moved on, or was cancelled, is
  deleted with no other write.
- If the resume itself throws, the waiting instance is marked `failed`, with a
  history error and a `process.failed` audit record. Otherwise the deleted item
  would leave the instance waiting for ever. An operator retry then runs the
  timer step again.
- A test scenario's `advanceTime` runs due `process.timer` items, as it runs
  other due work. A timer also runs after an action when its time has already
  passed on the scenario clock. A scenario never makes a timer due early.

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

- **Compile checks are built, the run is not.** The compiler checks and builds
  the step, and the instance detail graph shows it as an `operation` step. An
  instance that reaches it still fails with the engine's error for a step it
  cannot run, until the engine part is built.
- `process` names a process of the same application, ignoring letter case,
  otherwise it is `AXC0250` at `/steps/{i}/process`. A process may not reach
  itself through sub-process steps, directly or through others. Each step that
  closes such a loop is `AXC0251` at `/steps/{i}/process`, naming the chain,
  such as `'A' → 'B' → 'A'`. A process that starts itself is `'A' → 'A'`.
- `subject` is an expression that gives a reference to a record of the child's
  subject entity. It may be left out when the child's subject entity is the
  parent's. The child then runs for the same record. A missing `subject` is
  `AXC0252` at `/steps/{i}` when the child's entity is not the parent's or the
  parent has no subject. It is not checked when either entity is unknown. A
  `subject` given to a child without a subject is `AXC0252` at
  `/steps/{i}/subject`. The expression is type-checked over the parent's scope,
  as a decision `when` is, and keeps its own code.
- A child without a subject takes `parameters` instead of `subject`, see
  [Processes without a subject](#processes-without-a-subject). `parameters`
  maps each parameter name of the child to an expression of its type. Every
  parameter is required. A parameter that is missing is `AXC0253` at
  `/steps/{i}/parameters`, and one that is unknown or repeated, ignoring
  letter case, is `AXC0253` at `/steps/{i}/parameters/{name}`. `parameters`
  given to a child with a subject are `AXC0253` at `/steps/{i}/parameters`.
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
waiting step. A `task` and a `timer` are built as waiting steps. _(Planned for M14)_ A
`waitEvent` is a waiting step too. `AXC0073` reports only a
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

A process may leave out `entity`. It then declares typed `parameters`. The
compile checks in this section are built, and so are the instance storage,
running such an instance on the engine and the start endpoint. The other starts
and inspection described below are _(planned for M14)_. Until inspection is
built, the instance list is empty, and the instance detail and the operator
commands answer `404`.

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
  types of rule parameters. Names are unique, ignoring letter case, and a
  repeated name is `AXC0245`. A process has `entity` or `parameters`, not both
  and not neither. Either mistake is `AXC0244`, reported at `/parameters` when
  there are both and at the root of the file when there is neither.
- Expressions see the parameters by name, and the named rules. There is no
  record, so there is no `id` and no field path. The start condition, a
  decision `when` and the inputs of a connector or package operation are
  checked over this scope.
- **Steps.** `decision`, `createRecord`, `callConnector`, package operations,
  `timer`, `subProcess`, `notify` and `end` are allowed. `updateRecord`,
  `raiseEvent`, `task` and `waitEvent` need a subject and are `AXC0246`. So is
  the `output` of a `callConnector` or package operation step, because it
  writes the subject. Only the step types that exist today are checked: `task`,
  `updateRecord`, `raiseEvent` and the outputs. The compiler reports no other
  problem in such a step.
- **Triggers and other starts by name.** An event, data change or schedule
  trigger that names a process without a subject is `AXC0247`, except a
  schedule trigger without a data source, which must name one (`AXC0249`
  otherwise). An endpoint, a
  page `startProcess` action and a scenario step that name one are reported
  with their usual codes for a process over another entity: `AXC0117`,
  `AXC0088` and `AXC0184`.
- **Instance.** `axis.process_instances` stores `subject_id` as null and the
  values in a `parameters` `jsonb` column. The partial unique index treats a
  null subject as one value (`NULLS NOT DISTINCT`), so one instance of the
  process runs at a time. The engine evaluates a decision `when` and the inputs
  of a connector or package operation over the stored values, and locks, reads
  and writes no record. The step history `input` has a null `subjectId` and
  `subjectVersion`.
- **Start endpoint.** The body is `{ "parameters": { … } }` instead of
  `{ "subjectId" }`. A missing, unknown or mistyped parameter is a `400` keyed
  `/parameters/{name}`. The `startCondition` sees the parameters. A running
  instance is the `409`. The user needs the `start` grant only. See
  [the start endpoint](processes.md#a-process-without-a-subject).
- **Other starts.** A [schedule trigger without a data
  source](process-events.md#scheduled-jobs), a [delete
  trigger](process-events.md#delete-triggers) and a sub-process step give the
  parameters. A start that finds the process running is skipped, as for a
  record.
- **Inspection.** The instance list and detail show "no subject". Inspecting,
  retrying, cancelling and resolving need only the grant.
