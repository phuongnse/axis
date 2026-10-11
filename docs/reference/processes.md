# Processes

Detailed reference for processes: the resource shape, the steps, the compile
checks, the start endpoint, the instance and task states, the task API,
execution, the worker settings and the tables. The `process` resource kind is
built with `decision`, `task`, `operation` and `end` steps, and so are their
[compile checks](#compile-checks). A compiled process is part of the release
model. The worker host, its claims (see [Execution](#execution)), the
[worker settings](#worker-settings), the [start endpoint](#start-endpoint) and
the `axis.process_instances`, `axis.process_step_history`,
`axis.process_tasks`, `axis.process_start_receipts` and
`axis.process_work_items` tables are built too. So is the engine that runs
`decision`, `task`, `operation` and `end` steps, see
[Running a step](#running-a-step). The [task API](#task-api) routes that
list the user's tasks, read one task and complete one are built. Completing
a task resumes its `waiting` instance, see
[Completing a task](#completing-a-task). A start and the task API follow the
user's policies, see [Start endpoint](#start-endpoint) and
[Policies on tasks](#task-api). Calls to external systems through a
`callConnector` step are [planned for M5](#connector-calls).

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
approved, returned or rejected:

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
  - `dueIn` is optional. It is a positive ISO 8601 duration in whole
    numbers: either weeks, such as `P2W`, or days with an optional time part
    of hours, minutes and seconds, such as `P3D`, `PT4H` or `P1DT12H`. Years
    and months are `AXC0084` until calendar durations come with the
    [time zone rule](../decisions.md#d22-current-time-and-time-zones--agreed), because they have no fixed length. The task's due date is the time the task is created plus `dueIn`. It is
    shown only. Escalation needs timers, so it comes later.
  - `outcomes` is an array of `{ "name", "label": { "textKey" }, "next" }`,
    such as `approve`, `return` and `reject`. Each names the step taken when
    the task is completed with it.

  The step's transaction evaluates the assignee, creates the task as `open`
  and sets the instance to `waiting`. A `user` expression that gives `null`
  or blank text fails the step. Completing the task resumes the instance, see
  [Task API](#task-api).
- **`operation`.** M3 has one built-in operation, `updateRecord`. Its `set`
  maps fields of the subject record to expressions, and `next` names the step
  that follows. The step writes the values with the record update rules, so
  computed fields are recomputed and validations run, in the step's
  transaction. A validation failure fails the step. Operations from extension
  packages come in M5. *(planned for M5)* A second built-in operation,
  `callConnector`, calls an external system outside the step's transaction,
  see [Connector calls](#connector-calls).
- **`end`.** The instance is `completed` when it reaches an `end` step. An
  `end` step has no `next`.

## Compile checks

The compiler reports these as diagnostics. Every problem of a process is
reported in one pass. See
[the Resolve step](configuration.md#configuration-pipeline) for the details.

- `entity` names a loaded entity (`AXC0067`) that is not a child entity
  (`AXC0068`).
- Step names are unique within the process, ignoring letter case
  (`AXC0069`).
- `start`, every branch `next`, `otherwise`, the `next` of an operation and
  every outcome `next` of a task name a step of the process, ignoring letter
  case (`AXC0070`).
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
  `next`. Neither has a task's `label`, `assignee`, `form`, `dueIn` or
  `outcomes` (`AXC0004`).
- A task has `label`, `assignee`, `form` and at least one outcome, and no
  `branches`, `otherwise`, `operation`, `set` or `next` (`AXC0004`).
- An `end` step has no `next`, `branches`, `otherwise`, `operation`, `set` or
  any task property (`AXC0004`).
- A task's `assignee` has exactly one of `user` and `role` (`AXC0081` at
  `/steps/{i}/assignee`). A `role` is a non-empty name (`AXC0004`). It must
  name a declared [role](configuration.md#roles-and-policies), compared
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
  [time zone rule](../decisions.md#d22-current-time-and-time-zones--agreed) (`AXC0084` at `/steps/{i}/dueIn`).
- Outcome names are unique within the step, ignoring letter case (`AXC0085`
  at `/steps/{i}/outcomes/{j}/name`).
- The `startCondition` message text key exists, checked like other labels
  (`AXC0028`). So do the text keys of each task `label` and each outcome
  `label`.
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
- **`422`.** The `Idempotency-Key` was used before with a different subject
  record.

The `201` has no `Location` header, because there is no endpoint to read an
instance yet.

A start needs a session, a `start` grant on the process and read access to
the subject record, as [Authorization](../architecture.md#authorization)
describes.

- **Subject the user may not read.** A subject record the user may not read
  does not exist for them. That is a record outside every filter of their
  `read` grants on the subject entity, or any record when they hold no
  `read` grant on it. It gets the same `400` keyed `/subjectId` as a
  `subjectId` that names no record, so the user cannot tell the two apart.

A request is checked in this order, and the first failure is the response:

1. The session, `401`.
2. The application and the process, `404`.
3. The `start` grant on the process, `403`.
4. The content type, `415`.
5. The body, `400`.
6. The `Idempotency-Key` header, `400`.

A start without the CSRF header is a `403` before any of these checks. Read
access to the subject record is checked in the transaction, when the record
is read.

One transaction then does the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls
   back and answers from the stored receipt, or with the `422`.
2. It locks the subject record, checks that the user may read it, and reads
   it. A missing one, or one the user may not read, is the `400`. The lock
   makes the start condition see the latest version, and an edit of the
   record waits until the start commits or rolls back.
3. It evaluates the `startCondition` on the record, and a failure is the
   `400`.
4. It inserts the instance as `running` at revision 1, pinned to the release
   that was active when the request was checked. Its next step is the
   process's `start` step. An instance already running or waiting is the
   `409`.
5. It inserts the instance's first work item.
6. It writes the audit record `process.started` for the subject record, with
   details `{ "processId", "releaseId" }`. The actor is the
   [current user](#task-api) with their display name.

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
| `waiting` | The instance waits for a human task to be completed, or for a connector call *(planned for M5)*, see [Connector calls](#connector-calls). |
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
complete it with an outcome. The user is the user of the
[session](../architecture.md#sign-in-and-the-session)
([D23](../decisions.md#d23-sign-in-through-oidc-and-the-bff-session--agreed)).

**Who may act.** The user may act on a task when they are its assignee, or
when the task's assignee is a role and the user holds it. The user's roles
are the roles kept in their session. This is checked on every request, at
the moment of action. Policies add to this check and never replace it.

**Policies on tasks.** These follow
[D24](../decisions.md#d24-roles-and-policies--agreed).

- The assignee check is enough to list, read and complete a task. The user
  needs no grant on the subject entity, and its read filters do not apply.
- The task's `form` and its `entity` leave out the fields the user may not
  read on the subject entity. A user with no `read` grant on it reads no
  field. A form section left with no field is left out too.
- Which fields the user may read comes from the policies of the active
  release, so a grant change applies to open tasks at once. The form's
  layout still comes from the task's release. Fields are matched by name.
- A value for a field the user may not read is refused as a field the form
  does not make editable, with "Cannot be set.".
- Field write grants do not apply to the values of a task completion. The
  form decides which fields are editable.
- `subject.label` follows the record API's
  [label rule](record-api.md#record-shape): it shows when the user may read
  the subject entity's display field, and is `null` otherwise.

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
  record API's `labels` give it. It is `null` when the user may not read the
  display field.
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
- `values` is optional. It holds only fields the task's form makes editable
  and the user may read, in the record API's
  [value format](record-api.md#request-bodies-and-values).
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
who may not act on a task never learns its state. A completion without the
[CSRF header](record-api.md#create-update-and-delete) is a `403` before any
of these checks.

- **`401`.** Nobody is signed in.
  Every task route needs a signed-in user.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
    "title": "Sign in to work on tasks.",
    "status": 401
  }
  ```

- **`404`.** An unknown application, an application with no active release,
  an unknown task, or an `{id}` that is not a UUID in the hyphenated form. A
  task of another application is an unknown task. For an unknown application,
  or one with no active release, the title is "No application is active under
  this name." instead of the one below.

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
  - A value for a field the form does not make editable, or the user may
    not read, is keyed `/values/<field>` with the message "Cannot be set.".
  - A missing `version` when `values` is not empty is keyed `/version`.
  - Any other property, a repeated property and a body that is not a JSON
    object get the record API's keys and messages, such as "Unknown
    property.".
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

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    "title": "The record has changed since this version was read.",
    "status": 409,
    "errors": {
      "/version": ["Must be the record's current version."]
    }
  }
  ```

- **Other write failures.** The other storage outcomes of writing the values
  answer as in the record API's [errors](record-api.md#errors), such as a
  `409` for a unique value, or a `404` when the subject record no longer
  exists.

### Completing a task

One transaction records the decision:

1. It locks the task row and checks that the task is still `open`.
2. It writes `values`, when there are any, through the record update command,
   so computed fields are recomputed and validations run.
3. It sets the task's `outcome`, `completedBy` and `completedAt`, and its
   state to `completed`.
4. It sets the instance to `running` on the outcome's `next` step, with a
   new revision. The instance must be `waiting` on the task's step.
5. It writes the history row of the task step at the revision the instance
   waited at. Its `decision` is the outcome's declared name. Its input is
   `{ "subjectId", "subjectVersion" }`, with the version the values were
   written over, or `null` when there are no values. Its output is
   `{ "taskId", "outcome", "next" }`, plus `subjectVersion`, the version the
   values wrote, when there are any.
6. It writes the audit record `task.completed`, with the user and their
   display name as its actor, and details `{ "outcome", "fields" }`.
   `fields` names the changed fields in declaration order and holds no
   values. A completion writes no `record.updated`.
7. It inserts the work item for the outcome's `next` step.

The form, the outcomes and the record write all use the release the instance
is pinned to. When any part fails, nothing is written. The worker then runs
the next step as usual.

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
  error in the history and sets the instance to `failed`. No step is retried.
  *(planned for M5)* A `callConnector` step retries its call under its retry
  policy, see [Connector calls](#connector-calls). Operator retry comes in
  M7.
- **Revision.** An instance's revision is a counter that starts at 1 and goes
  up each time the instance changes. A step changes the instance only at the
  revision it loaded.
- **Work item errors.** When a work item's handler throws, its writes roll
  back. A following transaction, still checked against the claim token,
  deletes the item and calls the handler's failure callback with the error.
  The item is not retried. A worker that crashes or stops before it commits
  calls no failure callback. Its item is claimed again after the lease
  expires.
- **Restarts.** A `waiting` instance has no work item, so it needs no running
  worker. A task decision is accepted while no worker runs: it queues the
  next step's work item. The next worker to start runs that step. A step that
  a worker never committed leaves no trace, and the worker that claims it after
  the lease runs it once.
- **History.** Every step occurrence records its input, its output, the
  decision taken and any error.

### Running a step

The worker runs the `decision`, `task`, `operation` and `end` steps. Each work
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
   - A **task** evaluates its assignee. A `user` expression gives the user id,
     and one that gives `null`, blank text or a run-time error fails the step.
     A `role` is the role name. The instance becomes `waiting` on the task
     step.
   - An **end** step sets the instance to `completed` with its `ended_at` time.

   `now()` in any of these expressions, a task's `assignee.user` included,
   gives the start time of the step's transaction. That is the same instant
   the history row stores as `started_at`. A validation that the operation's
   write runs sees it too. See
   [D22](../decisions.md#d22-current-time-and-time-zones--agreed).
4. It sets the new state and step, with the next revision, only if the
   instance is still `running` at the revision it loaded. Otherwise another
   transaction changed the instance first. The step then writes nothing else,
   and its work item is deleted, so it never runs again.
5. A task step then inserts its task into `axis.process_tasks` as `open`. Its
   `due_at` is the transaction time, `now()`, plus `dueIn`, or null without
   `dueIn`. `dueIn` is added as a fixed length of time.

   An operation then writes the values the way a record update does. Each
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
   - A task step writes `task.created` with details `{ "step", "taskId" }`.
   - An end step writes `process.completed` with details `{ "step" }`.
7. A decision and an operation insert the work item of the next step. A task
   step inserts none, because the instance waits for the task.

Any exception in the step, such as a `when` that divides by zero, a missing
subject record, a record write that a validation refuses or a task assignee
that gives no user, rolls all of it back. So the record keeps its values and
version, and no task is stored. The error of a refused write
names each field pointer and message, such as
`The record update was rejected: /values/amount: request.amountNegative`. A
following transaction then sets the instance to `failed` with its `ended_at`
time and the next revision, writes a history row with the error, and writes
the audit record `process.failed` with
details `{ "step" }` and the actor `system`. It does nothing when the instance
has changed since the step loaded it. No step is retried.

## Connector calls

*(planned for M5)* This section follows
[D25](../decisions.md#d25-connectors-deployment-bindings-and-secrets--agreed).
None of it is built yet. A `callConnector` step calls one operation of a
[connector](configuration.md#connectors-and-deployment-bindings), an external
system such as the purchasing system. The call never runs inside a database
transaction. The step stages it, the worker sends it later, and a separate
transaction records the result. A stable operation identity lets every retry
and every lookup ask about the same call, so a lost response never leads to a
second purchase order.

### Step shape

After approval, the purchase request process creates a purchase order.
`markApproved` names the new step as its `next`:

```json
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
  "next": "done"
}
```

The sample files do not change until the build issues add connectors.

- `connector` names a connector and one of its operations.
- `input` maps operation inputs to expressions. Each key is an input of the
  operation. Each value is an expression over the subject record, with the
  same scope as a decision's `when`.
- `output` is optional. It maps operation outputs to subject fields. Each key
  is an output of the operation, and each value names a field of the subject
  record.
- `retry` is optional.
  - `attempts` counts the first attempt. It defaults to 3 and is at most 10.
  - `backoff` is an ISO 8601 duration, in the same form as a task's `dueIn`.
    It is the delay before the second attempt, and it doubles each attempt,
    up to 1 hour. It defaults to `PT30S`.
- `next` names the step taken when the call succeeds.

The compiler reports these as compile errors. Their codes come with the
build issue.

- The connector and its operation exist.
- Each `input` key names an input of the operation, at most once. Every
  required input has a key.
- Each `input` expression gives the input's type.
- Each `output` key names an output of the operation, at most once.
- Each `output` value names a field of the subject entity that an
  `updateRecord` `set` may name. The output's type fits the field.
- `attempts` is from 1 to 10, and `backoff` is a valid duration.
- A `callConnector` step has `connector`, `input` and `next`, and no `set`.
  An `updateRecord` step has none of `connector`, `input`, `output` and
  `retry`.

### Staging and the call

**The step's transaction stages the call.** It runs as other steps do, see
[Running a step](#running-a-step), and writes these together:

1. It evaluates each `input` expression on the subject record. A required
   input that gives `null` fails the step.
2. It inserts the call into `axis.connector_calls` as `pending`, with the
   evaluated input. The row's id is the call's operation identity, a
   version 7 UUID. A unique index on instance and step lets a step stage only
   one call.
3. It sets the instance to `waiting` on the step.
4. It writes the step's history row, with output
   `{ "state": "waiting", "operationId" }`.
5. It inserts a work item of kind `connector.call` for the call.

While its call is pending, retrying or unknown, the instance stays `waiting`.
So a second start for the same record is blocked, as with an open task.

**Each attempt.** A worker that claims the `connector.call` item does this:

1. **Mark.** A short transaction marks the call `sending` and counts the
   attempt. It extends the item's lease to the connector's `timeout` plus
   `Worker:LeaseDuration`, so no other worker claims the item while the call
   runs.
2. **Bindings.** The worker reads the connector's bindings from its host
   configuration. A missing binding, or an `http` URL outside Development,
   means the request is never sent.
3. **Send.** The worker sends the request outside any transaction.
   - The body is the stored input, as JSON. A `GET` sends no body.
   - With `idempotency`, the request carries the header
     `<idempotency>: <operationId>`, such as `Idempotency-Key: 0192f5c3-…`.
   - Redirects are not followed, and `Retry-After` is ignored.
4. **Record.** An outcome transaction records the result. Its commit checks
   the claim token, and it needs the instance still `waiting` on the step at
   its revision. Otherwise it writes nothing else, and the item is deleted.

A worker can crash after it sends and before it records. Its lease then
expires, and the next worker finds the call still `sending`. That attempt's
outcome counts as unknown, see [Call outcomes](#call-outcomes). The worker
never resends blind.

**A successful call settles.** The outcome transaction does this:

1. It writes the output fields to the subject record with the record update
   rules, as `updateRecord` does.
2. It sets the call to `succeeded` and clears its stored input.
3. It sets the instance to `running` on `next`, with a new revision.
4. It writes the history row of the step, at the revision the instance
   waited at. Its output is `{ "operationId", "outcome", "next" }`, plus
   `subjectVersion` when it wrote output fields.
5. It writes `record.updated` when it wrote output fields, with the same
   details as an `updateRecord` step. It writes `connector.called` with the
   details
   `{ "connector", "operation", "operationId", "attempts", "outcome", "lastStatus" }`.
   Both have the actor `system`.
6. It inserts the work item of the next step and deletes the call's item.

**A retry** sets the call back to `pending` and keeps the item. The item's
`due_at` is now plus `backoff`·2^(n−1) after attempt n, capped at 1 hour.

**A failed call settles** too. The call becomes `failed` and its stored input
is cleared. The instance becomes `failed`, with the error in a history row.
The transaction writes `connector.called` and `process.failed`.

No request or response body is ever stored. The stored input lives only on
the call row until the call settles. It never appears in history, audit
records or logs.

### Call outcomes

The platform decides which outcomes are retried. A retry always sends the
stored input again under the same operation identity.

| Outcome of an attempt | Class | Retried | When attempts run out |
| --- | --- | --- | --- |
| `2xx` with a body that fits the output | Succeeded | Not needed | Not needed |
| `2xx` with a body that does not fit the output | Unknown | Never resent. It goes straight to [reconciliation](#reconciliation), with or without `idempotency` | Does not apply |
| `3xx` | Failed | No | The instance becomes `failed` at once |
| `4xx` other than `408` and `429` | Failed | No | The instance becomes `failed` at once |
| Never sent: DNS failure, connection refused, TLS failure, missing binding | Retryable | Yes | The instance becomes `failed` |
| `408`, `429` and `503` | Retryable | Yes | The instance becomes `failed` |
| Timeout, a reset after the request was sent, another `5xx`, or a call found still `sending` | Unknown | With the same key, only when the connector declares `idempotency`. Without it, the call goes to [reconciliation](#reconciliation) | The call goes to [reconciliation](#reconciliation) |

A body fits the output when it is a JSON object with a value of the right type
for each output, and a value for each required output.

### Reconciliation

An unknown outcome means the system may have acted. The call becomes
`unknown`, and the worker asks the system what happened. It calls the
operation's `lookup` with the operation identity as `{idempotencyKey}`.

- **Found.** The lookup answers `200` with a body that fits the output. The
  call settles as succeeded with that output, as after a successful attempt.
  Nothing is sent again.
- **Not found.** The lookup answers `404`, so the system never acted. The
  call goes back to `pending` and is retried with the same identity. That
  counts as an attempt. When no attempts are left, the instance becomes
  `failed`.
- **Failed lookup.** Any other answer, including a `200` whose body does not
  fit, and a lookup that is never sent. The call stays `unknown`, and the
  lookup is tried again. The delay starts at `backoff` and doubles each time,
  up to 1 hour, with no limit.
- **No lookup.** The operation declares none. The call stays `unknown`, and
  the instance stays `waiting`. A history row with output
  `{ "operationId", "outcome": "unknown" }` makes it visible. An operator
  resolves it in M7.

An unknown outcome is never retried without the idempotency key or a lookup.

A lookup can only find a call whose key reached the system. The key goes in
the `idempotency` header, or in an operation path that holds
`{idempotencyKey}`.

**A late original request.** A lookup can answer not found while the original
request is still on its way. The call is then sent again. Only the system's
idempotency key stops the late original from creating a second order. A
connector without `idempotency` depends on its lookup being correct.

### Lost response

The purchasing system creates the order, but its response is lost. The
request still ends with exactly one purchase order.

1. The finance reviewer approves request `PR-2026-00042`. `markApproved` runs
   and queues `createPurchaseOrder`.
2. The step's transaction evaluates the input, stages call C as `pending`,
   sets the instance `waiting` and queues a `connector.call` item.
3. Worker A claims the item. It marks C `sending` with attempt 1.
4. Worker A sends `POST /purchase-orders` with `Idempotency-Key: C`.
5. The purchasing system creates `PO-2026-0001` under key C. Its response is
   lost, and worker A's request times out after `PT30S`.
6. The timeout is an unknown outcome. The connector declares `idempotency`
   and 4 of 5 attempts are left, so C goes back to `pending`. The item is due
   again after `PT30S`.
7. Worker B claims the item, marks C `sending` with attempt 2, and sends the
   same body with `Idempotency-Key: C`.
8. The purchasing system knows key C. It returns the existing
   `PO-2026-0001` and creates no new order.
9. The outcome transaction settles C as succeeded. It writes
   `purchaseOrderNumber = PO-2026-0001`, `record.updated`, and one
   `connector.called` with `attempts` 2. The instance moves on to `done`.
10. `done` completes the instance.

The purchasing system holds one order, and the request records its number.

- **Crash after sending.** Worker A crashes after step 5, before it records
  anything. C is still `sending` when its lease expires. The worker that
  claims the item next counts attempt 1 as unknown, and the steps go on from
  step 6.
- **No `idempotency`, with a lookup.** Suppose the connector declares no
  `idempotency`, and the create operation's path holds the key instead, such
  as `/purchase-orders/{idempotencyKey}`. The timeout in step 5 is unknown,
  and C is not sent again. The worker calls the lookup for key C, and the
  system answers `200` with `PO-2026-0001`. C settles as succeeded, as in step 9,
  with no second `POST`. Had the lookup answered `404`, C would be sent again
  as attempt 2.

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
  | `input` | `jsonb` | `{ "subjectId", "subjectVersion" }`: the subject record and the version the step read. For a task completion, the version its values were written over, or `null` without values. It holds no field values |
  | `output` | `jsonb`, null | `{ "next" }` for a decision, `{ "next", "subjectVersion" }` for an operation, with the version it wrote, `{ "state": "waiting", "taskId" }` for a task step, with the task it created, `{ "taskId", "outcome", "next" }` for a task completion, plus `subjectVersion` when it wrote values, `{ "state": "completed" }` for an end step, null for a failed step. *(planned for M5)* `{ "state": "waiting", "operationId" }` for a `callConnector` step that staged its call, and `{ "operationId", "outcome", "next" }` when the call settles, plus `subjectVersion` when it wrote output fields. `{ "operationId", "outcome": "unknown" }` for a call left unknown with no lookup |
  | `decision` | `text`, null | The branch a decision took: its zero-based index, such as `0`, or `otherwise`. The outcome's declared name for a task completion. Null for other steps and failed steps |
  | `error` | `text`, null | The error of a failed step |
  | `started_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `finished_at` | `timestamptz` | When the row was written, `clock_timestamp()` |
- `axis.process_work_items`: the ready work. Each item has an `id`, its
  `tenant_id`, its `kind`, its `due_at` time, and the `lease_expires_at` and
  `claim_token` of its current claim. A step's item also has the
  `process_instance_id` of its instance. A worker claims only items of the
  tenant it polls and only kinds it has a handler for. Lease times use the
  database clock. Completed and failed items are deleted. *(planned for M5)*
  A new kind, `connector.call`, sends or reconciles one connector call. Its
  item also has the `connector_call_id` of the call, and its `due_at` gives
  the backoff. See [Connector calls](#connector-calls).
- `axis.process_start_receipts`: the stored `201` response of each
  `Idempotency-Key`, unique per application, process and key.
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
  | `state` | `text` | `open` or `completed` |
  | `outcome` | `text`, null | The outcome the task was completed with. Null while it is `open` |
  | `completed_by` | `text`, null | The user who completed the task. Null while it is `open` |
  | `completed_at` | `timestamptz`, null | When the task was completed. Null while it is `open` |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
- `axis.connector_calls` *(planned for M5)*: one row per staged connector
  call, see [Connector calls](#connector-calls). A unique index on
  (`process_instance_id`, `step`) lets a step stage only one call. Times use
  the database clock. There is no column for a request or response body.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID, the call's operation identity. Every attempt and every lookup sends it as the idempotency key |
  | `process_instance_id` | `uuid` | The instance |
  | `step` | `text` | The declared name of the `callConnector` step |
  | `release_id` | `uuid` | The release the instance is pinned to |
  | `connector` | `text` | The connector's name |
  | `operation` | `text` | The operation's name |
  | `state` | `text` | `pending`, `sending`, `unknown`, `succeeded` or `failed` |
  | `attempts` | `integer` | The attempts made so far, the first one included |
  | `input` | `jsonb`, null | The evaluated input, sent again by every retry. Cleared when the call settles. Never copied to history, audit records or logs |
  | `last_status` | `integer`, null | The HTTP status of the last attempt or lookup. Null when none answered |
  | `last_error` | `text`, null | The error of the last attempt or lookup, such as a timeout. It holds no body or secret |
  | `created_at` | `timestamptz` | The start of the step's transaction, `now()` |
  | `settled_at` | `timestamptz`, null | When the call became `succeeded` or `failed`. Null before |

## Limits in M3

- **No cycles.** A process may not contain a cycle. A loop with no task step
  would run for ever. A returned request starts a new instance, so M3 needs
  no loops.
- **No user check.** The user id that a `user` assignee gives is not checked
  against any list of users. A wrong id creates a task that nobody sees.
