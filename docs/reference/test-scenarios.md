# Test scenarios

Detailed reference for test scenarios, the `scenario` resources that check an application's
behaviour against a real database.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

A `scenario` resource is a configuration-level test. See
[D32](../decisions/d32-test-scenarios.md). Scenario files live under
`scenarios/`, one file per scenario. They compile with the application but stay
out of the release and its content hash. A file is a scenario because its `kind`
is `scenario`, wherever it is in the folder. The resource kind and its compile
checks exist. The runner runs record actions, process starts and task
completions, with or without `expect`, and the `expectRecord`, `expectInstance`
and `expectTask` steps. It sets `now`. It answers connector and package calls
from `stubs`. Studio runs scenarios through the publisher, see
[D55](../decisions/d55-running-test-scenarios-from-studio.md). The runner is a library that runs a scenario from a folder
or from files held in memory, such as a snapshot, and the `Axis.Scenarios`
command is one caller of it.

This scenario submits a purchase request above the finance threshold. The
manager approves it, the finance reviewer takes the finance decision and the
request ends `approved`:

```json
{
  "id": "ee1ac8c8-74c5-4105-867a-1090a15746a7",
  "kind": "scenario",
  "name": "FinanceApprovalAboveThreshold",
  "formatVersion": 1,
  "now": "2026-10-01T09:00:00Z",
  "users": {
    "anna": ["employee"],
    "binh": ["department-manager"],
    "lan": ["finance-reviewer"]
  },
  "stubs": [
    {
      "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" },
      "output": { "orderNumber": "PO-1001" }
    },
    {
      "operation": "payment-references/creditorReference",
      "output": { "creditorReference": "RF18PR0001" }
    }
  ],
  "steps": [
    {
      "type": "createRecord",
      "entity": "PurchaseRequest",
      "as": "anna",
      "save": "request",
      "values": {
        "title": "Laptops",
        "department": "15d5bba1-eaf1-431b-9001-e1e3ade50979",
        "supplier": "06e1abca-17bb-4a84-a05d-508f7428a7e5",
        "currency": "USD",
        "status": "draft",
        "lineItems": [{ "description": "Laptop", "quantity": 3, "unitPrice": 5000 }]
      }
    },
    { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "request", "as": "anna" },
    { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "binh" } },
    { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
    {
      "type": "expectInstance",
      "process": "PurchaseRequestApproval",
      "record": "request",
      "state": "waiting",
      "step": "financeApproval"
    },
    { "type": "expectTask", "record": "request", "step": "financeApproval", "assignee": { "role": "finance-reviewer" } },
    { "type": "completeTask", "record": "request", "step": "financeApproval", "outcome": "approve", "as": "lan" },
    {
      "type": "expectInstance",
      "process": "PurchaseRequestApproval",
      "record": "request",
      "state": "completed"
    },
    {
      "type": "expectRecord",
      "record": "request",
      "values": { "status": "approved", "purchaseOrderNumber": "PO-1001", "paymentReference": "RF18PR0001" }
    }
  ]
}
```

- `users` maps a user id to its role names. Each role must be a declared
  [`role`](roles-and-policies.md). No identity provider takes part. The runner
  gives each step the user's id and roles, as a session would.
- `now` is a date-time with an offset. It fixes the time that expressions,
  filters, sequence numbers and task due dates read, for the whole scenario.
  Timestamps that PostgreSQL writes, such as `created_at`, keep the real clock.
- `stubs` hold one answer per connector operation or package operation. A stub
  names a `connector` with its `operation`, or a package operation written
  `<package>/<operation>`. It gives either an `output` or `"outcome": "failed"`,
  never both. A connector stub matches the connector and the operation ignoring
  letter case. A package stub matches the package id exactly and the operation
  ignoring letter case. One stub answers every call of its operation with the
  same output.
  - `output` is the answer of a successful call. The call writes it to the
    subject fields and the process moves on, as a real call would.
  - `"outcome": "failed"` makes the call fail at once with no retry. The
    instance ends `failed`, which `expectInstance` can check.
  - A call that is reached and has no stub fails the scenario at that action.
    The failure names the process step and the operation.
  - The compile step checks that a stub names an operation and its outputs. It
    does not check the output's value types. A connector output that does not
    fit leaves the call `unknown` or retried. A package output that does not fit
    fails the instance.
  - No real system or package code runs. The scenario tenant has placeholder
    deployment bindings for every connector and package binding the application
    names, never a real URL or secret.

Every step has a `type`. These are the step kinds:

| Step | Properties | What it does |
| --- | --- | --- |
| `createRecord` | `entity`, `values`, `as`, optional `save`, `expect` | Creates a record through the record command. `save` gives it an alias for later steps. |
| `updateRecord` | `record`, `values`, `as`, `expect` | Updates the saved record through the record command. |
| `deleteRecord` | `record`, `as`, `expect` | Deletes the saved record through the record command. |
| `startProcess` | `process`, `record`, `as`, `expect` | Starts the process on the saved record. |
| `completeTask` | `record`, `step`, `outcome`, optional `values`, `as`, `expect` | Completes the task of that step with the outcome. |
| `expectRecord` | `record`, `values` | Asserts that the saved record holds these values. |
| `expectInstance` | `process`, `record`, `state`, optional `step` | Asserts the state of the process instance on the record, and the step it waits at. |
| `expectTask` | `record`, `step`, `assignee` or `state` | Asserts who the task is assigned to, or its state. |
| `advanceTime` | `by` | Moves the scenario's `now` forward by `by`, then runs the work items that are due. |

