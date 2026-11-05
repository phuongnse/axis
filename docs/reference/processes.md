# Processes

A process is the configured workflow of an application: a `process` resource of named steps and the
transitions between them, which the worker runs durably. This page holds the resource's shape, its
steps, the compile checks, the start endpoint, and which release running work uses. The reasons for
durable execution are in [knowledge](../domain/knowledge.md#execution), and those for human tasks in
[knowledge](../domain/knowledge.md#human-work).

The rest of the process reference is on these pages:

| Page                                          | What it holds                                                                                    |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| [Process states](process-states.md)           | The states of an instance and of a task                                                          |
| [Task API](task-api.md)                       | Listing, reading and completing tasks                                                            |
| [Task deadlines and claims](task-deadlines.md) | Escalations, deadlines, overdue tasks, claim, release and reassign                               |
| [Process flow](process-flow.md)               | Timer, wait for event and sub-process steps, loops, `createRecord`, processes without a subject |
| [Notifications](notifications.md)             | The `notify` operation, in-app notifications and email                                           |
| [Process execution](process-execution.md)     | How the worker runs steps, connector calls, package operations and the worker settings          |
| [Process events](process-events.md)           | Raised events, triggers and schedules                                                            |
| [Process inspection](process-inspection.md)   | Inspecting instances and the operator commands                                                   |
| [Process tables](process-tables.md)           | The tables that `Axis.Processes` owns                                                            |

A process names one entity, its subject, and each **process instance**, one run of the process, runs
for one record of it, the **subject record**. A process may instead take typed parameters and have no
subject, as [process flow](process-flow.md#processes-without-a-subject) describes. An instance is
pinned to the release that was active when it started. A **step** is one unit of a process, such as
a decision or a human task, and names the step that follows it. A **task** is one occurrence of a
`task` step: the work a person must decide on. It names its assignee, and it is `open` until one
decision completes it. Process expressions use the language of [expressions](expressions.md), and
what they can see is in [Names and references](expressions.md#names-and-references).

## Resource shape

This section shows a whole process and says what each top-level property does. The purchase request
approval needs one rule and one process, and the purchase request sample in
`samples/apps/purchase-requests/` holds both. The finance threshold is a `rule` with no parameters,
so it is configured, not hard-coded:

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

The process submits the request and records when, asks the department manager, asks a finance
reviewer at or above the threshold, and ends approved, returned or rejected. After `markApproved`,
`announceApproved` raises the `PurchaseRequestApproved` event with the request's number and total,
see [Raise event step](process-events.md#raise-event-step), and the sample's
`PurchaseRequestApprovedWebhook` delivers the event to the purchasing system, see
[Webhook resource](outbound-webhooks.md#webhook-resource). An approved request then gets its purchase
order from the purchasing system, see [Connector calls](process-execution.md#connector-calls), and a
payment reference from the sample extension package, see
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
      "assignee": { "user": "department.head.user" },
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
      "branches": [{ "name": "branch1", "when": "totalAmount >= FinanceThreshold()", "next": "financeApproval" }],
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
      "next": "announceApproved"
    },
    {
      "name": "announceApproved",
      "type": "operation",
      "operation": "raiseEvent",
      "event": "PurchaseRequestApproved",
      "fields": { "number": "number", "totalAmount": "totalAmount" },
      "next": "createPurchaseOrder"
    },
    {
      "name": "createPurchaseOrder",
      "type": "operation",
      "operation": "callConnector",
      "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" },
      "input": { "requestNumber": "number", "supplierCode": "supplier.code", "amount": "totalAmount", "lines": { "from": "lineItems", "fields": { "description": "description", "quantity": "quantity", "unitPrice": "unitPrice", "amount": "amount" } } },
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

- `entity` names the subject entity, and each instance runs for one record of it. A process without
  it declares `parameters` instead, a list of `{ "name", "type" }` with the scalar types of rule
  parameters: `text`, `integer`, `decimal`, `boolean`, `date` and `date-time`. See
  [Processes without a subject](process-flow.md#processes-without-a-subject).
- `startCondition` is optional. Its `expression` is a boolean expression over the subject record,
  and a start is allowed only when it is true: a `false`, a `null` or a run-time error fails the
  start. Its `message` is a label, shown when a start fails the condition.
- `start` names the first step.
- `steps` is an array of step objects. Each has a `name` and a `type`, and its other properties
  depend on the type, see [Steps](#steps). A [customization layer](customization-layers.md#list-keys)
  finds a step, a branch or a task's outcome by its `name`.
- Transitions name the next step by its name: `next`, `branches[].next`, `otherwise` and a task's
  `outcomes[].next`.
- `kind` is the resource kind. Steps use `type`, as fields and widgets do.

## Steps

Each step has a `type`, which decides what it does and which properties it takes. The step types
and operations, and where each is described:

| Step type or operation                                  | What it does                                                         | Described in                                                     |
| ------------------------------------------------------- | -------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `decision`                                              | Takes the first branch whose condition is true                       | Below                                                            |
| `task`                                                  | Waits for a person to decide                                         | Below                                                            |
| `operation` with `updateRecord`                         | Sets fields of the subject record                                    | Below                                                            |
| `operation` with `callConnector`                        | Calls an external system outside the step's transaction             | [Connector calls](process-execution.md#connector-calls)          |
| `operation` with `<package>/<operation>`                | Runs an extension package's operation outside the step's transaction | [Package operations](process-execution.md#package-operations)    |
| `operation` with `raiseEvent`                           | Raises an event about the subject record                             | [Raise event step](process-events.md#raise-event-step)           |
| `operation` with `createRecord`                         | Creates a record of another entity                                   | [Create record operation](process-flow.md#create-record-operation) |
| `operation` with `notify`                               | Notifies users, roles or addresses in the site or by email           | [Notify operation](notifications.md#notify-operation)            |
| `timer`                                                 | Waits for a time                                                     | [Timer step](process-flow.md#timer-step)                         |
| `waitEvent`                                             | Waits for an event about the subject record                          | [Wait for event step](process-flow.md#wait-for-event-step)       |
| `subProcess`                                            | Starts another process and waits until it ends                       | [Sub-process step](process-flow.md#sub-process-step)             |
| `end`                                                   | Completes the instance                                               | Below                                                            |

- **`decision`.** `branches` is an ordered list, and each branch has a `name`, a `when` expression
  and a `next` step. A branch name is unique in its decision, ignoring letter case. The branches are
  evaluated in order and the first one that is true wins, and a `when` that gives `null` counts as
  false. `otherwise` is required and names the step taken when no branch is true.
- **`task`.** A human task: the instance waits until a person completes the task with one of its
  outcomes. It has these parts:
  - `label` is required. It is a label with a text key, shown in the task inbox and on the task page.
  - `assignee` has exactly one of `{ "user": "<expression>" }` and `{ "role": "<role name>" }`. A
    `user` expression gives `text`, a user id, such as `department.manager`. A `role` names a role
    the user must hold, and a user who holds it may claim the task, see
    [Claim and release](task-deadlines.md#claim-and-release).
  - `form` names a [form resource](form-widget.md) whose entity is the subject entity. The task page
    shows it over the subject record.
  - `dueIn` is optional. It is a positive fixed-length duration, as
    [Compile checks](#compile-checks) defines, and the task's due date is the time the task is
    created plus `dueIn`. The due date is only shown, and an open task past it is
    [overdue](task-deadlines.md#task-states).
  - `deadline` and `escalations` are optional. A deadline moves the instance to its `next` step when
    the task is still `open` after its `after`, and an escalation reassigns the task or notifies
    people before that. See [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
  - `outcomes` is an array of `{ "name", "label": { "textKey" }, "next" }`, such as `approve`,
    `return` and `reject`. Each names the step taken when the task is completed with it.

  The step's transaction evaluates the assignee, creates the task as `open` and sets the instance to
  `waiting`. A `user` expression that gives `null` or blank text fails the step. Completing the task
  resumes the instance, see [Completing a task](task-api.md#completing-a-task).
- **`operation` with `updateRecord`.** Its `set` maps fields of the subject record to expressions,
  and `next` names the step that follows. The step writes the values with the record update rules,
  in the step's transaction, so computed fields are recomputed and validations run, and a validation
  failure fails the step.
- **`end`.** The instance is `completed` when it reaches an `end` step, which has no `next`.

## Compile checks

The compiler reports each of these problems as a diagnostic, and it reports every problem of a
process in one pass. The details of the pass are in
[the Resolve step](configuration.md#configuration-pipeline), and each code is in the
[diagnostic code table](diagnostic-codes.md).

- `entity` names a loaded entity (`AXC0067`) that is not a child entity (`AXC0068`).
- A process has `entity` or `parameters`, not both and not neither (`AXC0244`). The other checks of a
  process without a subject, `AXC0245` to `AXC0247`, are in
  [Processes without a subject](process-flow.md#processes-without-a-subject).
- Step names are unique within the process, ignoring letter case (`AXC0069`).
- `start`, every `next`, `otherwise`, outcome `next`, `deadline.next` and `timeout.next` name a step
  of the process, ignoring letter case (`AXC0070`).
- Every step is reachable from `start` (`AXC0071`), and every step has a path to an `end` step
  (`AXC0072`).
- The steps form no cycle that has no waiting step (`AXC0073`), because such a cycle would run for
  ever. A cycle through a `task`, a `timer` or a `waitEvent` step is allowed, see
  [Loops](process-flow.md#loops).
- A decision has at least one branch (`AXC0004`). Each branch has a `name` (`AXC0004` when it is
  missing), and a later branch with a name already used in the decision, ignoring letter case, is
  `AXC0359` at `/steps/{i}/branches/{j}/name`.
- Each `when` and the `startCondition` expression give a boolean. They see the subject entity's
  fields, computed ones included, `id`, the subject record's id, its child collections through
  aggregates, the named rules, and paths through reference fields such as `department.manager.name`.
  A path takes at most 3 hops (`AXC0058`), and a `.` after a field that is not a reference is
  `AXC0047`. See [Scope in a process expression](expressions.md#names-and-references).
- An operation names `updateRecord`, `callConnector`, `raiseEvent`, `createRecord`, `notify` or a
  package operation written `<package>/<operation>`, matched exactly as a step `type` is (`AXC0078`
  at `/steps/{i}/operation`).
- Each key of an `updateRecord` `set` names a field of the subject entity, ignoring letter case, that
  no earlier key of the step names (`AXC0079`). The field is not computed, not numbered by a sequence
  and not a child collection (`AXC0080`). Each is reported at `/steps/{i}/set/{field}`.
- Each `set` expression fits its field's type, with the same scope as a `when`. A problem is reported
  at `/steps/{i}/set/{field}` with its expression code, such as `AXC0048` for a value of the wrong
  type.
- The checks of the other operations and step types are with each of them: a `createRecord` step's in
  [Create record operation](process-flow.md#create-record-operation), a `callConnector` step's
  (`AXC0102` to `AXC0109`) in [Connector calls](process-execution.md#connector-calls), a package
  operation step's (`AXC0150` and `AXC0151`) in
  [Package step shape](process-execution.md#package-step-shape), a `raiseEvent` step's (`AXC0125` to
  `AXC0128`) in [Raise event step](process-events.md#raise-event-step), a `notify` step's (`AXC0266`
  to `AXC0273`) in [Notify operation](notifications.md#notify-operation), a `timer` step's in
  [Timer step](process-flow.md#timer-step), a `waitEvent` step's in
  [Wait for event step](process-flow.md#wait-for-event-step), a `subProcess` step's in
  [Sub-process step](process-flow.md#sub-process-step), and a task's `deadline` and `escalations` in
  [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
- A step has the properties of its type and no others, as the table below says (`AXC0004`). `to`,
  `channels`, `message` and `open` are on a `notify` step only, and the task properties are `label`,
  `assignee`, `form`, `dueIn` and `outcomes`.
- A task's `assignee` has exactly one of `user` and `role` (`AXC0081` at `/steps/{i}/assignee`). A
  `role` is a non-empty name (`AXC0004`) that names a declared [role](roles-and-policies.md),
  compared exactly, otherwise it is `AXC0089` at `/steps/{i}/assignee/role`. A name whose role file
  was not loaded is not reported again. In a
  [module](modules-and-templates.md#processes-pages-policies-and-dependencies) process, `role` names a
  role slot instead, otherwise it is `AXC0307`.
- A `user` expression gives `text`, with the same scope as a `when`. A problem is reported at
  `/steps/{i}/assignee/user` with its expression code, such as `AXC0048` for a number.
- A task's `form` names a loaded form (`AXC0082`) whose entity is the subject entity (`AXC0083`).
  Each is reported at `/steps/{i}/form`.
- A task's `dueIn`, when set, is a positive ISO 8601 duration in whole numbers: either weeks, such as
  `P2W`, or days with an optional time part of hours, minutes and seconds, such as `P3D`, `PT4H` or
  `P1DT12H` (`AXC0084` at `/steps/{i}/dueIn`). Years and months are refused, because they have no
  fixed length. Every other duration of a process follows the same rule.
- Outcome names are unique within the step, ignoring letter case (`AXC0085` at
  `/steps/{i}/outcomes/{j}/name`).
- The `startCondition` message's text key, each task `label`'s and each outcome `label`'s exist,
  checked like other labels (`AXC0028`).
- Every expression gets the usual syntax, type and cost diagnostics, see
  [Diagnostics](expressions.md#diagnostics).

| Step                        | Has                                              | Has none of                                                                                                                              |
| --------------------------- | ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Any `operation`             | `operation` and `next`                           | `branches`, `otherwise` and the task properties                                                                                          |
| `updateRecord`              | A `set` with at least one field                  | `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields`                                                                     |
| `callConnector`             | `connector` and `input`                          | `set`, `event`, `entity`, `fields`                                                                                                       |
| `raiseEvent`                | `event` and `fields`                             | `set`, `connector`, `input`, `output`, `retry`, `entity`                                                                                 |
| `createRecord`              | `entity` and a `set` with at least one field     | `connector`, `input`, `output`, `retry`, `event`, `fields`                                                                               |
| `notify`                    | `to`, `channels` and `message`                   | `set`, `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields`                                                              |
| `decision`                  | `branches` and `otherwise`                       | `operation`, `set`, `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields`, `to`, `channels`, `message`, `open`, `next` and the task properties |
| `task`                      | `label`, `assignee`, `form` and at least one outcome | `branches`, `otherwise`, `operation`, `set`, `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields`, `next`                |
| `end`                       | Nothing more                                     | `next`, `branches`, `otherwise`, `operation`, `set`, `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields`, `wait`, `until` and the task properties |
| `timer`                     | `next`                                           | `branches`, `otherwise`, `operation`, `set`, `connector`, `input`, `output`, `retry`, `event`, `entity`, `fields` and the task properties |
| `waitEvent`                 | `event` and `next`                               | Any property but `when` and `timeout`                                                                                                    |

## Start endpoint

The start endpoint creates an instance for one subject record. A process without a subject takes
[parameters instead](#a-process-without-a-subject):

```http
POST /api/apps/{app}/processes/{process}/instances
Content-Type: application/json
Idempotency-Key: 3f0c…

{ "subjectId": "0192f4a7-…" }
```

The content type follows the same rules as the record API, see
[Create, update and delete](record-api.md#create-update-and-delete), and errors are problem details,
as in the [record API](record-api.md#errors).

| Status                               | When                                                                                                                                                                                                                                                                                                                                         |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `201`                                | The instance was started. The body is the instance: `{ "id", "process", "subjectId", "releaseId", "state" }`.                                                                                                                                                                                                                                 |
| `401`                                | Nobody is signed in. The title is the record API's "Sign in to read or write this data.".                                                                                                                                                                                                                                                     |
| `404`                                | An unknown application, an application with no active release, or an unknown process.                                                                                                                                                                                                                                                         |
| `403`                                | The user has no `start` grant on the process. The title is the record API's "You have no grant for this action.".                                                                                                                                                                                                                             |
| `400` for a body                     | A body that is not `{ "subjectId": "<uuid>" }`, or a `subjectId` that names no record of the subject entity the user may read. The error is keyed `/subjectId`.                                                                                                                                                                              |
| `400` for a start condition          | The subject record fails the `startCondition`. The error is keyed `/subjectId` and holds the condition's message text key.                                                                                                                                                                                                                   |
| `409`                                | A `running` or `waiting` instance already exists for this process and subject record.                                                                                                                                                                                                                                                         |
| `409` for a policy that reads a pending field | A `read` filter of the user's grants on the subject entity reads a [pending field](migrations.md#recomputing-computed-fields). The title is the record API's "Your access to this entity cannot be checked while a field is being recomputed.", with no `errors`, see [the record API](record-api.md#errors). It comes before the subject record is looked up, so it is the same for a record that does not exist. |
| `422`                                | The `Idempotency-Key` was used before with a different subject record.                                                                                                                                                                                                                                                                        |
| `503` for a changed release          | The application's active release changed on each of three attempts, found under the [start lock](#releases-and-running-work). The server already ran the start again on the new release each time. Nothing was written, and the client may send the request again. The title is fixed: "The application's active release changed while the request ran. Try again." |
| `503` for a write timeout            | The start's transaction ran past `Data:WriteTimeout` and PostgreSQL cancelled it. Nothing was written, and the client may send the request again. The title is fixed: "The write took too long. Try again." See [data settings](host-settings.md#data-settings).                                                                             |

The `201` has no `Location` header, because reading an instance needs an `inspect` grant, which the
user who started it may not hold. See the [instance detail](process-inspection.md#instance-detail).

A start needs a session, or a bearer token of a [service account](security.md#service-accounts), a
`start` grant on the process and read access to the subject record, as
[Authorization](security.md#authorization) describes. A request with a bearer token needs no CSRF
header, and its caller id is the actor of the `process.started` audit record. A bad token is a `401`
with `WWW-Authenticate: Bearer`.

A subject record the user may not read does not exist for them. That is a record outside every
filter of their `read` grants on the subject entity, or any record when they hold no `read` grant on
it. It gets the same `400` keyed `/subjectId` as a `subjectId` that names no record, so the user
cannot tell the two apart.

A request is checked in this order, and the first failure is the response:

1. The session or the bearer token, `401`.
2. The application and the process, `404`.
3. The `start` grant on the process, `403`.
4. The content type, `415`.
5. The body, `400`.
6. The `Idempotency-Key` header, `400`.

A start by a session without the CSRF header is a `403` before any of these checks. Read access to
the subject record is checked in the transaction, when the record is read. One transaction then does
the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls back and answers from the
   stored receipt, or with the `422`.
2. It answers the policy `409` when a `read` filter of the user reads a pending field, whatever the
   record. A repeat with a used key has already been answered by step 1. Then it locks the subject
   record, checks that the user may read it, and reads it. A missing one, or one the user may not
   read, is the `400`. The lock makes the start condition see the latest version, and an edit of the
   record waits until the start commits or rolls back.
3. It evaluates the `startCondition` on the record, with any pending computed field computed by the
   active release, and a failure is the `400`.
4. It inserts the instance as `running` at revision 1, pinned to the release that was active when
   the request was checked. Its next step is the process's `start` step. An instance already running
   or waiting is the `409`.
5. It inserts the instance's first work item.
6. It writes the audit record `process.started` for the subject record, with details
   `{ "processId", "releaseId" }`. The actor is the [current user](task-api.md).

When any part fails, nothing is written. The instance then waits for the worker to run its steps.

An [endpoint call](inbound-endpoints.md) starts its process on the record it creates by steps 3 to 6,
in the call's transaction. Its actor is the caller, and its details add the endpoint's name.

The `Idempotency-Key` header is optional, and it makes a repeated start safe:

- A value must be non-empty and at most 255 characters, otherwise the response is `400`.
- Keys are scoped per application and process.
- A repeat with the same key and the same subject record returns the stored `201` status and body,
  and no second instance starts.
- The same key with a different subject record is `422`.
- Only a `201` is stored, so a rejected start can be retried with the same key after the record is
  fixed.
- Concurrent repeats are serialized by the receipt's unique key, so only one of them starts an
  instance.

### A process without a subject

A [process without a subject](process-flow.md#processes-without-a-subject) takes its typed parameters
instead of a record id:

```http
POST /api/apps/{app}/processes/{process}/instances
Content-Type: application/json

{ "parameters": { "days": 31 } }
```

The checks run in the order above, and so do the answers. The differences are these:

- **`201`.** The body has the same keys, and `subjectId` is `null`. The instance stores the parameter
  values.
- **`400` for a body.** The body must be exactly one `parameters` property, and its value must be an
  object. Anything else is one error keyed `/parameters`.
- **`400` for a parameter.** Each declared parameter must be given once, by its exact name, letter
  case included, with a value of its type, read as a record field value of that type is. A missing,
  unknown, repeated, `null` or mistyped parameter is an error keyed `/parameters/{name}`, and all the
  errors are in one response.
- **`400` for a start condition.** The `startCondition` is checked over the parameters. A failure is
  an error keyed `/parameters` that holds the condition's message text key.
- **`409`.** A `running` or `waiting` instance of the process exists. The title is "A running or
  waiting instance of this process already exists.".
- **`403`.** The user needs the `start` grant only. There is no record, so no `read` check runs and
  there is no policy `409`.
- **`422`.** The `Idempotency-Key` was used before with other parameter values. The receipt stores a
  null subject, and the values are compared with those stored on the receipt's instance. Decimals
  count as equal when they are the same number.

## Releases and running work

This section says which release each part of Axis uses while an application's releases change under
running work. New work starts on the active release. Running work stays on its pinned release until
it ends, including its tasks, forms, texts and package versions, while roles and policies always come
from the active release. The reasons are in
[D29](../decisions/d29-release-pinning-and-text-resources.md).

In the table, **Pinned** means the instance's or task's release, or the release id requested for
texts, and **Both** means different parts use different releases.

| Endpoint or command | Release | Notes |
| --- | --- | --- |
| Record API | Active | Reads and writes use the release resolved when the request begins. |
| Data source endpoint | Active | The query and its policies come from the active release. |
| Process start endpoint | Active | The new instance is pinned to the active release at its start. |
| Inbound endpoint | Active | Creates the record and starts its instance on the active release. |
| Site, site list and page endpoints | Active | Describe sites and pages of active applications. |
| Texts endpoint without `release` | Active | Serves the active release's texts. |
| Texts endpoint with `release` | Pinned | Serves any stored release of the site's application. An unknown id, a non-UUID or another application's release answers `404`. |
| Task list | Both | Step and subject labels use the task's release, and their texts load from it too. Policies and roles come from the active release. |
| Task read | Both | Form, outcomes and field definitions use the task's release, and so do texts. Policies come from the active release. |
| Task completion | Both | Form, outcomes and the record write use the pinned release. Data change triggers come from the active release. |
| Instance list | Active | Roles and policies come from the active release. |
| Instance detail | Both | The graph and the step names use the instance's release, and so do the fields a call's mapping reads or writes. Roles and policies come from the active release, and so do the fields the user may read. The `sensitive` flag counts when the pinned release or the active release marks the field. See [Values and masking](process-inspection.md#values-and-masking). |
| Worker steps (`decision`, `task`, `operation`, `updateRecord`, `subProcess`, `end`) | Pinned | Step definitions, expressions, rules and record writes use the instance's release. |
| `connector.call` and `package.call` | Pinned | Calls use the instance's release, including its pinned package version. Deployment binding values are resolved at run time. |
| Operator retry | Pinned steps, active policies | The instance runs its steps on its pinned release. The `retry` grant comes from the active release. |
| Operator resolve | Both | The output check and the record write use the pinned release. The `resolve` grant and data change triggers come from the active release. |
| `raiseEvent` | Both | The occurrence holds the pinned release. Its `sync` triggers come from the active release. |
| `event.deliver` | Active | Reads `async` event triggers from the release active at delivery. |
| Data change triggers | Active | Fire from the active release, even when old-release work writes the record. |
| Schedule fan-out | Active | Uses the release active when the fan-out runs, and pins each new instance to it. |
| Server and worker start check | Both | Rebuilds every active release and every release with a running or waiting instance. A missing pinned package version is `AXC0146` and stops the start. |

- **Starts from old work.** A data change or event trigger set off by old-release work starts a new
  instance on the active release. For writes, the record is read again with the active model before
  its triggers run. See [Starts from triggers](process-events.md#starts-from-triggers).
- **Authorization.** Roles and policies always come from the active release, older tasks included,
  and the assignee check is the same on every release. The subject's display field comes from the
  task's release, but the active policies decide whether it is readable. Removing or renaming a
  policy can deny access to an old task's subject record or fields.
- **Pinned texts.** Each release carries its own texts, and the inbox and the task page load them by
  the task's `releaseId`. Old texts stay readable by id for as long as the release is stored, even
  after its instances end. A newer release may remove a key its labels no longer use, and every key a
  label uses must exist in every locale of its release, as `AXC0027` and `AXC0028` check.

The **start lock** keeps a start from running on a release that is being replaced:

- Each start takes `pg_advisory_xact_lock_shared` keyed on the application `id`, before it reads the
  active release, as the first statement of the transaction that inserts the instance. This covers
  the process start endpoint, the inbound endpoint, record API creates and updates, task completion,
  event delivery, `sync` event triggers, update triggers from worker steps and schedule fan-out. A
  record delete takes the lock shared too, and the subject's row lock decides between a start and a
  delete of the same record.
- An activation with a pending migration takes the same lock exclusively, after the schema lock, and
  holds it to commit, so starts then read the new release. An activation with no pending migration
  never takes it. See [Migrations](migrations.md#migrations).
- A server request that finds a different active release under the lock runs again on the new
  release. The process start endpoint, the [inbound endpoint](inbound-endpoints.md#responses), task
  completion and record creates, updates and deletes answer `503` after three release changes.
- Operator [retry](process-inspection.md#retry) takes the lock as the first statement of its
  transaction and answers `503` after three release changes, and
  [resolve](process-inspection.md#resolve) takes it the same way.
- Event delivery reads the active release again in each trigger's transaction, and skips a trigger
  that the new release does not have.

### Walkthrough: a waiting purchase request

This walkthrough shows release pinning in the
[purchase request sample](../../samples/apps/purchase-requests/sites/purchasing.json). R1 and R2
stand for stored release UUIDs of the same application.

Release R1 has `FinanceThreshold()` = 10000, and the text key `purchaseRequest.sections.decision`
reads "Decision" in English. PR-2026-00042 has a total of 12000 and waits on `managerApproval` in R1.
Release R2 then becomes active. It replaces that text key with a new key in its form and texts, and
raises `FinanceThreshold()` to 15000.

1. The manager's inbox row shows R1's step label, loaded from R1's texts. Its subject label uses R1's
   display field, if R2's policies allow it.
2. The task read returns `releaseId` R1 and R1's form. Its fields are filtered by R2's policies, so a
   tightened field grant applies at once.
3. The task page loads `GET /api/sites/purchasing/texts/en?release=<R1>`. The section title still
   reads "Decision", even though R2 removed its key.
4. When the manager approves, `financeCheck` runs on R1. The waiting request's finance check uses
   R1's threshold, 12000 >= 10000, so it goes to `financeApproval`, which is also on R1.
5. A new request for 12000 submitted after R2 becomes active starts on R2. After manager approval,
   12000 < 15000 sends it to `markApproved`.
6. After the R1 instance ends, `?release=<R1>` still answers with R1's texts, because R1 is still a
   stored release of the site's application.

## Limits

Two limits apply to every process, and an author should know them when writing one.

- **Loops only through a waiting step.** A process may not contain a cycle that has no `task`,
  `timer` or `waitEvent` step, because it would run for ever (`AXC0073`). See
  [Loops](process-flow.md#loops).
- **No user check on an assignee.** The user id that a `user` assignee gives is not checked against
  any list of users, so a wrong id creates a task that nobody sees. A
  [reassign](task-deadlines.md#reassign) checks its target against the
  [user profiles](notifications.md#user-profiles), but the `user` expression stays unchecked.
