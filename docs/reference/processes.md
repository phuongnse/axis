# Processes

Detailed reference for processes: the resource shape, the steps, the compile
checks, the start endpoint, the instance and task states, releases and running
work, and the tables. The rest of the process reference is in these files:

- [task-api.md](task-api.md): the task API.
- [process-flow.md](process-flow.md): timer, wait for event and sub-process
  steps, loops, the `createRecord` operation and processes without a subject.
  The timer step and loops through a `task` or a `timer` are built, the rest
  is _(planned for M14)_.
- [task-deadlines.md](task-deadlines.md): task escalations, deadlines, claim
  and reassign. Claim and reassign are built, the rest is _(planned for M14)_.
- [notifications.md](notifications.md): the `notify` operation and in-app and
  email notifications _(planned for M14)_.
- [process-execution.md](process-execution.md): execution, connector calls,
  package operations and the worker settings.
- [process-events.md](process-events.md): raised events, triggers and
  schedules.
- [process-inspection.md](process-inspection.md): process inspection and the
  operator commands.

The `process` resource kind is
built with `decision`, `task`, `operation`, `end` and `timer` steps, and so are their
[compile checks](#compile-checks). A compiled process is part of the release
model. The worker host, its claims (see [Execution](process-execution.md#execution)), the
[worker settings](process-execution.md#worker-settings), the [start endpoint](#start-endpoint) and
the `axis.process_instances`, `axis.process_step_history`,
`axis.process_tasks`, `axis.process_start_receipts`,
`axis.endpoint_receipts`, `axis.process_work_items`, `axis.connector_calls`,
`axis.package_calls` and `axis.events` tables are built too. So is the engine that runs
`decision`, `task`, `operation`, `end` and `timer` steps, see
[Running a step](process-execution.md#running-a-step). The [task API](task-api.md) routes that
list the user's tasks, read one task and complete one are built. Completing
a task resumes its `waiting` instance, see
[Completing a task](task-api.md#completing-a-task). A start and the task API follow the
user's policies, see [Start endpoint](#start-endpoint) and
[Policies on tasks](task-api.md). A `callConnector` step, which calls an
external system, is checked and compiled. Its step stages the call and leaves
the instance `waiting`. The worker then sends the call and records its
result, see [Connector calls](process-execution.md#connector-calls). Retries under the step's
retry policy are built too, and so is reconciliation of a call whose outcome
is unknown. A `raiseEvent` step, which raises an event about the subject
record, is checked, compiled and run. Its step stores the event in
`axis.events`, see [Raise event step](process-events.md#raise-event-step). Package operation
steps are checked and compiled against the manifest of the pinned package
version. Their step stages the call and leaves the instance `waiting`, and the
worker runs the operation and records its result, see
[Package operations](process-execution.md#package-operations). Retries of package calls under the
step's retry policy are built too. The capability limits of package operations are built too. So are
triggers, which start
processes when something happens, see
[Events and triggers](process-events.md). Process inspection, where
authorized users see an instance's graph, path, tasks and calls, is built. The `inspect`
grant, the instance list, the instance detail with its `calls`, the
`sensitive` flag and the inspector widget are built, see
[Process inspection](process-inspection.md#process-inspection). Operator cancel
and retry are built, see [Cancel](process-inspection.md#cancel) and [Retry](process-inspection.md#retry). Operator
resolve is built for connector and package calls,
see [Operator commands](process-inspection.md#operator-commands).

Dn refers to [decisions.md](../decisions.md). The design follows
[D11](../decisions/d11-durable-process-engine.md) and
[D19](../decisions/d19-process-resource-and-worker-host.md), and human
tasks follow
[D20](../decisions/d20-human-tasks-task-inbox-and-forms.md). The
reasons for durable execution are in
[knowledge](../domain/knowledge.md#execution), and for human tasks in
[knowledge](../domain/knowledge.md#human-work).

- **Process.** A `process` resource is a set of named steps and the
  transitions between them. It names one entity, its subject.
- **Process instance.** One run of a process. It is pinned to the release
  that was active when it started.
- **Subject record.** The record of the subject entity that an instance runs
  for. Each instance has exactly one.
- **Step.** One unit of a process, such as a decision or a human task. A step
  names the step that follows it.
- **Task.** One occurrence of a `task` step: the work a person must decide
  on. It names its assignee, and it is `open` until one decision completes
  it.

Process expressions use the language of [expressions.md](expressions.md). For
what they can see, see
[Names and references](expressions.md#names-and-references).

## Resource shape

The purchase request approval needs one rule and one process. The purchase
request sample in `samples/apps/purchase-requests/` holds both. The finance
threshold is a `rule` with no parameters, so it is configured, not hard-coded:

```json
{
  "id": "313225f1-c69f-41bc-b746-12d91f127c39",
  "kind": "rule",
  "name": "FinanceThreshold",
  "formatVersion": 1,
  "parameters": [],
  "resultType": "decimal",
  "expression": "10000"
}
```

The process submits the request and records when, asks the department
manager, asks a finance reviewer at or above the threshold, and ends
approved, returned or rejected. An approved request then gets its purchase
order from the purchasing system, see [Connector calls](process-execution.md#connector-calls), and
a payment reference from the sample extension package, see
[Package steps](process-execution.md#package-step-shape):

```json
{
  "id": "4b60c03e-02d0-4329-80c6-75f972190399",
  "kind": "process",
  "name": "PurchaseRequestApproval",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "startCondition": {
    "expression": "status in ('draft', 'returned') and totalAmount is not null",
    "message": { "textKey": "purchaseRequest.cannotSubmit" }
  },
  "start": "submit",
  "steps": [
    {
      "name": "submit",
      "type": "operation",
      "operation": "updateRecord",
      "set": { "status": "'submitted'", "submittedAt": "now()" },
      "next": "managerApproval"
    },
    {
      "name": "managerApproval",
      "type": "task",
      "label": { "textKey": "purchaseRequest.managerApproval" },
      "assignee": { "user": "department.manager" },
      "form": "PurchaseRequestReview",
      "dueIn": "P3D",
      "outcomes": [
        { "name": "approve", "label": { "textKey": "task.approve" }, "next": "financeCheck" },
        { "name": "return", "label": { "textKey": "task.return" }, "next": "markReturned" },
        { "name": "reject", "label": { "textKey": "task.reject" }, "next": "markRejected" }
      ]
    },
    {
      "name": "financeCheck",
      "type": "decision",
      "branches": [{ "when": "totalAmount >= FinanceThreshold()", "next": "financeApproval" }],
      "otherwise": "markApproved"
    },
    {
      "name": "financeApproval",
      "type": "task",
      "label": { "textKey": "purchaseRequest.financeApproval" },
      "assignee": { "role": "finance-reviewer" },
      "form": "PurchaseRequestReview",
      "dueIn": "P3D",
      "outcomes": [
        { "name": "approve", "label": { "textKey": "task.approve" }, "next": "markApproved" },
        { "name": "return", "label": { "textKey": "task.return" }, "next": "markReturned" },
        { "name": "reject", "label": { "textKey": "task.reject" }, "next": "markRejected" }
      ]
    },
    {
      "name": "markApproved",
      "type": "operation",
      "operation": "updateRecord",
      "set": { "status": "'approved'" },
      "next": "createPurchaseOrder"
    },
    {
      "name": "createPurchaseOrder",
      "type": "operation",
      "operation": "callConnector",
      "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" },
      "input": { "requestNumber": "number", "supplierCode": "supplier.code", "amount": "totalAmount" },
      "output": { "orderNumber": "purchaseOrderNumber" },
      "retry": { "attempts": 5, "backoff": "PT30S" },
      "next": "assignPaymentReference"
    },
    {
      "name": "assignPaymentReference",
      "type": "operation",
      "operation": "payment-references/creditorReference",
      "input": { "reference": "number" },
      "output": { "creditorReference": "paymentReference" },
      "retry": { "attempts": 3, "backoff": "PT10S" },
      "next": "done"
    },
    {
      "name": "markReturned",
      "type": "operation",
      "operation": "updateRecord",
      "set": { "status": "'returned'" },
      "next": "done"
    },
    {
      "name": "markRejected",
      "type": "operation",
      "operation": "updateRecord",
      "set": { "status": "'rejected'" },
      "next": "done"
    },
    { "name": "done", "type": "end" }
  ]
}
```

- `entity` names the subject entity. Each instance runs for one record of it.
  It is optional. A process without it declares `parameters` instead, a list
  of `{ "name", "type" }` with the scalar types of rule parameters: `text`,
  `integer`, `decimal`, `boolean`, `date` and `date-time`. It has no subject
  record. See [Processes without a
  subject](process-flow.md#processes-without-a-subject).
- `startCondition` is optional. Its `expression` is a boolean expression over
  the subject record. A start is allowed only when it is true. A `false`, a
  `null` or a run-time error fails the start. Its `message` is a label, shown
  when a start fails the condition.
- `start` names the first step.
- `steps` is an array of step objects. Each has a `name` and a `type`. The
  other properties depend on the type, see [Steps](#steps).
- Transitions name the next step by its name: `next`, `branches[].next`,
  `otherwise` and `outcomes[].next` of a task.
- `kind` stays the resource kind. Steps use `type`, as fields and widgets do.

## Steps

M3 has four step types, and the `timer` step is built too. Wait for event and
sub-process come later. _(Planned for M14)_ `waitEvent` and `subProcess` steps,
and the `createRecord` and `notify` operations, see [process flow](process-flow.md)
and [notifications](notifications.md#notify-operation).

- **`decision`.** `branches` is an ordered list. Each branch has a `when`
  expression and a `next` step. The branches are evaluated in order, and the
  first one that is true wins. A `when` that gives `null` counts as false.
  `otherwise` is required and names the step taken when no branch is true.
- **`task`.** A human task. The instance waits until a person completes the
  task with one of its outcomes. It has these parts:
  - `label` is required. It is a label with a text key, shown in the task
    inbox and on the task page.
  - `assignee` has exactly one of `{ "user": "<expression>" }` and
    `{ "role": "<role name>" }`. A `user` expression gives `text`, a user id,
    such as `department.manager`. A `role` names a role the user must hold.
    Queues come later. _(Planned for M14)_ A user who holds the role may
    claim the task, see [Claim and release](task-deadlines.md#claim-and-release).
  - `form` names a [form resource](form-widget.md) whose entity is the subject
    entity. The task page shows it over the subject record.
  - `dueIn` is optional. It is a positive ISO 8601 duration in whole
    numbers: either weeks, such as `P2W`, or days with an optional time part
    of hours, minutes and seconds, such as `P3D`, `PT4H` or `P1DT12H`. Years
    and months are `AXC0084` until calendar durations come with the
    [time zone rule](../decisions/d22-current-time-and-time-zones.md), because they have no fixed length. The task's due date is the time the task is created plus `dueIn`. It is
    shown only.
  - `deadline` is optional. It is `{ "after", "next" }`. `after` is a duration
    by the rule of `dueIn`, counted from the task's creation. `next` names a
    step. When the task is still `open` after that time, the task becomes
    `expired` and the instance moves to `next`, see
    [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
    _(Planned for M14)_ `escalations`, see the same section.
  - `outcomes` is an array of `{ "name", "label": { "textKey" }, "next" }`,
    such as `approve`, `return` and `reject`. Each names the step taken when
    the task is completed with it.

  The step's transaction evaluates the assignee, creates the task as `open`
  and sets the instance to `waiting`. A `user` expression that gives `null`
  or blank text fails the step. Completing the task resumes the instance, see
  [Task API](task-api.md).
- **`operation`.** M3 has one built-in operation, `updateRecord`. Its `set`
  maps fields of the subject record to expressions, and `next` names the step
  that follows. The step writes the values with the record update rules, so
  computed fields are recomputed and validations run, in the step's
  transaction. A validation failure fails the step. An `operation` of the
  form `<package>/<operation>` names an operation from an extension package.
  It is checked and compiled, and it runs outside the step's transaction,
  see [Package operations](process-execution.md#package-operations). Its calls are retried under the
  step's retry policy, and its context hands it only the capabilities it declares. A second built-in operation, `callConnector`, calls an
  external system outside the step's transaction, see
  [Connector calls](process-execution.md#connector-calls). Its step is checked, compiled, staged
  and sent, its call is retried, and an unknown outcome is reconciled. A third
  built-in operation, `raiseEvent`, raises an event for the subject record in
  the step's transaction, see [Raise event step](process-events.md#raise-event-step).
- **`end`.** The instance is `completed` when it reaches an `end` step. An
  `end` step has no `next`.
- **`timer`.** It waits for a time, then goes on to `next`. It has exactly one
  of `wait`, a positive fixed-length duration, and `until`, an expression that
  gives a `date-time`. The step's transaction sets the instance `waiting` and
  queues a `process.timer` work item due at the time. See
  [Timer step](process-flow.md#timer-step).

## Compile checks

The compiler reports these as diagnostics. Every problem of a process is
reported in one pass. See
[the Resolve step](configuration.md#configuration-pipeline) for the details.

- `entity` names a loaded entity (`AXC0067`) that is not a child entity
  (`AXC0068`).
- A process has `entity` or `parameters`, not both and not neither
  (`AXC0244`). Parameter names are unique, ignoring letter case (`AXC0245`).
  A process without a subject has no `task`, `updateRecord` or `raiseEvent`
  step and no `output` on a connector or package step (`AXC0246`). Its start
  condition and each `when` see its parameters and the named rules, and no
  field, path or `id`. A trigger that names such a process is `AXC0247`, except a schedule trigger
  without a data source, see [Scheduled
  jobs](process-events.md#scheduled-jobs). The
  start endpoint answers `400` for it until starting these processes is built,
  see [Processes without a
  subject](process-flow.md#processes-without-a-subject).
- Step names are unique within the process, ignoring letter case
  (`AXC0069`).
- `start`, every branch `next`, `otherwise`, the `next` of an operation or a
  timer and every outcome `next` of a task name a step of the process, ignoring letter
  case (`AXC0070`).
- Every step is reachable from `start` (`AXC0071`).
- Every step has a path to an `end` step (`AXC0072`).
- The steps form no cycle that has no waiting step (`AXC0073`). A cycle that
  passes through a `task` or a `timer` step is allowed, see
  [Loops](process-flow.md#loops). A cycle of steps that never wait, such as
  `decision` and `operation` steps, would run for ever and is reported.
  _(Planned for M14)_ A `waitEvent` step is a waiting step too.
- A decision has at least one branch (`AXC0004`).
- Each `when` and the `startCondition` expression give a boolean. They see
  the subject entity's fields, computed ones included, `id`, the subject
  record's id, its child collections through aggregates, the named rules, and
  paths through reference fields such as `department.manager.name`. A path takes at most 3 hops
  (`AXC0058`), and a `.` after a field that is not a reference is `AXC0047`.
  See [Scope in a process expression](expressions.md#names-and-references).
- An operation names `updateRecord`, `callConnector`, `raiseEvent` or a
  package operation written `<package>/<operation>`, matched exactly as a
  step `type` is (`AXC0078` at `/steps/{i}/operation`).
- Each key of an `updateRecord` `set` names a field of the subject entity,
  ignoring letter case, that no earlier key of the step names (`AXC0079`).
  The field is not computed, not numbered by a sequence and not a child
  collection (`AXC0080`). Each is reported at `/steps/{i}/set/{field}`.
- Each `set` expression fits its field's type, with the same scope as a
  `when`. A problem is reported at `/steps/{i}/set/{field}` with its
  expression code, such as `AXC0048` for a value of the wrong type.
- A `callConnector` step's checks are listed under
  [Connector calls](process-execution.md#connector-calls) (`AXC0102` to `AXC0109`). A package
  operation step's checks are listed under
  [Package step shape](process-execution.md#package-step-shape) (`AXC0150` and `AXC0151`).
- A `raiseEvent` step's checks are listed under
  [Raise event step](process-events.md#raise-event-step) (`AXC0125` to `AXC0128`).
- An operation has `operation` and `next`, and no `branches` or `otherwise`.
  An `updateRecord` step has a `set` with at least one field, and no
  `connector`, `input`, `output`, `retry`, `event` or `fields`. A
  `callConnector` step has `connector` and `input`, and no `set`, `event` or
  `fields`. A `raiseEvent` step has `event` and `fields`, and no `set`,
  `connector`, `input`, `output` or `retry`. A decision has no `operation`,
  `set`, `connector`, `input`, `output`, `retry`, `event`, `fields` or
  `next`. Neither an operation nor a decision has a task's `label`,
  `assignee`, `form`, `dueIn` or `outcomes` (`AXC0004`).
- A task has `label`, `assignee`, `form` and at least one outcome, and no
  `branches`, `otherwise`, `operation`, `set`, `connector`, `input`,
  `output`, `retry`, `event`, `fields` or `next` (`AXC0004`).
- An `end` step has no `next`, `branches`, `otherwise`, `operation`, `set`,
  `connector`, `input`, `output`, `retry`, `event`, `fields`, `wait`, `until`
  or any task property (`AXC0004`).
- A timer has `next`, and no `branches`, `otherwise`, `operation`, `set`,
  `connector`, `input`, `output`, `retry`, `event`, `fields` or any task
  property (`AXC0004`). It has exactly one of `wait` and `until` (`AXC0250` at
  `/steps/{i}`). A `wait` is a positive fixed-length duration, by the rule of a
  task's `dueIn` (`AXC0084` at `/steps/{i}/wait`). An `until` gives a
  `date-time`, with the same scope as a `when`. A problem is reported at
  `/steps/{i}/until` with its expression code, such as `AXC0048` for text.
- A task's `assignee` has exactly one of `user` and `role` (`AXC0081` at
  `/steps/{i}/assignee`). A `role` is a non-empty name (`AXC0004`). It must
  name a declared [role](roles-and-policies.md), compared
  exactly, otherwise it is `AXC0089` at `/steps/{i}/assignee/role`. A name
  whose role file was not loaded because of its own errors is not reported
  again.
- A `user` expression gives `text`, with the same scope as a `when`. A
  problem is reported at `/steps/{i}/assignee/user` with its expression code,
  such as `AXC0048` for a number.
- A task's `form` names a loaded form (`AXC0082`) whose entity is the subject
  entity (`AXC0083`). Each is reported at `/steps/{i}/form`.
- A task's `dueIn`, when set, is a positive ISO 8601 duration in whole
  numbers: either weeks, such as `P2W`, or days with an optional time part
  of hours, minutes and seconds, such as `P3D`, `PT4H` or `P1DT12H`. Years
  and months are not allowed until calendar durations come with the
  [time zone rule](../decisions/d22-current-time-and-time-zones.md) (`AXC0084` at `/steps/{i}/dueIn`).
- Outcome names are unique within the step, ignoring letter case (`AXC0085`
  at `/steps/{i}/outcomes/{j}/name`).
- The `startCondition` message text key exists, checked like other labels
  (`AXC0028`). So do the text keys of each task `label` and each outcome
  `label`.
- Every expression gets the usual syntax, type and cost diagnostics, see
  [Diagnostics](expressions.md#diagnostics).

## Start endpoint

A start creates an instance for one subject record. A process without a subject
takes [parameters instead](#a-process-without-a-subject):

```http
POST /api/apps/{app}/processes/{process}/instances
Content-Type: application/json
Idempotency-Key: 3f0c…

{ "subjectId": "0192f4a7-…" }
```

The content type follows the same rules as the record API, see
[Create, update and delete](record-api.md#create-update-and-delete). Errors
are problem details, as in the [record API](record-api.md#errors).

- **`201`.** The instance was started. The body is the instance:
  `{ "id", "process", "subjectId", "releaseId", "state" }`.
- **`401`.** Nobody is signed in. The title is the record API's "Sign in to
  read or write this data.".
- **`404`.** An unknown application, an application with no active release,
  or an unknown process.
- **`403`.** The user has no `start` grant on the process. The title is the
  record API's "You have no grant for this action.".
- **`400` for a body.** A body that is not `{ "subjectId": "<uuid>" }`, or a
  `subjectId` that names no record of the subject entity the user may read.
  The error is keyed `/subjectId`.
- **`400` for a start condition.** The subject record fails the
  `startCondition`. The error is keyed `/subjectId` and holds the condition's
  message text key.
- **`409`.** A `running` or `waiting` instance already exists for this
  process and subject record.
- **`409` for a policy that reads a pending field.** A `read` filter of the
  user's grants on the subject entity reads a
  [pending field](migrations.md#recomputing-computed-fields). The title is the
  record API's "Your access to this entity cannot be checked while a field is
  being recomputed.", with no `errors`, see
  [the record API](record-api.md#errors). It comes before the subject record
  is looked up, so it is the same for a record that does not exist.
- **`422`.** The `Idempotency-Key` was used before with a different subject
  record.
- **`503`.** The application's active release changed on each of three
  attempts, found under the [start lock](#releases-and-running-work). The
  server already ran the start again on the new release each time. Nothing was
  written, and the client may send the request again. The title is fixed:
  "The application's active release changed while the request ran. Try again."

The `201` has no `Location` header. Reading an instance needs an `inspect`
grant, which the user who started it may not hold. See the
[instance detail](process-inspection.md#instance-detail).

A start needs a session, or a bearer token of a
[service account](security.md#service-accounts), a `start` grant on the process
and read access to the subject record, as
[Authorization](security.md#authorization) describes. A request with a bearer
token needs no CSRF header, and its caller id is the actor of the
`process.started` audit record. A bad token is a `401` with
`WWW-Authenticate: Bearer`.

- **Subject the user may not read.** A subject record the user may not read
  does not exist for them. That is a record outside every filter of their
  `read` grants on the subject entity, or any record when they hold no
  `read` grant on it. It gets the same `400` keyed `/subjectId` as a
  `subjectId` that names no record, so the user cannot tell the two apart.

A request is checked in this order, and the first failure is the response:

1. The session or the bearer token, `401`.
2. The application and the process, `404`.
3. The `start` grant on the process, `403`.
4. The content type, `415`.
5. The body, `400`.
6. The `Idempotency-Key` header, `400`.

A start by a session without the CSRF header is a `403` before any of these checks. Read
access to the subject record is checked in the transaction, when the record
is read.

One transaction then does the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls
   back and answers from the stored receipt, or with the `422`.
2. It answers the policy `409` when a `read` filter of the user reads a
   pending field, whatever the record. A repeat with a used key has already
   been answered by step 1. Then it locks the subject record, checks that the
   user may read it, and reads it. A missing one, or one the user may not read, is the `400`. The lock
   makes the start condition see the latest version, and an edit of the
   record waits until the start commits or rolls back.
3. It evaluates the `startCondition` on the record, with any pending computed
   field computed by the active release, and a failure is the `400`.
4. It inserts the instance as `running` at revision 1, pinned to the release
   that was active when the request was checked. Its next step is the
   process's `start` step. An instance already running or waiting is the
   `409`.
5. It inserts the instance's first work item.
6. It writes the audit record `process.started` for the subject record, with
   details `{ "processId", "releaseId" }`. The actor is the
   [current user](task-api.md) with their display name.

When any part fails, nothing is written. The instance then waits for the
worker to run its steps.

An [endpoint call](inbound-endpoints.md) starts its process on the
record it creates by steps 3 to 6, in the call's transaction. Its actor is the
caller, and its details add the endpoint's name.

**`Idempotency-Key`.** The header is optional.

- A value must be non-empty and at most 255 characters, otherwise the
  response is `400`.
- Keys are scoped per application and process.
- A repeat with the same key and the same subject record returns the stored
  `201` status and body. No second instance starts.
- The same key with a different subject record is `422`.
- Only a `201` is stored. A rejected start can be retried with the same key
  after the record is fixed.
- Concurrent repeats are serialized by the receipt's unique key, so only one
  of them starts an instance.

### A process without a subject

A [process without a subject](process-flow.md#processes-without-a-subject)
takes its typed parameters instead of a record id:

```http
POST /api/apps/{app}/processes/{process}/instances
Content-Type: application/json

{ "parameters": { "days": 31 } }
```

The checks run in the order above, and so do the answers. The differences are
these:

- **`201`.** The body has the same keys, and `subjectId` is `null`. The
  instance stores the parameter values.
- **`400` for a body.** The body must be exactly one `parameters` property,
  and its value must be an object. Anything else is one error keyed
  `/parameters`.
- **`400` for a parameter.** Each declared parameter must be given once, by its
  exact name, letter case included, with a value of its type, read as a record
  field value of that type is. A missing, unknown, repeated, `null` or
  mistyped parameter is an error keyed `/parameters/{name}`. All the errors
  are in one response.
- **`400` for a start condition.** The `startCondition` is checked over the
  parameters. A failure is an error keyed `/parameters` that holds the
  condition's message text key.
- **`409`.** A `running` or `waiting` instance of the process exists. The title
  is "A running or waiting instance of this process already exists.".
- **`403`.** The user needs the `start` grant only. There is no record, so no
  `read` check runs and there is no policy `409`.
- **`422`.** The `Idempotency-Key` was used before with other parameter values.
  The receipt stores a null subject, and the values are compared with those
  stored on the receipt's instance. Decimals count as equal when they are the
  same number.

## Instance states

| State | Meaning |
| --- | --- |
| `running` | The instance has work that is ready or claimed by a worker. |
| `waiting` | The instance waits for a human task to be completed or for a connector call to settle, see [Connector calls](process-execution.md#connector-calls). It also waits for a package operation call, see [Package operations](process-execution.md#package-operations), and for a timer, see [Timer step](process-flow.md#timer-step). _(Planned for M14)_ It also waits for an event or a sub-process, see [process flow](process-flow.md). |
| `completed` | The instance reached an `end` step. |
| `failed` | A step failed. The error is in the history. |
| `cancelled` | An operator cancelled the instance while it was `running` or `waiting`. It is an end state. See [Cancel](process-inspection.md#cancel). |

Only `running` and `waiting` block a new start for the same process and
record. A `failed` or `cancelled` instance does not, so it never locks its
record. An operator can cancel a `running` or `waiting` instance, see
[Cancel](process-inspection.md#cancel). An operator can retry a `failed` instance, see [Retry](process-inspection.md#retry).

## Task states

| State | Meaning |
| --- | --- |
| `open` | The task waits for a decision. |
| `completed` | The decision is recorded, and the instance moves on. |
| `cancelled` | An operator cancelled the task's instance. The task has no decision and leaves every inbox. See [Cancel](process-inspection.md#cancel). |
| `expired` | The task's `deadline` passed while it was `open`, and the instance moved to the deadline's `next` step. The task has no decision and leaves every inbox. It cannot be completed. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline). |

A task has no other state. Its due date is only shown, so there is no overdue
state. _(Planned for M14)_ The task API reports an open task past its due date
as overdue, see [Task states](task-deadlines.md#task-states).

## Releases and running work

New work starts on the active release. Running work stays on its pinned
release until it ends, including its tasks, forms, texts and package versions.
Roles and policies come from the active release. This
section records [D29](../decisions/d29-release-pinning-and-text-resources.md).

The table lists the release each endpoint and command uses.
**Pinned** means the instance's or task's release, or the release id requested
for texts. **Both** means different parts use different releases.

| Endpoint or command | Release | Notes |
| --- | --- | --- |
| Record API | Active | Reads and writes use the release resolved when the request begins. |
| Data source endpoint | Active | The query and its policies come from the active release. |
| Process start endpoint | Active | The new instance is pinned to the active release at its start. |
| Inbound endpoint | Active | Creates the record and starts its instance on the active release. |
| Site, site list and page endpoints | Active | Describe sites and pages of active applications. |
| Texts endpoint without `release` | Active | Serves the active release's texts. |
| Texts endpoint with `release` | Pinned | Serves any stored release of the site's application. An unknown id, a non-UUID or another application's release answers `404`. |
| Task list | Both | Step and subject labels use the task's release. Their texts load from it too. Policies and roles come from the active release. |
| Task read | Both | Form, outcomes and field definitions use the task's release. Texts use it too. Policies come from the active release. |
| Task completion | Both | Form, outcomes and the record write use the pinned release. Data change triggers come from the active release. |
| Instance list | Active | Roles and policies come from the active release. |
| Instance detail | Both | The graph and the step names use the instance's release. So do the fields a call's mapping reads or writes. Roles and policies come from the active release, and so do the fields the user may read. The `sensitive` flag counts when the pinned release or the active release marks the field. See [Values and masking](process-inspection.md#values-and-masking). |
| Worker steps (`decision`, `task`, `operation`, `updateRecord`, `end`) | Pinned | Step definitions, expressions, rules and record writes use the instance's release. |
| `connector.call` and `package.call` | Pinned | Calls use the instance's release, including its pinned package version. Deployment binding values are resolved at run time. |
| Operator retry | Pinned steps, active policies | The instance runs its steps on its pinned release. The `retry` grant comes from the active release. |
| Operator resolve | Both | The output check and the record write use the pinned release. The `resolve` grant and data change triggers come from the active release. |
| `raiseEvent` | Both | The occurrence holds the pinned release. Its `sync` triggers come from the active release. |
| `event.deliver` | Active | Reads `async` event triggers from the release active at delivery. |
| Data change triggers | Active | Fire from the active release, even when old-release work writes the record. |
| Schedule fan-out | Active | Uses the release active when the fan-out runs, and pins each new instance to it. |
| Server and worker start check | Both | Rebuilds every active release and every release with a running or waiting instance. A missing pinned package version is `AXC0146` and stops the start. |

- **Starts from old work.** A data change or event trigger set off by
  old-release work starts a new instance on the active release. For writes,
  the record is read again with the active model before its triggers run.
  See [Starts from triggers](process-events.md#starts-from-triggers).
- **Start lock** *(built)*. Each
  start takes `pg_advisory_xact_lock_shared` keyed on the application `id`,
  before it reads the active release, in the transaction that inserts the
  instance. It is the first statement of that transaction. This covers the
  process start endpoint, the inbound endpoint, record API creates and
  updates, task completion, event delivery, `sync` event triggers, update
  triggers from worker steps and schedule fan-out. An activation with a
  pending migration takes the same lock exclusively, after the schema lock,
  and holds it to commit. Starts then read the new release. An activation
  with no pending migration never takes it. A server request that finds a
  different active release under the lock runs again on the new release. The
  process start endpoint, the [inbound endpoint](inbound-endpoints.md#responses),
  task completion and record creates and updates answer `503` after three
  release changes. A record delete takes the lock shared too, and the subject's row lock decides between a
  start and a delete of the same record. Operator [retry](process-inspection.md#retry) takes it too, as the first
  statement of its transaction, and answers `503` after three release changes.
  [Resolve](process-inspection.md#resolve) takes it the same way. Event delivery reads the active release again in each
  trigger's transaction, and skips a trigger that the new release no longer
  has. See [Migrations](migrations.md#migrations).
- **Authorization.** Roles and policies always come from the active release,
  including for older tasks. The assignee check stays unchanged. The
  subject's display field comes from the task's release, but the active
  policies decide whether it is readable. Removing or renaming a policy can
  deny access to an old task's subject record or fields.
- **Pinned texts.** Each release carries its own texts.
  The inbox and task page load them using the task's `releaseId`. Old texts
  stay readable by id for as long as the release is stored, even after its
  instances end. A newer release may remove a key its labels no longer use.
  Every key used by a label must still exist in every locale of that release,
  as `AXC0027` and `AXC0028` already check.

### Walkthrough: a waiting purchase request

This walkthrough shows full release pinning in the
[purchase request sample](../../samples/apps/purchase-requests/sites/purchasing.json).
R1 and R2 stand for stored release UUIDs of the same application.

Release R1 has `FinanceThreshold()` = 10000, and the text key
`purchaseRequest.sections.decision` reads "Decision" in English.
PR-2026-00042 has a total of 12000 and waits on `managerApproval` in R1.
Release R2 then becomes active. It replaces that text key with a new key in
its form and texts, and raises `FinanceThreshold()` to 15000.

1. The manager's inbox row shows R1's step label, loaded from R1's texts.
   Its subject label uses R1's display field, if R2's policies allow it.
2. The task read returns `releaseId` R1 and R1's form. Its fields are
   filtered by R2's policies, so a tightened field grant applies at once.
3. The task page loads `GET /api/sites/purchasing/texts/en?release=<R1>`.
   The section title still reads "Decision", even though R2 removed its key.
4. When the manager approves, `financeCheck` runs on R1. The waiting
   request's finance check uses R1's threshold: 12000 >= 10000. It goes to
   `financeApproval`, which is also on R1.
5. A new request for 12000 submitted after R2 becomes active starts on R2.
   After manager approval, 12000 < 15000 sends it to `markApproved`.
6. After the R1 instance ends, `?release=<R1>` still answers with R1's
   texts, because R1 is still a stored release of the site's application.

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
  _(Planned for M14)_ The table gains `parent_instance_id` and `parent_step`.
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
  the `event_id` of the occurrence. Every item also has the `trace_id` and
  `span_id` of the span whose transaction queued it, or null when no span was
  current. A run starts a new trace with a link to that span, see
  [Telemetry](../architecture.md#telemetry). A rescheduled item keeps them.
  Activation that queues a `trigger.schedule` item again replaces them.
  A `process.timer` item resumes one instance that waits on a `timer` step. It
  has the `process_instance_id` of the instance and the `revision` the timer
  step left, and its `due_at` is the time of the timer. See
  [Timer step](process-flow.md#timer-step).
  Two more kinds come with [schedules](process-events.md#schedules), one with
  [task deadlines](task-deadlines.md), and one with
  [computed fields](migrations.md#recomputing-computed-fields):
  - `trigger.schedule` runs the occurrences of one schedule trigger. Its item
    also has the `trigger_id`, and its `due_at` is the next occurrence. A
    partial unique index allows one such item for each trigger.
  - `trigger.fanOut` starts the process for the records of one occurrence.
    Its item also has the `trigger_id`, the `release_id` that was active at
    the occurrence and the `occurrence_at` time. Its `cursor` is the last
    record id it handled, and is null before its first batch.
  - `task.deadline` expires one task at its deadline. Its item also has the
    `task_id` of the task and the `process_instance_id` of its instance, and
    its `due_at` is the task step's time plus the deadline's `after`. See
    [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
  - `computed.backfill` recomputes the out-of-date rows of
    one entity. Its item also has the `entity_id`, and each claim runs one
    batch of up to 500 rows. See
    [Recomputing computed fields](migrations.md#recomputing-computed-fields).
- `axis.process_start_receipts`: the stored `201` response of each
  `Idempotency-Key`, unique per application, process and key. The subject is
  null for a process without a subject.
- `axis.endpoint_receipts`: the record and instance of each endpoint call's
  `Idempotency-Key`, unique per application, endpoint, caller and key, with a
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
- `axis.events`: one row per event occurrence, see
  [Raise event step](process-events.md#raise-event-step). Rows are append-only and kept, even
  when no trigger listens. An index on `process_instance_id` finds the
  events an instance raised.

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

## Limits in M3

- **Loops only through a task.** A process may not contain a cycle that has no
  `task` step, because it would run for ever. A cycle through a `task` step is
  allowed, see [Loops](process-flow.md#loops). _(Planned for M14)_ A cycle
  through a `timer` or `waitEvent` step is allowed too.
- **No user check.** The user id that a `user` assignee gives is not checked
  against any list of users. A wrong id creates a task that nobody sees. A
  [reassign](task-deadlines.md#reassign) checks its target against the
  [user profiles](notifications.md#user-profiles), but the `user` expression
  stays unchecked.
