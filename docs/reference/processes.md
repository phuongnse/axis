# Processes

Detailed reference for processes: the resource shape, the steps, the compile
checks, the start endpoint, the instance states, execution, the worker
settings and the tables. Nothing in this file is built yet. Everything here is
*(planned for M3)*.

Dn refers to [decisions.md](../decisions.md). The design follows
[D11](../decisions.md#d11-durable-process-engine--agreed) and
[D19](../decisions.md#d19-process-resource-and-worker-host--agreed). The
reasons for durable execution are in
[knowledge](../domain/knowledge.md#execution).

- **Process.** A `process` resource is a set of named steps and the
  transitions between them. It names one entity, its subject.
- **Process instance.** One run of a process. It is pinned to the release
  that was active when it started.
- **Subject record.** The record of the subject entity that an instance runs
  for. Each instance has exactly one.
- **Step.** One unit of a process, such as a decision or a human task. A step
  names the step that follows it.

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
      "outcomes": { "approve": "financeCheck", "return": "markReturned", "reject": "markRejected" }
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
      "outcomes": { "approve": "markApproved", "return": "markReturned", "reject": "markRejected" }
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
  the subject record. A start is allowed only when it is true. Its `message`
  is a label, shown when a start fails the condition.
- `start` names the first step.
- `steps` is an array of step objects. Each has a `name` and a `type`. The
  other properties depend on the type, see [Steps](#steps).
- Transitions name the next step by its name: `next`, `branches[].next`,
  `otherwise` and the targets of task `outcomes`.
- `kind` stays the resource kind. Steps use `type`, as fields and widgets do.

## Steps

M3 has four step types. Wait for event, timer and sub-process come later.

- **`decision`.** `branches` is an ordered list. Each branch has a `when`
  expression and a `next` step. The branches are evaluated in order, and the
  first one that is true wins. A `when` that gives `null` counts as false.
  `otherwise` is required and names the step taken when no branch is true.
- **`task`.** A human task. The instance waits until a person completes the
  task with one of its outcomes. `outcomes` maps each outcome name, such as
  `approve`, `return` or `reject`, to the next step. The assignee, the form
  and the due date come with the human task design *(planned for M3)*. The
  manager of the purchase request is expected to come from
  `department.manager`.
- **`operation`.** M3 has one built-in operation, `updateRecord`. Its `set`
  maps fields of the subject record to expressions. The step writes them
  through the record update command, so computed fields are recomputed and
  validations run, in the step's transaction. A validation failure fails the
  step. Operations from extension packages and external calls come in M5.
- **`end`.** The instance is `completed` when it reaches an `end` step. An
  `end` step has no `next`.

## Compile checks

The compiler reports these as diagnostics. Their codes are assigned when the
checks are built.

- `entity` names a loaded entity that is not a child entity.
- Step names are unique within the process, ignoring letter case.
- `start`, every `next`, every branch `next`, `otherwise` and every outcome
  target name a step of the process.
- Every step is reachable from `start`.
- The steps form no cycle. See [Limits in M3](#limits-in-m3).
- A decision has at least one branch.
- Each `when` and the `startCondition` expression give a boolean.
- The fields in an `updateRecord` `set` are fields of the subject entity.
  None is computed or a child collection. Each expression fits its field's
  type.
- An `end` step has no `next`.
- The `startCondition` message text key exists, checked like other labels.
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

One transaction pins the active release, inserts the instance and its first
work item, and stores the receipt for the `Idempotency-Key`.

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
- **History.** Every step occurrence records its input, its output, the
  decision taken and any error.

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

## Tables

`Axis.Processes` owns these tables in the `axis` schema of each tenant
database, with history in `axis.__processes_migrations` (see
[storage](storage.md)):

- `axis.process_instances`: one row per instance, with its process, subject
  record, release, state and revision. A partial unique index on
  application, process and subject record, where the state is `running` or
  `waiting`, enforces one start per submission.
- `axis.process_step_history`: one row per step occurrence, with its input,
  output, decision and error.
- `axis.process_work_items`: the ready work, with its tenant id, claim token
  and lease expiry.
- `axis.process_start_receipts`: the stored `201` response of each
  `Idempotency-Key`, unique per application, process and key.

## Limits in M3

- **No cycles.** A process may not contain a cycle. A loop with no task step
  would run for ever. A returned request starts a new instance, so M3 needs
  no loops.
- **No sign-in until M4.** Tasks cannot be completed in Production before
  sign-in arrives. Processes can be used only in Development and in tests in
  M3.
