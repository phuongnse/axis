# Process execution

Detailed reference for how the worker runs processes: execution and the running of a step,
connector calls, package operations and the worker settings with its metrics.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

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
  A `callConnector` step retries its call under its retry policy, see
  [Connector calls](#connector-calls). So does a package operation step, see
  [Package operations](#package-operations). An operator can retry a `failed`
  instance, see [Retry](process-inspection.md#retry).
- **Revision.** An instance's revision is a counter that starts at 1 and goes
  up each time the instance changes. A step changes the instance only at the
  revision it loaded.
- **Work item errors.** When a work item's handler throws, its writes roll
  back. A following transaction, still checked against the claim token,
  deletes the item and calls the handler's failure callback with the error.
  The item is not retried. A worker that crashes or stops before it commits
  calls no failure callback. Its item is claimed again after the lease
  expires.
- **Calls outside a transaction.** A `connector.call` item runs with no
  transaction open while its request is in flight. A `package.call` item runs
  with none open while its operation runs. It opens its own short
  transactions, before and after the request, and each checks the claim
  token. See [Staging and the call](#staging-and-the-call).
- **Restarts.** An instance that waits on a task has no work item, so it
  needs no running worker. One that waits on a connector call has the call's
  `connector.call` item, and one that waits on a package call has its
  `package.call` item, and one that waits on a timer has its `process.timer`
  item. A task decision is accepted while no worker runs: it
  queues the next step's work item. The next worker to start runs that step. A step that
  a worker never committed leaves no trace, and the worker that claims it after
  the lease runs it once.
- **History.** Every step occurrence records its input, its output, the
  decision taken and any error.

### Running a step

The worker runs the `decision`, `task`, `operation`, `subProcess`, `end` and `timer` steps. A
`callConnector` operation step stages its call, and a `connector.call` item
sends it, see [Staging and the call](#staging-and-the-call). A package
operation step stages its call, and a `package.call` item runs it, see
[Staging a package call](#staging-a-package-call). A `raiseEvent`
operation step stores its event, see [Raise event step](process-events.md#raise-event-step).
A `timer` step sets the instance `waiting`, and a `process.timer` item resumes
it, see [Timer step](process-flow.md#timer-step).
A `task.deadline` item expires the task of a task step that has a `deadline`,
see [task deadlines](task-deadlines.md#escalations-and-deadline).
A `subProcess` step starts its child and waits, and a `process.resume` item
resumes the parent when the child completes, see
[Sub-process step](process-flow.md#sub-process-step).
_(Planned for M14)_ The worker also runs `waitEvent` steps and the
`notify` operation, with the work item kinds
`task.escalate` and `notification.email`, see [process flow](process-flow.md),
[task deadlines](task-deadlines.md) and [notifications](notifications.md).
Each work item of kind `process.step` runs the current step of its instance in
one transaction:

1. It loads the instance. An instance that is missing or not `running` is
   skipped, and the item is deleted with no other write.
2. It reads the release the instance is pinned to, so an instance keeps the
   step graph it started with after a new release is activated.
3. It reads the subject record and runs the step. An `updateRecord`
   operation first locks the subject record's row, so it reads the latest
   stored version, and a user's write of the same record waits until the
   step's transaction ends. A `callConnector`, `raiseEvent` or `createRecord`
   step only reads the record, so it takes no lock on it. An `updateRecord`,
   `raiseEvent` or `createRecord` step takes the application's start lock
   shared first, since it can start an instance. A `createRecord` step in a
   process without a subject takes it too.
   Every read of the subject record computes a
   [pending field](migrations.md#recomputing-computed-fields) with the active
   release and does not store the result. That covers decisions, `set`
   expressions, connector and package inputs, `raiseEvent` fields and task
   assignees. A run-time error reads `null`. An instance pinned to an older
   release reads the same way, but a computed value replaces the stored one
   only when the field has the same name and type in both releases. An
   `updateRecord` step evaluates its `set` expressions on the computed values
   and writes from the stored record. An `updateRecord` write under a release
   that is out of date for the entity marks the field pending again, as
   described in
   [Recomputing computed fields](migrations.md#recomputing-computed-fields).
   - A **decision** evaluates its branches in order and takes the first `when`
     that is true. A `when` that gives `null` counts as false. When no branch
     is true, it takes `otherwise`. The instance stays `running` on the next
     step.
   - An **`updateRecord`** operation evaluates each `set` expression on the
     record. The instance stays `running` on its `next` step.
   - A **`callConnector`** operation evaluates each `input` expression on the
     record. A required input that gives `null` fails the step. The instance
     becomes `waiting` on the step.
   - A **package operation** step evaluates each `input` expression on the
     record. A required input that gives `null` fails the step. The instance
     becomes `waiting` on the step.
   - A **`createRecord`** operation evaluates each `set` expression on the
     record, or on the parameters of a process without a subject. A value that
     fails at run time fails the step. The instance stays `running` on its
     `next` step.
   - A **`raiseEvent`** operation evaluates each `fields` expression on the
     record. A payload field that gives `null` is stored as `null`. The
     instance stays `running` on its `next` step.
   - A **task** evaluates its assignee. A `user` expression gives the user id,
     and one that gives `null`, blank text or a run-time error fails the step.
     A `role` is the role name. The instance becomes `waiting` on the task
     step.
   - A **`timer`** step with a `wait` takes the time of the step's transaction
     plus `wait`. One with an `until` evaluates it on the record. An `until`
     that gives `null` or a run-time error fails the step. The instance becomes
     `waiting` on the step.
   - An **end** step sets the instance to `completed` with its `ended_at` time.

   `now()` in any of these expressions, a task's `assignee.user` included,
   gives the start time of the step's transaction. That is the same instant
   the history row stores as `started_at`. A validation that the operation's
   write runs sees it too. See
   [D22](../decisions/d22-current-time-and-time-zones.md).
4. It sets the new state and step, with the next revision, only if the
   instance is still `running` at the revision it loaded. Otherwise another
   transaction changed the instance first. The step then writes nothing else,
   and its work item is deleted, so it never runs again.
5. A task step then inserts its task into `axis.process_tasks` as `open`. Its
   `due_at` is the transaction time, `now()`, plus `dueIn`, or null without
   `dueIn`. `dueIn` is added as a fixed length of time. A task step with a
   `deadline` also queues one `task.deadline` work item for the task, due at
   the transaction time plus the deadline's `after`. See
   [task deadlines](task-deadlines.md#escalations-and-deadline).

   A `callConnector` step then inserts its call into `axis.connector_calls`
   as `pending`, see [Staging and the call](#staging-and-the-call).

   A package operation step then inserts its call into `axis.package_calls`
   as `pending`, see [Staging a package call](#staging-a-package-call).

   A `raiseEvent` step then inserts its occurrence into `axis.events`, see
   [Raise event step](process-events.md#raise-event-step).

   An `updateRecord` operation then writes the values the way a record
   update does. Each value must fit its field, and a `null` for a required
   field is refused. Computed fields are recomputed, validations run, and the
   record's version goes up by one. A refused write is an error, keyed and worded as in the
   [record API](record-api.md#child-rows-computed-fields-and-validations).

   A `createRecord` operation then creates its record in the step's
   transaction, as `system`, the way a record create does after its grant
   check: it applies the defaults of the fields `set` leaves out, sets each
   creator field to `system`, computes the computed fields, runs the
   validations and gives each sequence field its number. A `null` for a
   required field, and a required field with no value and no default, are
   refused with `Required.`. A refused create is an error that fails the step
   and leaves no record, no sequence number taken and no audit record. The
   history output is `{ "next", "recordId" }`. See
   [Create record operation](process-flow.md#create-record-operation).
6. It writes the step's history row and its audit records, all with the actor
   `system`:
   - An `updateRecord` operation first writes `record.updated` for the
     subject record, with the instance id. Its details are
     `{ "version", "fields" }`, as the
     [record API](record-api.md#audit-records-and-history) writes them.
   - A `createRecord` operation first writes `record.created` for the new
     record, with the instance id. Its details are `{ "version" }`.
   - A decision, an `updateRecord` operation, a `createRecord` operation and a
     `raiseEvent` operation write `process.stepCompleted` with details
     `{ "step", "next" }`.
   - A task step writes `task.created` with details `{ "step", "taskId" }`.
   - A `timer` step writes no audit record while it waits. No audit action
     fits waiting for a time. Its resume writes `process.stepCompleted`.
   - A `callConnector` step writes `connector.callStaged` with details
     `{ "step", "connector", "operation", "operationId" }`.
   - A package operation step writes `operation.staged` with details
     `{ "step", "package", "version", "operation", "operationId" }`.
   - An end step writes `process.completed` with details `{ "step" }`.
7. A decision, an `updateRecord` operation, a `createRecord` operation and a
   `raiseEvent` operation insert the work item of the next step. A task step inserts none, because
   the instance waits for the task. A `callConnector` step inserts a
   `connector.call` item for its call instead, and a package operation step
   inserts a `package.call` item. A `timer` step inserts a `process.timer`
   item due at the time of the timer, with the `revision` the step left.

Each work item of kind `process.timer` resumes one instance in one
transaction. It loads the instance and resumes it only while it is `waiting` on
a `timer` step at the item's `revision`. The instance then becomes `running`
on the step's `next`, with the next revision. The item writes the history row
`{ "next" }`, the audit record `process.stepCompleted` with details
`{ "step", "next" }` and the work item of the next step. An item whose instance
moved on, was cancelled or is missing is deleted with no other write. A
`process.timer` item that throws is rolled back, and a following transaction
sets the waiting instance to `failed`, writes a history row with the error and
the audit record `process.failed`, as for a step.

Any exception in the step, such as a `when` that divides by zero, a missing
subject record, a record write that a validation refuses or a task assignee
that gives no user, rolls all of it back. So the record keeps its values and
version, and no task, connector call, package call or event occurrence is stored. The
error of a refused write names each field pointer and message, such as
`The record update was rejected: /values/amount: request.amountNegative`. A
following transaction then sets the instance to `failed` with its `ended_at`
time and the next revision, writes a history row with the error, and writes
the audit record `process.failed` with
details `{ "step" }` and the actor `system`. It does nothing when the instance
has changed since the step loaded it. No step is retried.

The error in the history row is never the exception message, because a
message can hold a value. It is the exception type, such as
`System.DivideByZeroException`, or a fixed text that names the step, the part
and the kind of expression error, such as
`The when of branch 0 of the step 'check' failed: DivisionByZero.` A refused
write stores its field pointers and message keys, as above. See
[Step errors](process-inspection.md#step-errors-in-inspection).

The failure is also logged at Warning level with the step name, the instance id
and the same fixed text as the history row. A failed work item is logged with
its exception type only.

## Connector calls

This section follows
[D25](../decisions/d25-connectors-deployment-bindings-and-secrets.md).
The step shape, the compile checks, staging, each attempt, retries, unknown
outcomes, reconciliation and settling a successful or failed call are built,
see [Call outcomes](#call-outcomes) and [Reconciliation](#reconciliation). A `callConnector`
step calls one operation of a [connector](integration-resources.md#connectors-and-deployment-bindings), an external
system such as the purchasing system. The call never runs inside a database
transaction. The step stages it, the worker sends it later, and a separate
transaction records the result. A stable operation identity lets every retry
and every lookup ask about the same call, so a lost response never leads to a
second purchase order.

### Step shape

After approval, the purchase request process creates a purchase order.
`markApproved` names the step as its `next`:

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

- `connector` names a connector and one of its operations.
- `input` maps operation inputs to expressions. Each key is an input of the
  operation. Each value is an expression over the subject record, with the
  same scope as a decision's `when`.
  - _(planned for M17)_ An `object` input maps as `{ "fields": { … } }`, each
    key a nested field. A `list` input maps from a child collection of the
    subject as `{ "from": "<child collection>", "fields": { … } }`, and each
    item expression is over the child row. Each nested field is checked as a
    top-level input is. See
    [D91](../decisions/d91-connector-oauth2-and-structured-payloads.md) and
    [Connector resource](integration-resources.md#connector-resource).
- `output` is optional. It maps operation outputs to subject fields. Each key
  is an output of the operation, and each value names a field of the subject
  record.
- `retry` is optional.
  - `attempts` counts the first attempt. It defaults to 3 and is at most 10.
  - `backoff` is an ISO 8601 duration, in the same form as a task's `dueIn`.
    It is the delay before the second attempt, and it doubles each attempt,
    up to 1 hour. It defaults to `PT30S`.
- `next` names the step taken when the call succeeds.

The compiler reports these as compile errors. Every name is matched
ignoring letter case, and the compiled step holds the declared names.

- The connector exists (`AXC0102` at `/steps/{i}/connector/name`). A name
  whose connector file was not loaded because of its own errors is not
  reported again.
- The operation is an operation of the connector (`AXC0103` at
  `/steps/{i}/connector/operation`). With either error, `input` and `output`
  are not checked.
- Each `input` key names an input of the operation, at most once (`AXC0104`
  at `/steps/{i}/input/{key}`). Every required input has a key (`AXC0105` at
  `/steps/{i}/input`). `input` may be empty when the operation has no
  required input.
- Each `input` expression gives the input's type. A problem is reported at
  `/steps/{i}/input/{key}` with its expression code, such as `AXC0048` for a
  value of the wrong type.
- Each `output` key names an output of the operation, at most once (`AXC0106`
  at `/steps/{i}/output/{key}`).
- Each `output` value names a field of the subject entity that no other
  output of the step writes (`AXC0107`). The field is one an `updateRecord`
  `set` may name, so it is not computed, not numbered by a sequence and not a
  child collection. The output's type fits the field: it is the same type,
  or an `integer` output into a `decimal` field (`AXC0108`). Each is reported
  at `/steps/{i}/output/{key}`.
- `attempts` is from 1 to 10 (`AXC0004`), and `backoff` is a valid duration
  (`AXC0109` at `/steps/{i}/retry/backoff`).
- A `callConnector` step has `connector`, `input` and `next`, and no `set`.
  An `updateRecord` step has none of `connector`, `input`, `output` and
  `retry` (`AXC0004`).

The compiled step holds the connector, the operation, the inputs in the
operation's order, the outputs, the retry policy with its defaults filled in,
and `next`.

### Staging and the call

**The step's transaction stages the call.** It runs as other steps do, see
[Running a step](#running-a-step), and writes these together:

1. It evaluates each `input` expression on the subject record. A required
   input that gives `null` fails the step. An optional input that gives
   `null` is stored as `null`. Dates and date-times are stored in the text
   forms of the [record API](record-api.md).
2. It sets the instance to `waiting` on the step.
3. It inserts the call into `axis.connector_calls` as `pending`, with
   0 attempts and the evaluated input. The row's id is the call's operation
   identity, a version 7 UUID. It stores the instance revision the step ran
   at as `staged_revision`. A unique index on instance, step and
   `staged_revision` lets a step stage one call per pass of a
   [loop](process-flow.md#loops).
4. It writes the step's history row, with output
   `{ "state": "waiting", "operationId" }`.
5. It writes the audit record `connector.callStaged` with the actor `system`
   and details `{ "step", "connector", "operation", "operationId" }`.
6. It inserts a work item of kind `connector.call` for the call, due at once.
   The item names the instance and the call.

While its call is pending, retrying or unknown, the instance stays `waiting`.
So a second start for the same record is blocked, as with an open task.

**A step that runs again after a retry** finds that the latest call of the
step, the one with the highest `staged_revision`, is `failed`. It reuses that
row instead of inserting one, in the same transaction. A call of an earlier
pass of a loop is never reused. The row keeps its id, so
the operation identity and the idempotency key do not change. The step
evaluates the input again from the subject record as it is now, on purpose, so
the retry sends the current data. A failed connector call keeps its stored
input, but the step still evaluates it again. A failed package call's stored
input is cleared when it settles. It sets the call to
`pending` with that input, 0 attempts and 0 lookups, and clears the last status,
the last error and the settled time. It sets `staged_revision` to the revision
the step runs at again. Only a `failed` row is reused. The history
row, the audit record and the work item are written again as above, with the
same `operationId`. See [Retry](process-inspection.md#retry).

**Each attempt.** A worker that claims the `connector.call` item does this:

1. **Mark.** A short transaction marks the call `sending` and counts the
   attempt. It extends the item's lease to the connector's `timeout` plus
   `Worker:LeaseDuration`, so no other worker claims the item while the call
   runs.
2. **Bindings.** The worker reads the connector's bindings from its host
   configuration. A missing binding, or an `http` URL outside Development,
   means the request is never sent. Only a worker that runs in Development
   allows `http`. The error of an `http` URL names the tenant and the
   binding, never the URL.
3. **Send.** The worker sends the request outside any transaction.
   - The body is the stored input, as JSON. A `GET` sends no body.
   - The auth sends the secret as `Authorization: Bearer <secret>`, or in
     the named header.
   - With `idempotency`, the request carries the header
     `<idempotency>: <operationId>`, such as `Idempotency-Key: 0192f5c3-…`.
   - The request is cancelled after the connector's `timeout`.
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
   rules, as `updateRecord` does. An optional output that the body leaves
   out leaves its field unchanged, and an explicit `null` writes `null`. A
   refused write, such as a failed validation, fails the call instead.
2. It sets the call to `succeeded` and keeps its stored input, see
   [Call input kept](process-inspection.md#call-input-kept). It writes the attempt's row, see
   [Attempts](process-inspection.md#attempts).
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
`due_at` is now plus `backoff`·2^(n−1) after attempt n, capped at 1 hour. The
call row keeps the attempts, the last status and the last error, so a worker
that starts later sees them. A retry writes no history row and no audit record.

**An unknown call** is one whose outcome is unknown and that is not retried,
see [Call outcomes](#call-outcomes). The call becomes `unknown`, with the last
status and the error on the call row, and keeps its stored input. The instance
stays `waiting`. When the operation declares a `lookup`, the item is kept and
due at once, so a worker [reconciles](#reconciliation) the call. Without one,
the item is deleted, and a history row with output
`{ "operationId", "outcome": "unknown" }` and the error makes the call visible.
No `connector.called` is written, because the call has not settled.

**A failed call settles** too. The call becomes `failed` and keeps its stored
input, see [Call input kept](process-inspection.md#call-input-kept), with the last status and the
error on the call row. It writes the attempt's row too. The instance
becomes `failed`, with the error in a history row whose output is
`{ "operationId", "outcome": "failed" }`. The transaction writes
`connector.called`, with the same details as a successful call, and
`process.failed`, both with the actor `system`.

No request or response body is ever stored. The stored input lives only on
the call row. A connector call keeps it after the call settles, see
[Call input kept](process-inspection.md#call-input-kept). It never appears in history, audit
records or logs, and inspection shows it only under the
[masking rule](process-inspection.md#values-and-masking).

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
  counts as an attempt. The retry is due after the step's backoff for the
  attempts made, as any retry is. When no attempts are left, the instance
  becomes `failed`.
- **Failed lookup.** Any other answer, including another `2xx`, a `200` whose
  body does not fit, and a lookup that is never sent. The call stays
  `unknown`, with the lookup's status and error, and the lookup is tried again.
  The delay starts at `backoff` and doubles each time, up to 1 hour, with no
  limit. The call row counts the failed lookups in `lookups`, so the delay
  survives a worker restart. The count is reset when the call goes back to
  `pending`.
- **No lookup.** The operation declares none. The call stays `unknown`, and
  the instance stays `waiting`. A history row with output
  `{ "operationId", "outcome": "unknown" }` makes it visible. An operator
  resolves it, see [Resolve](process-inspection.md#resolve).

The first lookup runs at once after the unknown outcome. Only a failed lookup
waits. The same `connector.call` item runs each lookup, under the same claim
and fencing rules as an attempt. A lookup counts no attempt. No history row is
written while the call waits for its lookup. The call row shows it as
`unknown`, and the history row comes when the call settles.

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
   `connector.called` with `attempts` 2. The instance moves on to
   `assignPaymentReference`, which runs the sample package and then moves on to `done`.
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

## Package operations

This section follows
[D28](../decisions/d28-extension-packages.md). The step shape, its
compile checks, running the step and retries under the step's retry policy are
built, and so are the capability limits of the operation context. A package
operation step runs one operation of an
[extension package](extension-packages.md), custom C# code that the operator
installs. The step runs like a [connector call](#connector-calls): it stages
the call, the worker runs the operation outside any database transaction, and
a separate transaction records the result. This holds for every package
operation, even a pure one, so there is one rule for all of them.

### Package step shape

The examples use an illustrative package, `purchasing-tools`, with the
operation `normalizeSupplierCode` from the
[manifest example](extension-packages.md#manifest), and an illustrative
`supplierCode` text field on the subject. The sample package,
`payment-references`, is used the same way by the `assignPaymentReference` step
of the [purchase request sample](processes.md#resource-shape).

```json
{
  "name": "normalizeSupplier",
  "type": "operation",
  "operation": "purchasing-tools/normalizeSupplierCode",
  "input": { "code": "supplier.code" },
  "output": { "code": "supplierCode" },
  "retry": { "attempts": 3, "backoff": "PT30S" },
  "next": "financeCheck"
}
```

- `operation` is `<package>/<operation>`. The `/` marks a package operation.
  The part before it is a package id, and the part after it is an operation
  of that package.
- `input` maps operation inputs to expressions over the subject record, as
  for `callConnector`.
- `output` is optional. It maps operation outputs to subject fields, as for
  `callConnector`.
- `retry` is optional, with the same `attempts` and `backoff` as for
  `callConnector`.
- `next` names the step taken when the call succeeds.

The compiler reports these as compile errors.

- The package is one of the application's
  [`packages`](extension-packages.md#declaring-packages-in-an-application). The package id is
  matched exactly. Otherwise it is `AXC0150` at `/steps/{i}/operation`. A
  listed package that failed pinning (`AXC0139` to `AXC0142`) is not
  reported again, and neither is any package when `application.json` was not
  loaded.
- The operation is in the manifest of the version the release pins,
  matched ignoring letter case. Otherwise it is `AXC0151` at
  `/steps/{i}/operation`. The compiled step holds the manifest's declared
  name.
- `input`, `output` and `retry` are checked as for a `callConnector` step
  (`AXC0104` to `AXC0109`), see [Step shape](#step-shape), against the
  operation's fields in the manifest.
- A package operation step has `input` and `next`, and no `set`, `connector`,
  `event` or `fields` (`AXC0004`).

The compiled step holds the package id, the pinned version, the operation,
the inputs and outputs in manifest order, the retry policy with the defaults
filled in, `next`, and the manifest's `idempotent` and `timeout`.

### Staging a package call

The steps are those of [Staging and the call](#staging-and-the-call), with
the operation in place of the HTTP request:

- **Staging.** The step's transaction evaluates the input, stores the call
  as `pending` with a stable operation identity and the evaluated input, sets
  the instance to `waiting` on the step, and queues a work item. A unique
  index on instance, step and `staged_revision` lets a step stage one call per
  pass of a [loop](process-flow.md#loops). The table is
  `axis.package_calls` and the work item kind is `package.call`. The step
  writes the history row `{ "state": "waiting", "operationId" }` and the audit
  record `operation.staged`. A required input that gives `null` fails the
  step. The call also stores the step's retry policy and the operation's
  `idempotent` flag, because a worker that lacks the pinned version cannot
  read them from the release.
  A step that runs again after an operator [retry](process-inspection.md#retry) reuses its
  `failed` row, as a connector call does, under the same operation identity.
  It stores fresh input, the step's current retry policy, the operation's
  `idempotent` flag, the new `staged_revision` and 0 attempts, and clears the
  last error and the settled time.
- **Mark.** Before each attempt, a short transaction marks the call
  `sending`, counts the attempt and extends the item's lease past the
  operation's `timeout`.
- **Run.** The worker loads the pinned package version, resolves the
  `url` and `secret` bindings the operation declares for the tenant, and runs
  the operation with the stored input, outside any transaction. _(planned for
  M17)_ Outside Development, the worker sends the attempt to a
  [package runner](extension-packages.md#package-runners) instead of loading
  the version itself, and kills the runner at the `timeout`. The context's
  `OperationId` is the operation identity, the same on every attempt. The
  context hands out only the declared bindings, see
  [Capabilities](extension-packages.md#capabilities). The
  worker passes a cancellation token that is cancelled at the `timeout`.
- **Record.** An outcome transaction records the result, as for a connector
  call. On success it writes the output fields with the record update rules,
  sets the instance to `running` on `next`, writes the history row and the
  audit records, and queues the next step.
- **Stored input.** It is cleared when the call settles, and never appears in
  history, audit records or logs.

A worker can crash after it runs the operation and before it records the
result. The next worker then finds the call still `sending`, and that
attempt's outcome counts as unknown.

### Package call outcomes

The platform decides which outcomes are retried. A retry runs the operation
again with the stored input under the same operation identity.

| Outcome of an attempt | Class | Retried | When attempts run out |
| --- | --- | --- | --- |
| The output fits the manifest | Succeeded | Not needed | Not needed |
| The output does not fit the manifest | Failed | No | The instance becomes `failed` at once |
| A `RetryableException` | Retryable | Yes, while attempts are left | The instance becomes `failed` |
| Any other exception, or a request to the context for an undeclared capability | Failed | No | The instance becomes `failed` at once |
| The pinned version is missing on the worker | Retryable | Yes, while attempts are left | The instance becomes `failed` |
| A declared binding the tenant lacks, or an `http` url outside Development | Retryable | Yes, while attempts are left | The instance becomes `failed` |
| The release lacks the step, or the installed manifest lacks the operation | Failed | No | The instance becomes `failed` at once |
| A timeout, or a call found still `sending` | Unknown | Only when the operation declares `idempotent: true` and attempts are left | The call stays `unknown` |

- **Fits.** An output fits when it has a value of the right
  [.NET type](extension-packages.md#contracts) for each output, and a value
  for each required output.
- **Undeclared capability.** The attempt fails even if the operation catches
  the exception that the context throws. The error names the operation, the
  package and the binding, and holds no value.
- **A retry.** It sets the call back to `pending` and keeps the item. The item
  is due after the step's backoff doubled for each attempt made, so after
  `backoff`·2^(n−1) for attempt n, and at most an hour. A retry writes no
  history row and no audit record. A call with a missing version counts the
  attempt without marking the call `sending`, because the operation never ran.
  A release without the step, or an installed manifest without the operation,
  is not retried, since a retry cannot fix it.
- **Left unknown.** A call whose operation is not `idempotent`, or whose
  attempts ran out after an unknown outcome, stays `unknown`. The instance
  stays `waiting`. The operation may have acted, so the platform neither
  runs it again nor fails the instance. A history row with output
  `{ "operationId", "outcome": "unknown" }` makes it visible. Its error is a
  fixed sentence, one for a timeout and one for a call found `sending`. The
  call keeps its stored input, and it writes no `operation.called` audit record
  and no `process.failed`, because it has not settled. Packages have no
  lookup, so an operator resolves it, see [Resolve](process-inspection.md#resolve).
- **Audit.** Each settled call writes one audit record `operation.called`,
  with actor `system` and details
  `{ "package", "version", "operation", "operationId", "attempts", "outcome" }`.
  When it writes output fields, it also writes `record.updated`, as
  `updateRecord` does. A failed call also writes `process.failed`.
- **Exceptions.** History holds the exception type of a failed attempt, such
  as `System.FormatException`, never its message. A message can hold input
  values or secrets. An output that does not fit gets a fixed sentence that
  names the package and the operation, and holds no value.

## Worker settings

The worker polls every tenant database in the `Tenants` section of its
configuration (see
[Tenant configuration](../architecture.md#tenant-configuration)).
Under `Tenancy:Source=directory`, the worker reads its tenants from the
[tenant directory](../architecture.md#tenant-directory) instead, over
`ConnectionStrings:Platform`, and ignores a `Tenants` section. It needs
`ConnectionStrings:Platform` and `Platform:SecretKey` and stops at start without
them, and it stops at start when `Tenancy:RefreshInterval` is invalid. It never
uses `ConnectionStrings:PlatformSchema`. It reads the directory at start. If that
read fails, it logs the failure once, retries every `Worker:PollInterval` and is
not ready until a read works.

Then it reads the directory again every `Tenancy:RefreshInterval`, without a
restart. The refresh runs in the polling loop, between work items, so no
connection pool is dropped while an item runs. A tenant that is `suspended`,
`provisioning` or removed gets no new work item claimed after the next refresh.
Work already claimed finishes. A tenant added as `active` is polled after the
next refresh, once its runtime database role check passes. Until then the worker
skips it, logs an error and runs the check again at each refresh. A tenant whose
database settings change goes through the check again and gets a new connection
pool. If a refresh fails, the worker keeps the last good read and logs an error,
and `/health/ready` answers `200` with status `Degraded` until a refresh works.
A long run of work items for one tenant delays the refresh until the run ends.

The worker also reads the schema version of every tenant again at each poll
round, and claims no new work for a tenant whose version is not its own. It runs
that tenant's work again once `migrate` has brought the tenant to its version.

Once a tenant is ready, the worker also deletes the expired rows of its
`axis.sessions` table, when the tenant becomes ready and then at most every
5 minutes by its own clock, see
[sign-in and the session](security.md#sign-in-and-the-session). A database
without the table yet is skipped. Any other failure is logged as a warning and
does not hold back the tenant's work items.

The worker also sweeps the files of each ready tenant on the same cadence, with
its own timer and its own clock: it purges `deleted` files and `pending` files
uploaded more than 24 hours ago, see
[storage and clean-up](file-fields.md#storage-and-clean-up). A database without
the files table yet is skipped. Any other failure is logged as a warning and
does not hold back the tenant's work items.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Worker:LeaseDuration` | `00:00:30` | How long a claim lasts before another worker can claim the item. |
| `Worker:PollInterval` | `00:00:01` | How long the worker waits before it polls again when it found no work. |
| `Worker:MetricsInterval` | `00:00:30` | How often the worker reads each tenant database for the [due work item and pending computed field gauges](#worker-metrics). It must be positive. |
| `Worker:DeliveryAttempts` | `5` | Failures of a delivery before it becomes a [dead letter](process-events.md#dead-letters). It must be at least 1. |
| `Health:Urls` | `http://127.0.0.1:5208` | The address of the worker's health endpoints, `/health/live` and `/health/ready`. It serves nothing else. |

As environment variables they are `Worker__LeaseDuration`,
`Worker__PollInterval`, `Worker__MetricsInterval`, `Worker__DeliveryAttempts` and `Health__Urls`. What the health endpoints answer is
in [health checks](../architecture.md#health-checks).

The server migrates the tenant databases, and the worker does not. Before it
claims work for a tenant, the worker checks that the processes migrations are
applied to the tenant database. Until they are, it skips the tenant and logs
once that it is waiting. Once they are, it logs that the tenant is ready for
work and starts claiming. So the worker can start before the server.

That holds under `Tenancy:Source=configuration`. Under
`directory`, [`Axis.Admin migrate`](administration.md#migrate) migrates the
tenant databases, and the server does not. The worker reads the recorded schema
version of every tenant at each poll round. It skips a tenant whose schema
version is not its own and claims none of its work, until `migrate` has brought
the tenant to its version. It then runs the work without a restart. A tenant
that waits for its schema version logs once and does not make the worker not
ready.

### Worker metrics

The worker emits these metrics, next to the standard ASP.NET Core, HttpClient
and .NET runtime metrics. Attributes name things and never hold values, see
[Telemetry](../architecture.md#telemetry).
A metric is recorded in the transaction that writes the end or the attempt. So
a transaction that rolls back after its write, such as a `fenced` run, can count
the same end or attempt again when the item is retried. An ended instance whose
release cannot be loaded is counted without `axis.application` and
`axis.process`. A cancelled instance is not counted.

| Metric | Instrument | Unit | Attributes |
| --- | --- | --- | --- |
| `axis.work_items.due` | gauge | items | `axis.tenant`, `axis.work_item.kind` |
| `axis.work_items.oldest_due_age` | gauge | s | `axis.tenant`, `axis.work_item.kind`. Stuck work shows here |
| `axis.work_items.duration` | histogram | s | `axis.tenant`, `axis.work_item.kind`, `axis.outcome`: `completed`, `failed` or `fenced` |
| `axis.process.instances.ended` | counter | `{instance}` | `axis.tenant`, `axis.application`, `axis.process`, `axis.outcome`: the end state, `completed` or `failed` |
| `axis.calls.attempts` | counter | `{attempt}` | `axis.tenant`, `axis.application`, `axis.connector` or `axis.package`, `axis.operation`, `axis.outcome`: the [call outcome](#call-outcomes) class, `succeeded`, `retryable`, `failed` or `unknown` |
| `axis.queries.timeouts` | counter | `{query}` | `axis.tenant`, `axis.application` when the route has one. An interactive read that ran past `Data:QueryTimeout`, see [Errors](record-api.md#errors) |
| `axis.dead_letters.created` | counter | `{dead_letter}` | `axis.tenant`, `axis.work_item.kind`. See [Dead letters](process-events.md#dead-letters) |
| `axis.computed.pending_fields` | gauge | fields | `axis.tenant`, `axis.application`. See [Recomputing computed fields](migrations.md#recomputing-computed-fields) |

The gauges are read every `Worker:MetricsInterval` by a loop that is separate
from the work loop, so a hung item does not stop it and it does not slow the
work. A metrics scrape never queries a tenant database. An item counts as due
once its `due_at` has passed, claimed or not, so a hung item keeps aging. The
age and the due time use the clock of the claim. Every kind the worker handles
reports 0 when nothing is due, and so does every application with an active
release when nothing is pending. A kind with due items that the worker has no
handler for reports its real count. `axis.computed.pending_fields` adds up the
pending field names of all pending entities of the application. A tenant whose
database cannot be read logs a warning and reports no data until a later read
succeeds.