- `createRecord`, `updateRecord`, `deleteRecord`, `startProcess` and
  `completeTask` are the actions. The record actions go through the record
  command, as the record API does. `startProcess` and `completeTask` apply the
  checks of the process start and task endpoints without the HTTP layer.
- Only actions may declare `expect`. With no `expect`, an action must succeed.
  An unmet expectation fails the scenario:
  - `"denied"` means 403. A grant, a field grant or a record filter refused
    the action. For `startProcess` it is a missing start grant. For
    `completeTask` it is a user who is not the task's assignee.
  - `"notFound"` means 404. The record is outside every read filter, or it does
    not exist, for example after a `deleteRecord` step. For `completeTask` it is
    also a step with no task yet.
  - `"conflict"` means 409, for a unique value, a referenced record, a stale
    version, a process that is already running for the record or a task that is
    already completed.
  - `{ "invalid": "lineItems/0/quantity" }` means a 400 validation problem with
    an error at that path under `/values/`. Other errors may come with it. For
    `completeTask`, a value for a field the task form does not make editable is
    such an error, for example `{ "invalid": "amount" }`.
- A step that has `expect` may not use `save`, because a failed action creates
  no record.
- The runner supplies the record's current `version` on updates and task
  completions, so no step names one.
- `completeTask` and `expectTask` use the newest task of that step for the
  record. A returned request can create the same task again.
