# Processes

Detailed reference for processes: the resource shape, the steps, the compile
checks, the start endpoint, the instance and task states, the task API,
execution, the worker settings and the tables. The `process` resource kind is
built with `decision`, `operation` and `end` steps, and so are their
[compile checks](#compile-checks). A compiled process is part of the release
model. The worker host, its claims (see [Execution](#execution)), the
[worker settings](#worker-settings), the [start endpoint](#start-endpoint) and
the `axis.process_instances`, `axis.process_step_history`,
`axis.process_start_receipts` and `axis.process_work_items` tables are built
too. So is the engine that runs `decision`, `operation` and `end` steps, see
[Running a step](#running-a-step). Everything else here is
*(planned for M3)*: `task` steps, the task API and the other tables.
Until `task` is built, a step of that type is `AXC0004`, so the purchase
request example below does not compile yet.

Dn refers to [decisions.md](../decisions.md). The design follows
[D11](../decisions.md#d11-durable-process-engine--agreed) and
[D19](../decisions.md#d19-process-resource-and-worker-host--agreed), and human
tasks follow
[D20](../decisions.md#d20-human-tasks-task-inbox-and-forms--agreed). The
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

The purchase request approval needs one rule and one process. The finance
threshold is a `rule`, so it is configured, not hard-coded:

```json
{
  "id": "c527e0a8-2df0-4c8c-9d5b-190692edb970",
  "kind": "rule",
  "name": "NeedsFinanceReview",
  "formatVersion": 1,
  "parameters": [{ "name": "total", "type": "decimal" }],
  "resultType": "boolean",
  "expression": "total >= 10000"
}
```

The process submits the request, asks the manager, asks finance at or above
the threshold, and ends approved, returned or rejected:

```json
{
  "id": "2fd8ef90-f17e-437a-8cbd-ae1135e12f68",
  "kind": "process",
  "name": "PurchaseRequestApproval",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "startCondition": {
    "expression": "status is null or status in ('draft', 'returned')",
    "message": { "textKey": "purchaseRequest.cannotSubmit" }
  },
  "start": "submit",
  "steps": [
    {
      "name": "submit",
      "type": "operation",
      "operation": "updateRecord",
      "set": { "status": "'submitted'" },
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
      "branches": [{ "when": "NeedsFinanceReview(totalAmount)", "next": "financeApproval" }],
      "otherwise": "markApproved"
    },
    {
      "name": "financeApproval",
      "type": "task",
      "label": { "textKey": "purchaseRequest.financeApproval" },
      "assignee": { "role": "finance" },
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

M3 has four step types. Wait for event, timer and sub-process come later.

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
    Queues come later.
  - `form` names a [form resource](frontend.md) whose entity is the subject
    entity. The task page shows it over the subject record.
  - `dueIn` is optional. It is a positive ISO 8601 duration, such as `P3D`.
    The task's due date is the time the task is created plus `dueIn`. It is
    shown only. Escalation needs timers, so it comes later.
  - `outcomes` is an array of `{ "name", "label": { "textKey" }, "next" }`,
    such as `approve`, `return` and `reject`. Each names the step taken when
    the task is completed with it.

  The step's transaction evaluates the assignee, creates the task as `open`
  and sets the instance to `waiting`. A `user` expression that gives `null`
  fails the step. Completing the task resumes the instance, see
  [Task API](#task-api).
- **`operation`.** M3 has one built-in operation, `updateRecord`. Its `set`
  maps fields of the subject record to expressions, and `next` names the step
  that follows. The step writes the values with the record update rules, so
  computed fields are recomputed and validations run, in the step's
  transaction. A validation failure fails the step. Operations from extension
  packages and external calls come in M5.
- **`end`.** The instance is `completed` when it reaches an `end` step. An
  `end` step has no `next`.

## Compile checks

The compiler reports these as diagnostics. The checks for `decision`,
`operation` and `end` steps are built. The rest come with `task` steps
*(planned for M3)*. Every problem of a process is reported in one pass. See
[the Resolve step](configuration.md#configuration-pipeline) for the details.

- `entity` names a loaded entity (`AXC0067`) that is not a child entity
  (`AXC0068`).
- Step names are unique within the process, ignoring letter case
  (`AXC0069`).
- `start`, every branch `next`, `otherwise` and the `next` of an operation
  name a step of the process, ignoring letter case (`AXC0070`). Outcome
  targets join this check *(planned for M3)*.
- Every step is reachable from `start` (`AXC0071`).
- Every step has a path to an `end` step (`AXC0072`).
- The steps form no cycle (`AXC0073`). See [Limits in M3](#limits-in-m3).
- A decision has at least one branch (`AXC0004`).
- Each `when` and the `startCondition` expression give a boolean. They see
  the subject entity's fields, computed ones included, its child collections
  through aggregates, the named rules, and paths through reference fields
  such as `department.manager.name`. A path takes at most 3 hops
  (`AXC0058`), and a `.` after a field that is not a reference is `AXC0047`.
  See [Scope in a process expression](expressions.md#names-and-references).
- An operation names `updateRecord`, matched exactly as a step `type` is
  (`AXC0078` at `/steps/{i}/operation`).
- Each key of an `updateRecord` `set` names a field of the subject entity,
  ignoring letter case, that no earlier key of the step names (`AXC0079`).
  The field is not computed, not numbered by a sequence and not a child
  collection (`AXC0080`). Each is reported at `/steps/{i}/set/{field}`.
- Each `set` expression fits its field's type, with the same scope as a
  `when`. A problem is reported at `/steps/{i}/set/{field}` with its
  expression code, such as `AXC0048` for a value of the wrong type.
- An operation has `operation`, a `set` with at least one field and `next`,
  and no `branches` or `otherwise`. A decision has no `operation`, `set` or
  `next` (`AXC0004`).
- An `end` step has no `next`, `branches`, `otherwise`, `operation` or `set`
  (`AXC0004`).
- *(planned for M3)* A task's `assignee` has exactly one of `user` and
  `role`. A `user` expression gives `text`. A `role` is a non-empty name.
- *(planned for M3)* A task's `form` names a loaded form whose entity is the
  subject entity.
- *(planned for M3)* A task's `dueIn`, when set, parses as a positive ISO 8601
  duration.
- *(planned for M3)* A task has at least one outcome. Outcome names are
  unique within the step, ignoring letter case.
- The `startCondition` message text key exists, checked like other labels
  (`AXC0028`).
  *(planned for M3)* So do the text keys of each task `label` and each
  outcome `label`.
- Every expression gets the usual syntax, type and cost diagnostics, see
  [Diagnostics](expressions.md#diagnostics).

## Start endpoint

A start creates an instance for one subject record:

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
- **`404`.** An unknown application, an application with no active release,
  or an unknown process.
- **`400` for a body.** A body that is not `{ "subjectId": "<uuid>" }`, or a
  `subjectId` that names no record of the subject entity. The error is keyed
  `/subjectId`.
- **`400` for a start condition.** The subject record fails the
  `startCondition`. The error is keyed `/subjectId` and holds the condition's
  message text key.
- **`409`.** A `running` or `waiting` instance already exists for this
  process and subject record.
- **`422`.** The `Idempotency-Key` was used before with a different subject
  record.

The `201` has no `Location` header, because there is no endpoint to read an
instance yet. Starts have no authorization check until M4, as with record
writes.

A request is checked in this order, and the first failure is the response:

1. The application and the process, `404`.
2. The content type, `415`.
3. The body, `400`.
4. The `Idempotency-Key` header, `400`.

One transaction then does the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls
   back and answers from the stored receipt, or with the `422`.
2. It reads the subject record, and a missing one is the `400`.
3. It evaluates the `startCondition` on the record, and a failure is the
   `400`.
4. It inserts the instance as `running` at revision 1, pinned to the release
   that was active when the request was checked. Its next step is the
   process's `start` step. An instance already running or waiting is the
   `409`.
5. It inserts the instance's first work item.
6. It writes the audit record `process.started` for the subject record, with
   details `{ "processId", "releaseId" }`. The actor is the signed-in test
   user, or `anonymous`.

When any part fails, nothing is written. The instance then waits for the
worker to run its steps.

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

## Instance states

| State | Meaning |
| --- | --- |
| `running` | The instance has work that is ready or claimed by a worker. |
| `waiting` | The instance waits for a human task to be completed. |
| `completed` | The instance reached an `end` step. |
| `failed` | A step failed. The error is in the history. |

Only `running` and `waiting` block a new start for the same process and
record. A `failed` instance does not, so it never locks its record. Cancel
comes in M7.

## Task states

| State | Meaning |
| --- | --- |
| `open` | The task waits for a decision. |
| `completed` | The decision is recorded, and the instance moves on. |

A task has no other state. Its due date is only shown, so there is no overdue
state. Cancel comes in M7.

## Task API

The task API lets the signed-in user list their open tasks, read one task and
complete it with an outcome. The user is the signed-in
[test user](../architecture.md#development-test-users) until
sign-in arrives in M4.

**Who may act.** The user may act on a task when they are its assignee, or
when the task's assignee is a role and the user holds it. In M3 a user's
roles are the role names of their test user. This is checked on
every request, at the moment of action. Policies in M4 add to this check and
never replace it.

**List my tasks.** `GET /api/apps/{app}/tasks?page=&pageSize=` lists the
`open` tasks the user may act on, whatever release their instance is pinned
to. `page` and `pageSize` follow the record API's
[paging rules](record-api.md#paging-and-sorting). There is no `sort` or
`search`. Tasks are ordered by `dueAt` ascending with no due date last, then
by `createdAt`, then by `id`.

```json
{
  "items": [
    {
      "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
      "process": "PurchaseRequestApproval",
      "step": "managerApproval",
      "labelKey": "purchaseRequest.managerApproval",
      "subject": {
        "entity": "PurchaseRequest",
        "id": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
        "label": "PR-2026-00042"
      },
      "dueAt": "2026-10-13T02:05:00Z",
      "createdAt": "2026-10-10T02:05:00.123456Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

- `subject.label` is the value of the subject entity's display field, as the
  record API's `labels` give it.
- `dueAt` is `null` when the step has no `dueIn`.

**Read one task.** `GET /api/apps/{app}/tasks/{id}` returns one task in any
state:

```json
{
  "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
  "process": "PurchaseRequestApproval",
  "instanceId": "0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b",
  "step": "managerApproval",
  "labelKey": "purchaseRequest.managerApproval",
  "state": "open",
  "assignee": { "user": "maria", "role": null },
  "subject": {
    "entity": "PurchaseRequest",
    "id": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
    "label": "PR-2026-00042"
  },
  "form": {
    "name": "PurchaseRequestReview",
    "sections": [
      {
        "titleKey": "purchaseRequest.sections.decision",
        "fields": [{ "name": "decisionComments", "readOnly": false }]
      }
    ],
    "entity": { "name": "PurchaseRequest", "fields": [] }
  },
  "outcomes": [
    { "name": "approve", "labelKey": "task.approve" },
    { "name": "return", "labelKey": "task.return" },
    { "name": "reject", "labelKey": "task.reject" }
  ],
  "dueAt": "2026-10-13T02:05:00Z",
  "createdAt": "2026-10-10T02:05:00.123456Z",
  "completedAt": null,
  "completedBy": null,
  "outcome": null
}
```

- `assignee` has the user id or the role name, and the other is `null`.
- `form` is the step's form in the task's release, so a task pinned to an
  earlier release keeps its own layout. It has the shape of a form widget's
  `form` metadata, plus `entity` in the shape of a widget's `entity`
  metadata, so the task page knows each field's type. See
  [frontend](frontend.md). The example shortens both.
- `completedAt`, `completedBy` and `outcome` are `null` while the task is
  `open`.

**Complete a task.** `POST /api/apps/{app}/tasks/{id}/complete` records the
decision:

```http
POST /api/apps/{app}/tasks/{id}/complete
Content-Type: application/json

{ "outcome": "approve", "values": { "decisionComments": "Fine for Q4." }, "version": 3 }
```

- `outcome` is required. It names one of the step's outcomes, ignoring
  letter case.
- `values` is optional. It holds only fields the task's form makes editable,
  in the record API's [value format](record-api.md#request-bodies-and-values).
- `version` is the subject record's version, as the record API reads it. It
  is required when `values` is not empty.
- The response is `200` with the completed task, in the shape of a single
  read.

### Task API errors

Errors are problem details, as in the [record API](record-api.md#errors).
The titles are fixed and never contain text from the request. The content
type follows the record API's
[rules](record-api.md#create-update-and-delete), so a wrong one is a `415`.

A request is checked in this order, and the first failure is the response:
`401`, then `404`, then `403`, then `409`, then `415`, then `400`. So a user
who may not act on a task never learns its state.

- **`401`.** No test user is signed in. Every task route needs one.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
    "title": "Sign in to work on tasks.",
    "status": 401
  }
  ```

- **`404`.** An unknown application, an application with no active release,
  an unknown task, or an `{id}` that is not a UUID in the hyphenated form.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
    "title": "No task exists with this id.",
    "status": 404
  }
  ```
- **`403`.** The user is not the assignee and does not hold the assignee
  role. This applies to reading one task as well as completing it.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
    "title": "This task is assigned to someone else.",
    "status": 403
  }
  ```

- **`409` for a decided task.** The task is not `open`, because another
  completion came first. This covers a later completion and the loser of two
  concurrent ones.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    "title": "This task is already completed.",
    "status": 409
  }
  ```

- **`400`.** The body is a validation problem keyed by JSON Pointer. Every
  invalid part is reported in the same response.
  - A missing outcome, or one the step does not have, is keyed `/outcome`.
  - A value for a field the form does not make editable is keyed
    `/values/<field>` with the message "Cannot be set.".
  - A missing `version` when `values` is not empty is keyed `/version`.
  - The values are then checked by the record update command. Its parse
    errors and entity validations use the record API's keys and messages.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "One or more validation errors occurred.",
    "status": 400,
    "errors": {
      "/outcome": ["Must be one of the step's outcomes."],
      "/values/totalAmount": ["Cannot be set."]
    }
  }
  ```

- **`409` for a stale version.** The subject record's `version` is not the
  stored one. It is keyed `/version`. It is found when the values are
  written, so after the body checks, as in the record API.

### Completing a task

One transaction records the decision:

1. It locks the task row and checks that the task is still `open`.
2. It writes `values`, when there are any, through the record update command,
   so computed fields are recomputed and validations run.
3. It sets the task's `outcome`, `completedBy` and `completedAt`, and its
   state to `completed`.
4. It writes the step history and the audit record `task.completed`, with
   details `{ "outcome", "fields" }`. `fields` names the changed fields and
   holds no values.
5. It sets the instance to `running` with a new revision, and inserts the
   work item for the outcome's `next` step.

When any part fails, nothing is written. The worker then runs the next step
as usual.

## Execution

- **Worker host.** `Axis.Worker` runs the steps, not the server. Each work
  item carries its tenant id, and the worker sets the tenant context from it.
- **One transaction per step.** A step commits these together: business
  writes, the new instance state and revision, the step history, audit
  records and the next work item. A revision mismatch aborts the transaction.
- **Claims.** A worker claims ready work with `FOR UPDATE SKIP LOCKED`. It
  sets a lease and a claim token on the work item. The step's commit checks
  that the token is still the worker's. When a lease expires, another worker
  can claim the item.
- **Failure.** A failing step rolls back. A following transaction records the
  error in the history and sets the instance to `failed`. Retry policies come
  in M5, and operator retry in M7.
- **Revision.** An instance's revision is a counter that starts at 1 and goes
  up each time the instance changes. A step changes the instance only at the
  revision it loaded.
- **Work item errors.** When a work item's handler throws, its writes roll
  back. A following transaction, still checked against the claim token,
  deletes the item and calls the handler's failure callback with the error.
  The item is not retried. A worker that crashes or stops before it commits
  calls no failure callback. Its item is claimed again after the lease
  expires.
- **History.** Every step occurrence records its input, its output, the
  decision taken and any error.

### Running a step

The worker runs the `decision`, `operation` and `end` steps. A `task` step
fails the instance until that type is built *(planned for M3)*. Each work
item of kind `process.step` runs the current step of its instance in one
transaction:

1. It loads the instance. An instance that is missing or not `running` is
   skipped, and the item is deleted with no other write.
2. It reads the release the instance is pinned to, so an instance keeps the
   step graph it started with after a new release is activated.
3. It reads the subject record and runs the step. An operation first locks
   the subject record's row, so it reads the latest stored version, and a
   user's write of the same record waits until the step's transaction ends.
   - A **decision** evaluates its branches in order and takes the first `when`
     that is true. A `when` that gives `null` counts as false. When no branch
     is true, it takes `otherwise`. The instance stays `running` on the next
     step.
   - An **operation** evaluates each `set` expression on the record. The
     instance stays `running` on its `next` step.
   - An **end** step sets the instance to `completed` with its `ended_at` time.
4. It sets the new state and step, with the next revision, only if the
   instance is still `running` at the revision it loaded. Otherwise another
   transaction changed the instance first. The step then writes nothing else,
   and its work item is deleted, so it never runs again.
5. An operation then writes the values the way a record update does. Each
   value must fit its field, and a `null` for a required field is refused.
   Computed fields are recomputed, validations run, and the record's version
   goes up by one. A refused write is an error, keyed and worded as in the
   [record API](record-api.md#child-rows-computed-fields-and-validations).
6. It writes the step's history row and its audit records, all with the actor
   `system`:
   - An operation first writes `record.updated` for the subject record, with
     the instance id. Its details are `{ "version", "fields" }`, as the
     [record API](record-api.md#audit-records-and-history) writes them.
   - A decision and an operation write `process.stepCompleted` with details
     `{ "step", "next" }`.
   - An end step writes `process.completed` with details `{ "step" }`.
7. A decision and an operation insert the work item of the next step.

Any exception in the step, such as a `when` that divides by zero, a missing
subject record or a record write that a validation refuses, rolls all of it
back, so the record keeps its values and version. The error of a refused write
names each field pointer and message, such as
`The record update was rejected: /values/amount: request.amountNegative`. A
following transaction then sets the instance to `failed` with its `ended_at`
time and the next revision, writes a history row with the error, and writes
the audit record `process.failed` with
details `{ "step" }` and the actor `system`. It does nothing when the instance
has changed since the step loaded it. No step is retried.

## Worker settings

The worker polls every tenant database in the `Tenants` section of its
configuration (see
[Tenant configuration](../architecture.md#tenant-configuration)).

| Setting | Default | Meaning |
| --- | --- | --- |
| `Worker:LeaseDuration` | `00:00:30` | How long a claim lasts before another worker can claim the item. |
| `Worker:PollInterval` | `00:00:01` | How long the worker waits before it polls again when it found no work. |

As environment variables they are `Worker__LeaseDuration` and
`Worker__PollInterval`.

The server migrates the tenant databases, and the worker does not. Before it
claims work for a tenant, the worker checks that the processes migrations are
applied to the tenant database. Until they are, it skips the tenant and logs
once that it is waiting. Once they are, it logs that the tenant is ready for
work and starts claiming. So the worker can start before the server.

## Tables

`Axis.Processes` owns these tables in the `axis` schema of each tenant
database, with history in `axis.__processes_migrations` (see
[storage](storage.md)):

- `axis.process_instances`: one row per instance, with its process, subject
  record, release, state, revision and `step`, the step it runs next. Its
  `ended_at` is the time it became `completed` or `failed`, and is null before.
  A partial unique index on
  application, process and subject record, where the state is `running` or
  `waiting`, enforces one start per submission.
- `axis.process_step_history`: one row per step occurrence. Times use the
  database clock.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `process_instance_id` | `uuid` | The instance |
  | `step` | `text` | The declared name of the step |
  | `revision` | `bigint` | The instance revision the step ran at |
  | `input` | `jsonb` | `{ "subjectId", "subjectVersion" }`: the subject record and the version the step read. It holds no field values |
  | `output` | `jsonb`, null | `{ "next" }` for a decision, `{ "next", "subjectVersion" }` for an operation, with the version it wrote, `{ "state": "completed" }` for an end step, null for a failed step |
  | `decision` | `text`, null | The branch a decision took: its zero-based index, such as `0`, or `otherwise`. Null for other steps and failed steps |
  | `error` | `text`, null | The error of a failed step |
  | `started_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `finished_at` | `timestamptz` | When the row was written, `clock_timestamp()` |
- `axis.process_work_items`: the ready work. Each item has an `id`, its
  `tenant_id`, its `kind`, its `due_at` time, and the `lease_expires_at` and
  `claim_token` of its current claim. A step's item also has the
  `process_instance_id` of its instance. A worker claims only items of the
  tenant it polls and only kinds it has a handler for. Lease times use the
  database clock. Completed and failed items are deleted.
- `axis.process_start_receipts`: the stored `201` response of each
  `Idempotency-Key`, unique per application, process and key.
- `axis.process_tasks`: one row per task, with its instance, step,
  application, assignee kind and value, form, due date, state, outcome, and
  who completed it and when.

## Limits in M3

- **No cycles.** A process may not contain a cycle. A loop with no task step
  would run for ever. A returned request starts a new instance, so M3 needs
  no loops.
- **No sign-in until M4.** Tasks cannot be completed in Production before
  sign-in arrives. Processes can be used only in Development and in tests in
  M3.
