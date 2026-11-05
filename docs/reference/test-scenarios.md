# Test scenarios

A test scenario is a configuration-level test: a `scenario` resource that acts as users of the
application and checks what its records, processes, tasks and notifications do, against a real
database. This page covers the scenario file, its steps, how the runner runs it and what it reports.
The reasons for the design are in [D32](../decisions/d32-test-scenarios.md).

## Scenario files

A scenario is a file of the application folder whose `kind` is `scenario`, which names its users, its
time and the answers of the outside systems it calls, then lists its steps.

- **Place.** Scenario files live under `scenarios/`, one file per scenario, though a file is a
  scenario because of its `kind`, wherever it is in the folder. Scenarios compile with the
  application but stay out of the release and its content hash.
- **Callers.** The runner is a library that runs a scenario from a folder or from files held in
  memory, such as a snapshot. The `Axis.Scenarios` command is one caller, as
  [running the scenarios](#running-the-scenarios) says, and Studio runs scenarios through the
  publisher, as [running tests](studio.md#running-tests) says.

This scenario submits a purchase request above the finance threshold. The manager approves it, the
finance reviewer takes the finance decision and the request ends `approved`:

```json
{
  "id": "ee1ac8c8-74c5-4105-867a-1090a15746a7",
  "kind": "scenario",
  "name": "FinanceApprovalAboveThreshold",
  "formatVersion": 1,
  "now": "2026-10-01T09:00:00Z",
  "users": {
    "requester": ["employee"],
    "manager": ["department-manager"],
    "employee": ["finance-reviewer"]
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
      "as": "requester",
      "save": "request",
      "values": {
        "title": "Laptops",
        "department": "949c1d11-aaba-44aa-b0d1-6c9d659d6bd5",
        "supplier": "06e1abca-17bb-4a84-a05d-508f7428a7e5",
        "currency": "USD",
        "status": "draft",
        "lineItems": [{ "description": "Laptop", "quantity": 3, "unitPrice": 5000 }]
      }
    },
    { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "request", "as": "requester" },
    { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "manager" } },
    { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "manager" },
    {
      "type": "expectInstance",
      "process": "PurchaseRequestApproval",
      "record": "request",
      "state": "waiting",
      "step": "financeApproval"
    },
    { "type": "expectTask", "record": "request", "step": "financeApproval", "assignee": { "role": "finance-reviewer" } },
    { "type": "completeTask", "record": "request", "step": "financeApproval", "outcome": "approve", "as": "employee" },
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

- **`users`** maps a user id to its role names. Each role must be a declared
  [`role`](roles-and-policies.md). No identity provider takes part: the runner gives each step the
  user's id and roles, as a session would.
- **Module scenarios.** A scenario in a module names role slots of the module instead of roles, and
  each becomes the role the application maps it to, see
  [module scenarios](modules-and-templates.md#processes-pages-policies-and-dependencies). A step of a
  module scenario names an entity of its own module by plain name, or an entity of a module that the
  module declares as `<module id>/<name>`, and a process is always the module's own. An assignee role
  and a recipient role are slots too. A name that is not a slot is `AXC0335`.
- **`contract`** is for a module's scenario only. Its `covers` lists the named points of the module's
  contracts that the scenario tests, which makes it a contract scenario, see
  [contract scenarios](module-contracts.md#contract-scenarios). A point the module lacks is
  `AXC0339`, and an application's own scenario with `contract` is `AXC0340`.
- **`now`** is a date-time with an offset. It fixes the time that expressions, filters, sequence
  numbers and task due dates read, for the whole scenario. Timestamps that PostgreSQL writes, such
  as `created_at`, keep the real clock.
- **`stubs`** hold one answer per connector operation or package operation, as
  [stubs](#stubs) says.

## Stubs

A stub stands in for an outside system or a package operation, so a scenario never calls a real
system and runs no package code.

- **Matching.** A stub names a `connector` with its `operation`, or a package operation written
  `<package>/<operation>`. A connector stub matches the connector and the operation ignoring letter
  case. A package stub matches the package id exactly and the operation ignoring letter case. One
  stub answers every call of its operation with the same output.
- **Answer.** A stub gives either an `output` or `"outcome": "failed"`, never both.
  - `output` is the answer of a successful call. The call writes it to the subject fields and the
    process moves on, as a real call would. A connector stub answers with a flat body keyed by
    output name, so an output with a `source` finds nothing in it, and the call is left `unknown`.
  - `"outcome": "failed"` makes the call fail at once with no retry. The instance ends `failed`,
    which `expectInstance` can check.
- **No stub.** A call that is reached and has no stub fails the scenario at that action, and the
  failure names the process step and the operation.
- **Checks.** The compile step checks that a stub names an operation and its outputs, not the
  output's value types. A connector output that does not fit leaves the call `unknown` or retried,
  and a package output that does not fit fails the instance.
- **Bindings.** The scenario tenant has placeholder deployment bindings for every connector, package
  and identity provider binding the application names (the `issuer`, `clientId`, `clientSecret` or
  `clientKey` of an `oidc` provider and the `metadata` of a `saml` provider), never a real URL or
  secret.

## Steps

Every step has a `type`, and the steps run in order. These are the step kinds:

| Step                 | Properties                                                      | What it does                                                                                           |
| -------------------- | --------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `createRecord`       | `entity`, `values`, `as`, optional `save`, `expect`             | Creates a record through the record command. `save` gives it an alias for later steps.                |
| `updateRecord`       | `record`, `values`, `as`, `expect`                              | Updates the saved record through the record command.                                                   |
| `deleteRecord`       | `record`, `as`, `expect`                                        | Deletes the saved record through the record command.                                                   |
| `startProcess`       | `process`, `record`, `as`, `expect`                             | Starts the process on the saved record.                                                                |
| `completeTask`       | `record`, `step`, `outcome`, optional `values`, `as`, `expect`  | Completes the task of that step with the outcome.                                                      |
| `expectRecord`       | `record`, `values`                                              | Asserts that the saved record holds these values.                                                      |
| `expectInstance`     | `process`, `record`, `state`, optional `step`                   | Asserts the state of the process instance on the record, and the step it waits at.                     |
| `expectTask`         | `record`, `step`, `assignee` or `state`                         | Asserts who the task is assigned to, or its state.                                                     |
| `expectNotification` | `recipient`, optional `channel`, optional `values`              | Asserts that the process notified the user, role or email address, on the channel, with these values. |
| `advanceTime`        | `by`                                                            | Moves the scenario's `now` forward by `by`, then runs the work items that are due.                    |

### Actions

`createRecord`, `updateRecord`, `deleteRecord`, `startProcess` and `completeTask` are the actions:
they act as a user, as the record API, the process start and the task API would.

- **Record actions.** The record actions go through the record command, as the record API does.
- **`startProcess` and `completeTask`** apply the checks of the process start and task endpoints
  without the HTTP layer: the start grant, the read access to the record, the start condition and a
  running instance for `startProcess`, and the assignee, the task state and the task form for
  `completeTask`. A start whose condition is not true fails the scenario with `ConditionFailed`, and
  no `expect` matches it.
- **Task values.** `completeTask` writes its `values` under the task form rules, as the task API
  does, so only an editable field the user may read can be set. The values are written as a record
  update, then the data change triggers start.
- **Names.** `entity` and `process` name a resource of the application, or of a module it declares,
  written `<module id>/<name>` such as `department-budgets/Budget`. In a module's scenario, `entity`
  names an entity of the module or of a module it declares, and `process` names a process of the
  module only.
- **Versions.** The runner supplies the record's current `version` on updates and task
  completions, so no step names one.
- **Newest task.** `completeTask` and `expectTask` use the newest task of that step for the record,
  because a returned request can create the same task again.

### Expectations of an action

Only actions may declare `expect`. With no `expect`, an action must succeed, and with one, any other
outcome fails the scenario.

- **`"denied"`** means 403. A grant, a field grant or a record filter refused the action. For
  `startProcess` it is a missing start grant, and for `completeTask` a user who is not the task's
  assignee.
- **`"notFound"`** means 404. The record is outside every read filter, or it does not exist, for
  example after a `deleteRecord` step. For `completeTask` it is also a step with no task yet.
- **`"conflict"`** means 409, for a unique value, a referenced record, a stale version, a process
  that is already running for the record or a task that is already completed.
- **`{ "invalid": "lineItems/0/quantity" }`** means a 400 validation problem with an error at that
  path under `/values/`, and other errors may come with it. For `completeTask`, a value for a field
  the task form does not make editable is such an error, for example `{ "invalid": "amount" }`.
- **No `save`.** A step that has `expect` may not use `save`, because a failed action creates no
  record.

### Values and aliases

A reference in a step's `values` may name a record that an earlier step saved, by its alias.

- **Aliases.** A reference value in `values`, an item of a references field and a reference inside a
  child row may be a UUID or the alias (the `save` name) of a record that an earlier step saved, as
  in `"values": { "tag": "red", "tags": ["red", "blue"] }`. The alias must be of the field's target
  entity, and a step cannot use the alias it saves itself. In `createRecord`, `updateRecord`,
  `completeTask` and `expectRecord`, the runner replaces the alias with the record's id.
- **Ids.** A string in UUID form is always an id, and any other string is an alias. A value that is
  neither is `AXC0189`.
- **Compile checks.** Every reference to an entity, field, process, step, outcome, role, user or
  alias is checked at compile. See the **Scenarios** bullet of the
  [Resolve step](configuration.md#configuration-pipeline) for the checks and their codes.

### Assertions

`expectRecord`, `expectInstance` and `expectTask` read the state the actions left and compare it with
what the step expects.

- **`expectRecord`.** Numbers compare by value, so 5000 equals 5000.00, and date-times compare as
  instants. A child collection must have as many rows as expected, and each expected row is
  compared on the fields it names.
- **`expectInstance` and `expectTask`** read the newest instance or task of the record. `step` on
  `expectInstance` is compared ignoring letter case.
- **`assignee`** is `{ "user": … }` or `{ "role": … }`. `state` uses the
  [instance states](process-states.md#instance-states) and
  [task states](process-states.md#task-states).

## Expecting a notification

`expectNotification` checks that a process notified someone, in the app or by email, with the values
the step expects. It takes no `as` and no `expect`, and it is not an action.

- **Recipient.** `recipient` is exactly one of `{ "user": … }`, `{ "role": … }` and
  `{ "email": … }`. The user is a user of the scenario and the role is a declared role. `channel` is
  `inApp` or `email`, and without it the step reads both channels. `values` are the raw placeholder
  values of the `notify` step.
- **Where it reads.** In-app notifications are read from `axis.notifications`, for a `user` or a
  `role` recipient. Emails are read from `axis.email_outbox`, for a `user` or an `email` recipient,
  whether they are still `pending` or not. The address of a `user` is `<user>@scenario.invalid`, and
  addresses match ignoring letter case. A `role` with `channel: email`, or an `email` with
  `channel: inApp`, compiles and fails at run time, because no such notification exists.
- **Newest.** The step checks the newest notification of the recipient on the channel, as
  `expectTask` checks the newest task. Without `channel` it takes the newest of both channels.
- **Values.** Numbers in `values` compare by value, so 5000 equals 5000.00. Every other value
  compares by exact JSON equality, because a notification stores its values raw, with dates as ISO
  text. A key the notification does not hold fails the step. An email keeps the raw values of its
  step run in the outbox, so an email-only `notify` step can be checked too.
- **Failures.** A failure names the recipient and channel. It names the differing value with the
  expected and actual values, for example
  `Step 4 (expectNotification): notification to user 'requester' by email expected value 'title' "Chairs" but was "Desks"`.
  When nothing matches it says `no email notification to user 'requester' was found`.
- **Emails stay stubbed.** The scenario tenant has no mail host, so nothing is sent. The
  `notification.email` item runs in an `advanceTime` step, which makes it due once, after the other
  work is done, even when the scenario's `now` is before the time the application clock queued it.
  It logs the no-mail-host warning, and the email stays `pending`, where `expectNotification` still
  finds it.
- **Webhooks are never sent.** A [raise event step](process-events.md#raise-event-step) in a
  scenario still stages one delivery for each webhook of its event, but `advanceTime` does not run
  `webhook.deliver` items. They stay queued, and the rows in `axis.webhook_deliveries` stay
  `pending`, so no scenario calls an outside system. See
  [webhook delivery](process-events.md#webhook-delivery).

## Advancing time

`advanceTime` moves the scenario's clock forward and runs the work that becomes due, so a scenario
can test timers, schedules, escalations and deadlines without waiting. It takes no `as` and no
`expect`, and it is not an action.

- **`by`** has the form of a task's `dueIn`: weeks, such as `P2W`, or days with an optional time
  part, such as `P1D`, `PT2H` or `P1DT12H`. It must be positive. Years and months are refused,
  because they have no fixed length.
- **Clock.** It needs `now`. The runner sets the new time on every connection of the scenario,
  including the work item runner's, and every later step reads it. Time never goes back: no step
  sets an earlier time.
- **Due work.** The runner then runs every due work item (`process.step`, `process.timer`,
  `process.resume`, `connector.call`, `package.call`, `event.deliver`, `trigger.schedule`,
  `trigger.fanOut`, `task.escalate`, `task.deadline` and `notification.email`) until none is due. A
  `process.timer` item is due when the scenario's time reaches its time, so a timer resumes its
  instance in the `advanceTime` that passes it. If more than 1,000 items run in one step, the
  scenario fails at that step.
- **Schedules.** Schedule triggers run only in an `advanceTime` step, so no schedule fires on the
  real clock and a scenario stays repeatable. After an action the `trigger.schedule` and
  `trigger.fanOut` items stay queued and do not fail the scenario. Scenario activation writes the
  schedule state of each schedule trigger, so the first occurrence is computed from the scenario's
  `now`, not the real clock.
- **Missed occurrences.** A schedule that missed several occurrences runs only the latest one, once,
  as a worker does after an outage, see [schedules](process-events.md#schedules). To fire a daily
  schedule three times, advance one day three times.
- **Deadlines and escalations.** A task's `deadline` counts from the step's time, which follows the
  scenario's `now`. The task expires only in an `advanceTime` step that moves `now` to its deadline
  or past it, and no action runs a deadline early. The instance then moves to the deadline's `next`
  step, which `expectInstance` can check. An escalation's `reassign` and `notify` count from the
  same time and run in the `advanceTime` step that reaches its `after`, so `expectTask` sees the new
  assignee before the deadline passes. See
  [task deadlines](task-deadlines.md#escalations-and-deadline).

This example uses a daily schedule trigger, which the purchase request sample does not hold. The
trigger submits the drafts at 18:00 in Ho Chi Minh City:

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

The scenario starts at 16:00 local time, and nothing has started yet. After it advances three hours,
the schedule has fired and the manager has a task:

```json
{
  "id": "0b7d9a52-1c64-4e8f-8d3a-92f1c5a6e7b3",
  "kind": "scenario",
  "name": "DailySubmitStartsApproval",
  "formatVersion": 1,
  "now": "2026-10-01T09:00:00Z",
  "users": {
    "requester": ["employee"],
    "manager": ["department-manager"]
  },
  "steps": [
    {
      "type": "createRecord",
      "entity": "PurchaseRequest",
      "as": "requester",
      "save": "request",
      "values": {
        "title": "Laptops",
        "department": "949c1d11-aaba-44aa-b0d1-6c9d659d6bd5",
        "supplier": "06e1abca-17bb-4a84-a05d-508f7428a7e5",
        "currency": "USD",
        "status": "draft",
        "lineItems": [{ "description": "Laptop", "quantity": 3, "unitPrice": 5000 }]
      }
    },
    { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "manager", "expect": "notFound" },
    { "type": "advanceTime", "by": "PT3H" },
    { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "manager" } }
  ]
}
```

## Running the scenarios

The `Axis.Scenarios` command runs the scenarios of an application folder, each in a database of its
own:

```sh
dotnet run --project src/Axis.Scenarios -- <folder> --server <connection string> [--extensions <folder>] [--modules <folder>] [--results <folder>]
```

- **Databases.** The runner runs the scenarios one at a time. Each gets a new
  `axis_scenario_<uuid>` database, which the runner drops when the scenario ends, whether it passed
  or failed, so the `--server` login must be allowed to create databases.
- **Tenant.** A scenario runs as the tenant `scenario`, with the placeholder bindings of
  [stubs](#stubs) and no real ones. The tenant has a random data key and lookup key that live only
  in memory for the run, so sensitive values are encrypted as on a real tenant.
- **Users.** A user's display name is their id, and each user has a profile with the address
  `<user>@scenario.invalid`, so a `notify` step stages an email for a `user` recipient.
- **Packages.** `--extensions` names the folder of installed packages, which plays the role of
  `Extensions:Path`. The runner reads the manifests of the packages it is given and runs none of
  their code, and the command compiles the application against them. It prints the problems it
  finds with a package and carries on. Without the option no package is installed, so an
  application that declares a package does not compile.
- **Modules.** `--modules` names the folder of installed modules, which plays the role of
  `Modules:Path`, and works the same way for modules. A missing folder is a setup error.
- **CI.** `scripts/integration.sh` runs the scenarios of every application under `samples/apps/`
  from the integration tests, on their Testcontainers PostgreSQL.

## How the runner runs a scenario

After each action the runner runs the work the action queued, in-process, so the process reaches its
next wait before the next step, as it would on a worker.

- **Process steps.** After each action, the runner runs the `process.step` work items of the
  scenario's tenant until none is due, so the process reaches its next wait: an open task, or its
  end. The runner runs the due items in the order they were queued, so a clock change cannot
  reorder them. If one action makes more than 1,000 items run, of any kind, the scenario fails at
  that step.
- **Timers.** The same loop runs the `process.timer` items. The runner never makes a timer due
  early, as it does for the other kinds, because a timer's time comes from the scenario's clock. So
  after an action, a timer with a time in the past resumes its instance, and one with a time in the
  future waits for `advanceTime`.
- **Connector and package calls.** The same loop runs the `connector.call` and `package.call` items.
  They are the real handlers, so output mapping, resume, history, audit records and data change
  triggers behave as they do on a worker. Only the HTTP request of a connector call and the package
  operation are replaced by the scenario's [stubs](#stubs).
- **Event deliveries.** The same loop runs the `event.deliver` items with the real handler, so an
  `async` event trigger starts its process, and that process runs to its next wait before the next
  step. See [event delivery](process-events.md#event-delivery).
- **The current time.** With `now` set, the runner gives every connection of the scenario the
  PostgreSQL setting `axis.now`, which the transaction time reads instead of PostgreSQL `now()`. So
  `now()` in expressions, in policy and data source filters, task due dates, step history times and
  the year of a sequence number all use it. Timestamps that PostgreSQL writes itself, such as
  `created_at`, keep the real clock.

## Results

The runner stops a scenario at its first failing step, names the step and what differed, and writes
a JUnit file for the whole run.

- **Failures.** A failure names the step and what differed, for example
  `Step 3 (expectRecord): field 'quantity' expected 3 but was 4`.
- **Actions.** The failure of an action names the expectation and the actual outcome as the record
  command reports it, followed by any field errors, for example
  `Step 2 (createRecord): expected denied but the outcome was Written` or
  `Step 1 (createRecord): expected invalid at '/values/quantity' but the outcome was Invalid: /values/title Required.`.
  A taken unique value is reported as `Conflict (UniqueValue)`.
- **Instances and tasks.** A failure of `expectInstance` or `expectTask` names both values, for
  example
  `Step 4 (expectTask): task 'managerApproval' expected assignee user 'manager' but was user 'finance-reviewer'`
  or
  `instance of process 'PurchaseRequestApproval' expected state completed but was waiting at step 'managerApproval'`.
- **Module scenarios.** Each module the application pins may hold scenarios, see
  [module scenarios](modules-and-templates.md#processes-pages-policies-and-dependencies). They run
  after the application's own, for each module in pin order and then by file path. A result is
  named `<module id>/<scenario name>`, in the output, the JUnit file and the Studio test results.
- **JUnit file.** The command writes `scenarios-<application name>.xml`, a JUnit file with one test
  case per scenario, to `artifacts/test-results/`, or to the folder of `--results`.

The exit code is:

| Exit code | Meaning                                                                                                          |
| ----- | ---------------------------------------------------------------------------------------------------------------- |
| `0`   | Every scenario passed                                                                                            |
| `1`   | A scenario failed                                                                                                |
| `2`   | The arguments are wrong or the folder does not compile. The runner prints the problems and writes no JUnit file |
| `130` | The run was cancelled                                                                                            |