- `assignee` is `{ "user": … }` or `{ "role": … }`. `state` uses the instance
  and task states in [processes](processes.md#instance-states) and
  [task states](processes.md#task-states).
- Every reference to an entity, field, process, step, outcome, role, user or
  alias is checked at compile. See the **Scenarios** bullet of the
  [Resolve step](configuration.md#configuration-pipeline) for the checks and their codes.

`advanceTime`:

- `by` has the form of a task's `dueIn`: weeks, such as `P2W`, or days with an
  optional time part, such as `P1D`, `PT2H` or `P1DT12H`. It must be positive.
  Years and months are refused, because they have no fixed length.
- It needs `now`. The runner sets the new time on every connection of the
  scenario, including the work item runner's. Every later step reads it.
- The runner then runs every due work item (`process.step`, `process.timer`,
  `connector.call`, `package.call`, `event.deliver`, `trigger.schedule` and
  `trigger.fanOut`) until none is due. A `process.timer` item is due when the
  scenario's time reaches its time, so a timer resumes its instance in the
  `advanceTime` that passes it.
  If more than 1,000 items run in one step, the scenario fails at that step.
- A schedule that missed several occurrences runs only the latest one, once, as
  a worker does after an outage. See [schedules](process-events.md#schedules). To
  fire a daily schedule three times, advance one day three times.
- Scenario activation writes the schedule state of each schedule trigger, so
  the first occurrence is computed from the scenario's `now`, not the real
  clock.
- Schedule triggers run only in an `advanceTime` step. A scenario stays
  repeatable, because no schedule fires on the real clock.
- `advanceTime` is not an action. It takes no `as` and no `expect`.
- Time never goes back. No step sets an earlier time.

This example uses a daily schedule trigger. The purchase request sample does
not hold this trigger. The trigger submits the drafts at 18:00 in Ho Chi Minh
City:

```json
{
  "id": "5c0f6b0e-3a7d-4f0e-9a52-6d2b8f3e7c41",
  "kind": "trigger",
  "name": "SubmitDraftsDaily",
  "formatVersion": 1,
  "type": "schedule",
  "schedule": "0 18 * * *",
  "timeZone": "Asia/Ho_Chi_Minh",
  "dataSource": "PurchaseRequestList",
  "parameters": { "statusFilter": "draft" },
  "process": "PurchaseRequestApproval"
}
```

The scenario starts at 16:00 local time. Nothing has started yet. After it
advances three hours, the schedule has fired and the manager has a task:

```json
{
  "id": "0b7d9a52-1c64-4e8f-8d3a-92f1c5a6e7b3",
  "kind": "scenario",
  "name": "DailySubmitStartsApproval",
  "formatVersion": 1,
  "now": "2026-10-01T09:00:00Z",
  "users": {
    "anna": ["employee"],
    "binh": ["department-manager"]
  },
  "steps": [
    {
      "type": "createRecord",
      "entity": "PurchaseRequest",
      "as": "anna",
      "save": "request",
      "values": {
        "title": "Laptops",
        "department": "15d5bba1-eaf1-431b-9001-e1e3ade50979",
        "supplier": "06e1abca-17bb-4a84-a05d-508f7428a7e5",
        "currency": "USD",
        "status": "draft",
        "lineItems": [{ "description": "Laptop", "quantity": 3, "unitPrice": 5000 }]
      }
    },
    { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "expect": "notFound" },
    { "type": "advanceTime", "by": "PT3H" },
    { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "binh" } }
  ]
}
```

To run the scenarios of an application folder, see
[D32](../decisions/d32-test-scenarios.md):

```sh
dotnet run --project src/Axis.Scenarios -- <folder> --server <connection string> [--extensions <folder>] [--modules <folder>] [--results <folder>]
```

The runner runs the scenarios one at a time. Each gets a new
`axis_scenario_<uuid>` database, which the runner drops when the scenario ends,
whether it passed or failed. The `--server` login must be allowed to create
databases. A scenario runs as the tenant `scenario`, with no deployment
real bindings. The runner API reads the manifests of the extension packages it is
given and runs none of their code. `--extensions` names the folder of installed
packages, which plays the role of `Extensions:Path`. The command compiles the
application against them. It prints the problems it finds with a package and carries
on. Without the option no package is installed, so an application that declares a
package does not compile. `--modules` names the folder of installed modules,
which plays the role of `Modules:Path`, and works the same way for modules. A
missing folder is a setup error. A user's display name is their id.

`scripts/integration.sh` runs the scenarios of every application under
`samples/apps/` and of every sample template under `samples/templates/` from the
integration tests, on their Testcontainers PostgreSQL.

What runs now:

- `createRecord`, `updateRecord` and `deleteRecord`, with or without `expect`.
  With no `expect`, any outcome other than success fails the scenario. With
  `expect`, any other outcome fails it. The failure names the expectation and the
  actual outcome as the record command reports it, followed by any field errors,
  for example `Step 2 (createRecord): expected denied but the outcome was Written`
  or `Step 1 (createRecord): expected invalid at '/values/quantity' but the outcome
  was Invalid: /values/title Required.`. A taken unique value is reported as
  `Conflict (UniqueValue)`.
- `expectRecord`. Numbers compare by value, so 5000 equals 5000.00. Date-times
  compare as instants. A child collection must have as many rows as expected, and
  each expected row is compared on the fields it names.

- `startProcess` and `completeTask`, with or without `expect`. They apply the
  same checks as the API: the start grant, the read access to the record, the
  start condition and a running instance for `startProcess`, and the assignee,
  the task state and the task form for `completeTask`. `completeTask` writes its
  `values` under the task form rules, as the task API does. Only an editable
  field the user may read can be set. The values are written as a record update,
  then the data change triggers start. A start whose condition is not true fails
  the scenario with `ConditionFailed`, and no `expect` matches it.
- `expectInstance` and `expectTask`. They read the newest instance or task of
  the record. A failure names both values, for example
  `Step 4 (expectTask): task 'managerApproval' expected assignee user 'binh' but was user 'dung'`
  or `instance of process 'PurchaseRequestApproval' expected state completed but
  was waiting at step 'managerApproval'`. `step` on `expectInstance` is compared
  ignoring letter case.
- **Process steps.** After each action, the runner runs the `process.step` work
  items of the scenario's tenant in-process, until none is due. So the process
  reaches its next wait: an open task, or its end. The runner runs the due items
  in the order they were queued, so a clock change cannot reorder them. If one action makes more than
  1,000 items run, of any kind, the scenario fails at that step.
- **Timers.** The same loop runs the `process.timer` items. The runner never
  makes a timer due early, as it does for the other kinds. A timer's time comes
  from the scenario's clock. So after an action, a timer with a time in the
  past resumes its instance, and one with a time in the future waits for
  `advanceTime`.
- **Connector and package calls.** The same loop runs the `connector.call` and
  `package.call` items. They are the real handlers, so output mapping, resume,
  history, audit records and data change triggers behave as they do on a worker.
  Only the HTTP request of a connector call and the package operation are replaced
  by the scenario's `stubs`. See `stubs` above.
- **Event deliveries.** The same loop runs the `event.deliver` items with the real
  handler. So an `async` event trigger starts its process, and that process runs
  to its next wait before the next step. See
  [event delivery](process-events.md#event-delivery).
- **The current time.** With `now` set, the runner gives every connection of the
  scenario the PostgreSQL setting `axis.now`. The transaction time reads it
  instead of PostgreSQL `now()`. So `now()` in expressions, in policy and data
  source filters, task due dates, step history times and the year of a sequence
  number all use it. Timestamps that PostgreSQL writes itself, such as
  `created_at`, keep the real clock.
- **Schedule triggers.** They run only through [`advanceTime`](test-scenarios.md),
  as described above. After an action the `trigger.schedule` and `trigger.fanOut`
  items stay queued and do not fail the scenario.

A failure names the step and what differed, for example
`Step 3 (expectRecord): field 'quantity' expected 3 but was 4`. The runner stops
a scenario at its first failing step.

It writes `scenarios-<application name>.xml`, a JUnit file with one test case per
scenario, to `artifacts/test-results/`, or to the folder of `--results`. The exit
code is:

- `0` when every scenario passed.
- `1` when a scenario failed.
- `2` when the arguments are wrong or the folder does not compile. The runner
  prints the problems and writes no JUnit file.
- `130` when the run was cancelled.
