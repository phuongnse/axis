# Connector and package calls

A step that calls a connector or runs a package operation stages the call in its transaction. The
worker sends the call outside any transaction, so an external system never holds a step open. This
page holds the connector calls and package operations that a step stages, and the files that those
calls carry. The step transaction and the worker's settings are in [process execution](process-execution.md).

## Connector calls

A `callConnector` step calls one operation of a
[connector](integration-resources.md#connectors-and-deployment-bindings), an external system such as
the purchasing system. The call never runs inside a database transaction: the step stages it, the
worker sends it later, and a separate transaction records the result. A stable operation identity
lets every retry and every lookup ask about the same call, so a lost response never leads to a second
purchase order.

### Step shape

After approval, the purchase request process creates a purchase order. `announceApproved` names the
step as its `next`:

```json
{
  "name": "announceApproved",
  "type": "operation",
  "operation": "raiseEvent",
  "event": "PurchaseRequestApproved", "fields": { "number": "number", "totalAmount": "totalAmount" },
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
- `input` maps operation inputs to expressions. Each key is an input of the operation, and each value
  is an expression over the subject record, with the same scope as a decision's `when`. An `object`
  input maps as `{ "fields": { … } }`, each key a nested field. A `list` input maps from a child
  collection of the subject as `{ "from": "<child collection>", "fields": { … } }`, and each item
  expression is over the child row. The step sends them as nested JSON, see
  [Connector resource](integration-resources.md#connector-resource).
- `output` is optional. It maps operation outputs to subject fields: each key is an output of the
  operation, and each value names a field of the subject record.
- `retry` is optional. `attempts` counts the first attempt, defaults to 3 and is at most 10. `backoff`
  is an ISO 8601 duration, in the same form as a task's `dueIn`. It is the delay before the second
  attempt, it doubles each attempt up to 1 hour, and it defaults to `PT30S`.
- `next` names the step taken when the call succeeds.

The compiler reports these as compile errors. Every name is matched ignoring letter case, and the
compiled step holds the declared names.

| Check                                                                                                                                                                                                                                                                      | Code                                        | At                                    |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------- | ------------------------------------- |
| The connector exists. A name whose connector file was not loaded because of its own errors is not reported again                                                                                                                                                         | `AXC0102`                                   | `/steps/{i}/connector/name`           |
| The operation is an operation of the connector. With this error or the one above, `input` and `output` are not checked                                                                                                                                                    | `AXC0103`                                   | `/steps/{i}/connector/operation`      |
| Each `input` key names an input of the operation, at most once                                                                                                                                                                                                            | `AXC0104`                                   | `/steps/{i}/input/{key}`              |
| Every required input has a key. `input` may be empty when the operation has no required input                                                                                                                                                                             | `AXC0105`                                   | `/steps/{i}/input`                    |
| Each `input` expression gives the input's type                                                                                                                                                                                                                            | Its expression code, such as `AXC0048` for a value of the wrong type | `/steps/{i}/input/{key}` |
| A nested mapping is checked as a top-level input is. An item expression of a list is over the child row, so it sees the child's fields, not the subject's                                                                                                                 | `AXC0104`, `AXC0105` at the enclosing `…/fields`, and the expression codes | `/steps/{i}/input/{key}/fields/{name}` |
| A mapping has the right shape                                                                                                                                                                                                                                              | `AXC0318`                                   | `/steps/{i}/input/{key}`              |
| A `from` is a child collection of the subject, in a process with a subject. Otherwise the item expressions are not checked                                                                                                                                               | `AXC0319`                                   | `…/from`                              |
| Each `output` key names an output of the operation, at most once                                                                                                                                                                                                          | `AXC0106`                                   | `/steps/{i}/output/{key}`             |
| Each `output` value names a field of the subject entity that no other output of the step writes, and that an `updateRecord` `set` may name, so it is not computed, not numbered by a sequence and not a child collection                                                  | `AXC0107`                                   | `/steps/{i}/output/{key}`             |
| The output's type fits the field: it is the same type, or an `integer` output into a `decimal` field                                                                                                                                                                      | `AXC0108`                                   | `/steps/{i}/output/{key}`             |
| `attempts` is from 1 to 10                                                                                                                                                                                                                                                 | `AXC0004`                                   |                                       |
| `backoff` is a valid duration                                                                                                                                                                                                                                              | `AXC0109`                                   | `/steps/{i}/retry/backoff`            |
| A `callConnector` step has `connector`, `input` and `next`, and no `set`. An `updateRecord` step has none of `connector`, `input`, `output` and `retry`                                                                                                                   | `AXC0004`                                   |                                       |

The compiled step holds the connector, the operation, the inputs in the operation's order, the
outputs, the retry policy with its defaults filled in, and `next`. An `object` or `list` input holds
its nested inputs.

A module process's step names a
[connector slot](modules-and-templates.md#processes-pages-policies-and-dependencies) of its module and
one of the slot's operations. The inputs and outputs are checked against the slot's fields with the
same codes, and a name that is no slot is `AXC0102` and a missing slot operation is `AXC0103`, in the
module's file. In each application the compiled step holds the connector the slot maps to, so the
worker calls it as it calls an application step.

### Staging and the call

A connector call passes through three transactions: the step's, which stages it, a short one before
each attempt, and the outcome's, with the request itself sent between them outside any transaction.

**The step's transaction stages the call.** It runs as other steps do, see
[Running a step](process-execution.md#running-a-step), and writes these together:

1. It evaluates each `input` expression on the subject record. A required input that gives `null`
   fails the step, and an optional input that gives `null` is stored as `null`. Dates and date-times
   are stored in the text forms of the [record API](record-api.md). An `object` or `list` input is
   stored as nested JSON, see [Structured payloads](integration-resources.md#structured-payloads).
2. It sets the instance to `waiting` on the step.
3. It inserts the call into `axis.connector_calls` as `pending`, with 0 attempts and the evaluated
   input. The row's id is the call's operation identity, a version 7 UUID. It stores the instance
   revision the step ran at as `staged_revision`. A unique index on instance, step and
   `staged_revision` lets a step stage one call per pass of a [loop](process-flow.md#loops).
4. It writes the step's history row, with output `{ "state": "waiting", "operationId" }`.
5. It writes the audit record `connector.callStaged` with the actor `system` and details
   `{ "step", "connector", "operation", "operationId" }`.
6. It inserts a work item of kind `connector.call` for the call, due at once. The item names the
   instance and the call.

While its call is pending, retrying or unknown, the instance stays `waiting`, so a second start for
the same record is blocked, as with an open task.

**A step that runs again after a retry** finds that the latest call of the step, the one with the
highest `staged_revision`, is `failed`, and reuses that row instead of inserting one, in the same
transaction. Only a `failed` row is reused, and a call of an earlier pass of a loop never is. The row
keeps its id, so the operation identity and the idempotency key do not change. The step evaluates the
input again from the subject record as it is now, on purpose, so the retry sends the current data,
although a failed connector call keeps its stored input. It sets the call to `pending` with that
input, 0 attempts and 0 lookups, clears the last status, the last error and the settled time, and sets
`staged_revision` to the revision the step runs at again. The history row, the audit record and the
work item are written again as above, with the same `operationId`. See
[Retry](process-operator-commands.md#retry).

**Each attempt.** A worker that claims the `connector.call` item does this:

1. **Mark.** A short transaction marks the call `sending` and counts the attempt. It extends the
   item's lease to the connector's `timeout` plus `Worker:LeaseDuration`, so no other worker claims
   the item while the call runs. An `oauth2` connector gets four times the `timeout`, because its
   token request has the same `timeout`, and a `401` adds a second token request and a second send.
2. **Bindings.** The worker reads the connector's bindings from its host configuration. A missing
   binding, or an `http` URL outside Development, means the request is never sent: only a worker that
   runs in Development allows `http`. The error of an `http` URL names the tenant and the binding,
   never the URL. For an `oauth2` connector this covers the token URL too.
3. **Send.** The worker sends the request outside any transaction.
   - Each input goes where its `in` says, see
     [Structured payloads](integration-resources.md#structured-payloads). A `path` input fills its
     placeholder, escaped as one segment. `query` inputs make the query string, and `header` inputs
     are request headers named after the input. An optional input with no value is left out.
   - The body is JSON that holds only the `body` inputs. A `GET` sends no body.
   - The auth sends the secret as `Authorization: Bearer <secret>`, or in the named header. An
     `oauth2` auth first gets a token, from the worker's memory or the token endpoint, and sends that
     as the bearer token. A failed token request is a retryable attempt that sends nothing, see
     [OAuth2 auth](integration-resources.md#oauth2-auth).
   - When an `oauth2` connector's system answers `401`, the worker drops the token, gets a new one and
     sends the request once more in the same attempt, with the same idempotency key. A second `401`
     fails the attempt as any `4xx` does.
   - With `idempotency`, the request carries the header `<idempotency>: <operationId>`, such as
     `Idempotency-Key: 0192f5c3-…`.
   - The request is cancelled after the connector's `timeout`.
   - Redirects are not followed, and `Retry-After` is ignored, so backoff is decided in one place.
4. **Record.** An outcome transaction records the result. Its commit checks the claim token, and it
   needs the instance still `waiting` on the step at its revision. Otherwise it writes nothing else,
   and the item is deleted.

A worker can crash after it sends and before it records. Its lease then expires, and the next worker
finds the call still `sending`. That attempt's outcome counts as unknown, see
[Call outcomes](#call-outcomes), and the worker never resends blind.

**A successful call settles.** The outcome transaction does this:

1. It writes the output fields to the subject record with the record update rules, as `updateRecord`
   does. An optional output that the body leaves out leaves its field unchanged, and an explicit
   `null` writes `null`. A refused write, such as a failed validation, fails the call instead.
2. It sets the call to `succeeded` and keeps its stored input, see
   [Call input kept](process-inspection.md#call-input-kept). It writes the attempt's row, see
   [Attempts](process-inspection.md#attempts).
3. It sets the instance to `running` on `next`, with a new revision.
4. It writes the history row of the step, at the revision the instance waited at. Its output is
   `{ "operationId", "outcome", "next" }`, plus `subjectVersion` when it wrote output fields.
5. It writes `record.updated` when it wrote output fields, with the same details as an
   `updateRecord` step, and `connector.called` with the details
   `{ "connector", "operation", "operationId", "attempts", "outcome", "lastStatus" }`. Both have the
   actor `system`.
6. It inserts the work item of the next step and deletes the call's item.

**A retry** sets the call back to `pending` and keeps the item. After attempt n the item is due after
`backoff` × 2^(n−1), capped at 1 hour. The call row keeps the attempts, the last status and the last
error, so a worker that starts later sees them. A retry writes no history row and no audit record.

**An unknown call** is one whose outcome is unknown and that is not retried, see
[Call outcomes](#call-outcomes). The call becomes `unknown`, with the last status and the error on the
call row, and keeps its stored input. The instance stays `waiting`. When the operation declares a
`lookup`, the item is kept and due at once, so a worker [reconciles](#reconciliation) the call.
Without one, the item is deleted, and a history row with output
`{ "operationId", "outcome": "unknown" }` and the error makes the call visible. No `connector.called`
is written, because the call has not settled.

**A failed call settles** too. The call becomes `failed` and keeps its stored input, see
[Call input kept](process-inspection.md#call-input-kept), with the last status and the error on the
call row, and the attempt's row is written. The instance becomes `failed`, with the error in a history
row whose output is `{ "operationId", "outcome": "failed" }`. The transaction writes
`connector.called`, with the same details as a successful call, and `process.failed`, both with the
actor `system`.

The stored input lives only on the call row, which a connector call keeps after it settles, see
[Call input kept](process-inspection.md#call-input-kept).

**Request and responses.** The call
row also keeps the request, and each attempt keeps its response, with every secret as `[secret]`. Neither
is copied to history, audit records or logs, and inspection shows them only under the
[masking rule](process-inspection.md#values-and-masking).

### Call outcomes

The platform, not the author, decides which outcomes are retried. A retry always sends the stored
input again under the same operation identity.

| Outcome of an attempt                                                                       | Class     | Retried                                                                                                                           | When attempts run out                              |
| ------------------------------------------------------------------------------------------- | --------- | --------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------- |
| `2xx` with a body that fits the output                                                      | Succeeded | Not needed                                                                                                                        | Not needed                                         |
| `2xx` with a body that does not fit the output                                              | Unknown   | Never resent. It goes straight to [reconciliation](#reconciliation), with or without `idempotency`                                | Does not apply                                     |
| `3xx`                                                                                       | Failed    | No                                                                                                                                | The instance becomes `failed` at once              |
| `4xx` other than `408` and `429`                                                            | Failed    | No                                                                                                                                | The instance becomes `failed` at once              |
| Never sent: DNS failure, connection refused, TLS failure, missing binding                   | Retryable | Yes                                                                                                                               | The instance becomes `failed`                      |
| `408`, `429` and `503`                                                                      | Retryable | Yes                                                                                                                               | The instance becomes `failed`                      |
| Timeout, a reset after the request was sent, another `5xx`, or a call found still `sending` | Unknown   | With the same key, only when the connector declares `idempotency`. Without it, the call goes to [reconciliation](#reconciliation) | The call goes to [reconciliation](#reconciliation) |

A body fits the output when it is a JSON object with a value of the right type for each output, and a
value for each required output.

### Reconciliation

An unknown outcome means the system may have acted. The call becomes `unknown`, and the worker asks
the system what happened: it calls the operation's `lookup` with the operation identity as
`{idempotencyKey}`.

| Lookup answer                                                                                                     | What follows                                                                                                                                                                                                                                                                                                                         |
| ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Found:** `200` with a body that fits the output                                                                 | The call settles as succeeded with that output, as after a successful attempt. Nothing is sent again                                                                                                                                                                                                                                 |
| **Not found:** `404`, so the system never acted                                                                   | The call goes back to `pending` and is retried with the same identity, which counts as an attempt. The retry is due after the step's backoff for the attempts made, as any retry is. When no attempts are left, the instance becomes `failed`                                                                                         |
| **Failed lookup:** any other answer, including another `2xx` and a `200` whose body does not fit, or a lookup that is never sent | The call stays `unknown`, with the lookup's status and error, and the lookup is tried again. The delay starts at `backoff` and doubles each time, up to 1 hour, with no limit. The call row counts the failed lookups in `lookups`, so the delay survives a worker restart, and the count is reset when the call goes back to `pending` |
| **No lookup:** the operation declares none                                                                        | The call stays `unknown`, and the instance stays `waiting`. A history row with output `{ "operationId", "outcome": "unknown" }` makes it visible, and an operator resolves it, see [Resolve](process-operator-commands.md#resolve)                                                                                                           |

The first lookup runs at once after the unknown outcome, and only a failed lookup waits. The same
`connector.call` item runs each lookup, under the same claim and fencing rules as an attempt, and a
lookup counts no attempt. No history row is written while the call waits for its lookup: the call row
shows it as `unknown`, and the history row comes when the call settles.

An unknown outcome is never retried without the idempotency key or a lookup. A lookup can only find a
call whose key reached the system, in the `idempotency` header or in an operation path that holds
`{idempotencyKey}`.

**A late original request.** A lookup can answer not found while the original request is still on its
way, and the call is then sent again. Only the system's idempotency key stops the late original from
creating a second order, so a connector without `idempotency` depends on its lookup being correct.

### Lost response

In this example the purchasing system creates the order, but its response is lost. The request still
ends with exactly one purchase order.

1. The finance reviewer approves request `PR-2026-00042`. `markApproved` runs, and
   `announceApproved` raises `PurchaseRequestApproved` and queues `createPurchaseOrder`.
2. The step's transaction evaluates the input, stages call C as `pending`, sets the instance
   `waiting` and queues a `connector.call` item.
3. Worker A claims the item. It marks C `sending` with attempt 1.
4. Worker A sends `POST /purchase-orders` with `Idempotency-Key: C`.
5. The purchasing system creates `PO-2026-0001` under key C. Its response is lost, and worker A's
   request times out after `PT30S`.
6. The timeout is an unknown outcome. The connector declares `idempotency` and 4 of 5 attempts are
   left, so C goes back to `pending`. The item is due again after `PT30S`.
7. Worker B claims the item, marks C `sending` with attempt 2, and sends the same body with
   `Idempotency-Key: C`.
8. The purchasing system knows key C. It returns the existing `PO-2026-0001` and creates no new
   order.
9. The outcome transaction settles C as succeeded. It writes `purchaseOrderNumber = PO-2026-0001`,
   `record.updated`, and one `connector.called` with `attempts` 2. The instance moves on to
   `assignPaymentReference`, which runs the sample package, then `assignPaymentDueDate`, which runs the
   TypeScript sample package, and then moves on to `done`.
10. `done` completes the instance.

The purchasing system holds one order, and the request records its number. Two variants end the same
way:

- **Crash after sending.** Worker A crashes after step 5, before it records anything. C is still
  `sending` when its lease expires. The worker that claims the item next counts attempt 1 as unknown,
  and the steps go on from step 6.
- **No `idempotency`, with a lookup.** Suppose the connector declares no `idempotency`, and the create
  operation's path holds the key instead, such as `/purchase-orders/{idempotencyKey}`. The timeout in
  step 5 is unknown, and C is not sent again. The worker calls the lookup for key C, and the system
  answers `200` with `PO-2026-0001`. C settles as succeeded, as in step 9, with no second `POST`. Had
  the lookup answered `404`, C would be sent again as attempt 2.

## Package operations

A package operation step runs one operation of an [extension package](extension-packages.md), custom
code that an installation or an application provides. The step runs like a [connector call](#connector-calls): it stages
the call, the worker runs the operation outside any database transaction, and a separate transaction
records the result. This holds for every package operation, even a pure one, so there is one rule for
all of them.

### Package step shape

The examples use an illustrative package, `purchasing-tools`, with the operation
`normalizeSupplierCode` from the [manifest example](extension-packages.md#manifest), and an
illustrative `supplierCode` text field on the subject. The sample packages, `payment-references` and
`payment-terms`, are used the same way by the `assignPaymentReference` and `assignPaymentDueDate` steps of the
[purchase request sample](processes.md#resource-shape).

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

- `operation` is `<package>/<operation>`. The `/` marks a package operation: the part before it is a
  package id, and the part after it is an operation of that package.
- `input` maps operation inputs to expressions over the subject record, as for `callConnector`.
- `output` is optional. It maps operation outputs to subject fields, as for `callConnector`.
- `retry` is optional, with the same `attempts` and `backoff` as for `callConnector`.
- `next` names the step taken when the call succeeds.

The compiler reports these as compile errors.

- The package is one of the application's
  [`packages`](declaring-packages.md#declaring-packages-in-an-application), or an application's own
  package in `packages/<id>/`, see
  [an application's packages](extension-packages.md#an-applications-packages), with the package id
  matched exactly. Otherwise it is `AXC0150` at `/steps/{i}/operation`. A listed package that failed
  pinning (`AXC0139` to `AXC0142`) is not reported again, and neither is a package folder that has
  its own problem, nor any package when `application.json` was not loaded. A module process is
  checked against the module's own `packages` in `module.json` instead.
- The operation is in the manifest of the version the release pins, or in the manifest of the
  application's own package, matched ignoring letter case. Otherwise it is `AXC0151` at
  `/steps/{i}/operation`. The compiled step holds the manifest's declared name.
- `input`, `output` and `retry` are checked as for a `callConnector` step (`AXC0104` to `AXC0109`),
  see [Step shape](#step-shape), against the operation's fields in the manifest.
- A package operation step has `input` and `next`, and no `set`, `connector`, `event` or `fields`
  (`AXC0004`).

The compiled step holds the package id, the pinned version, or the version in the manifest of the
application's own package, the operation, the inputs and outputs in
manifest order, the retry policy with the defaults filled in, `next`, and the manifest's `idempotent`
and `timeout`.

### Staging a package call

A package call goes through the steps of [Staging and the call](#staging-and-the-call), with the
operation in place of the HTTP request:

- **Staging.** The step's transaction evaluates the input, stores the call as `pending` with a stable
  operation identity and the evaluated input, sets the instance to `waiting` on the step, and queues a
  work item, except for a call to a customer-hosted package, which waits `pending` with no work item, so
  the worker runs no attempt of it. The compiled step says which inputs the operation requires, so
  staging reads no installed package. A required input that gives `null` fails the step. The table is
  `axis.package_calls` and the work item kind is `package.call`. A unique index on instance, step and `staged_revision` lets a
  step stage one call per pass of a [loop](process-flow.md#loops). The step writes the history row
  `{ "state": "waiting", "operationId" }` and the audit record `operation.staged`. The call also
  stores the step's retry policy and the operation's `idempotent` flag, because a worker that lacks
  the pinned version cannot read them from the release.
- **Staging again.** A step that runs again after an operator [retry](process-operator-commands.md#retry)
  reuses its `failed` row, as a connector call does, under the same operation identity. It stores
  fresh input, the step's current retry policy, the operation's `idempotent` flag, the new
  `staged_revision` and 0 attempts, and clears the last error and the settled time.
- **Mark.** Before each attempt, a short transaction marks the call `sending`, counts the attempt and
  extends the item's lease past the operation's `timeout`.
- **Run.** The worker resolves the `url` and `secret` bindings the operation declares for the tenant,
  and runs the operation with the stored input, outside any transaction. The context's `OperationId` is the
  operation identity, the same on every attempt. The context hands out only the declared bindings, see
  [Capabilities](extension-packages.md#capabilities). With `inProcess`, the worker loads the pinned package
  version and passes the operation a cancellation token that is cancelled at the `timeout`.
- **Run in a package runner.** With `process`, the default, a C# attempt goes to a
  [package runner](extension-packages.md#package-runners) instead. The worker sends the stored input and the
  resolved bindings in a `run`, and closes the connection at the `timeout`, which kills the runner. A `node` or
  `python` attempt always goes to a package runner, with `inProcess` too.
  An attempt of an application's own package, in any language, always goes to a runner of
  `application-package-host`, see [an application's packages](extension-packages.md#an-applications-packages). The
  worker reads the package's built files from the call's release in the mark transaction and sends them with the call's
  tenant when it starts the runner. Without `Extensions:Runners:Application`, the attempt is retryable and names the setting.
- **Run in a customer-hosted runner**. A call to a customer-hosted package, whether
  the application's own package or an installation's package that the application
  [declares](declaring-packages.md#declaring-packages-in-an-application) with `runner: customer`, see
  [customer-hosted runners](extension-packages.md#customer-hosted-runners), waits `pending`, its retry due, until a
  [customer-hosted runner](extension-packages.md#customer-hosted-runners) that registers the package and
  version takes it, and the runner receives no binding value.
  - **The take is the Mark step.** It marks the call `sending`, counts the attempt and gives a lease that
    lasts the operation's `timeout`. It also queues a `package.call` item that is due when the lease ends,
    and the posted result deletes it.
  - **The posted result is the Record step.** `succeeded` is checked against the manifest as an output that
    fits, `retryable` counts as a `RetryableException`, and `failed` counts as any other exception, keeping
    only the exception type.
  - **A lease that ends with no result is a timeout.** When the lease's item comes due and the call is still
    `sending`, the worker records an unknown outcome with the timeout's error, as
    [package call outcomes](#package-call-outcomes) says. A result that arrives after it answers `409`.
  - **A call that no runner takes** stays `pending`, and the instance stays `waiting`.
- **Record.** An outcome transaction records the result, as for a connector call. On success it
  writes the output fields with the record update rules, sets the instance to `running` on `next`,
  writes the history row and the audit records, and queues the next step.
- **Stored input.** The stored input is cleared when the call settles, and never appears in history,
  audit records or logs.

A worker can crash after it runs the operation and before it records the result. The next worker then
finds the call still `sending`, and that attempt's outcome counts as unknown.

### Package call outcomes

The platform decides which outcomes are retried. A retry runs the operation again with the stored
input under the same operation identity.

| Outcome of an attempt                                                         | Class     | Retried                                                                   | When attempts run out                 |
| ----------------------------------------------------------------------------- | --------- | ------------------------------------------------------------------------- | ------------------------------------- |
| The output fits the manifest                                                  | Succeeded | Not needed                                                                | Not needed                            |
| The output does not fit the manifest                                          | Failed    | No                                                                        | The instance becomes `failed` at once |
| A `RetryableException`                                                        | Retryable | Yes, while attempts are left                                              | The instance becomes `failed`         |
| Any other exception, or a request to the context for an undeclared capability | Failed    | No                                                                        | The instance becomes `failed` at once |
| The pinned version is missing on the worker                                   | Retryable | Yes, while attempts are left                                              | The instance becomes `failed`         |
| The release holds an application package's source but no built files         | Failed    | No                                                                        | The instance becomes `failed` at once |
| A package host that cannot be reached, is not set, or starts no runner        | Retryable | Yes, while attempts are left                                              | The instance becomes `failed`         |
| A declared binding the tenant lacks, or an `http` url outside Development     | Retryable | Yes, while attempts are left                                              | The instance becomes `failed`         |
| The release lacks the step, or the installed manifest lacks the operation     | Failed    | No                                                                        | The instance becomes `failed` at once |
| A timeout, a runner that exits, or a call found still `sending`               | Unknown   | Only when the operation declares `idempotent: true` and attempts are left | The call stays `unknown`              |

- **Fits.** An output fits when it has a value of the right [.NET type](extension-packages.md#contracts)
  for each output, and a value for each required output.
- **A runner.** A runner that closes its connection after the `run` was sent has exited, and the operation
  may have acted. A host that is unreachable, is not set or starts no runner means that no `run` was sent,
  so the operation never ran. A `retryable` answer counts as a `RetryableException`, and a `failure` as any
  other exception, or as an undeclared capability when it names a binding.
- **Undeclared capability.** The attempt fails even if the operation catches the exception that the
  context throws. The error names the operation, the package and the binding, and holds no value.
- **A retry.** It sets the call back to `pending` and keeps the item. The item is due after the step's
  backoff doubled for each attempt made, so after `backoff` × 2^(n−1) for attempt n, and at most an
  hour. A retry writes no history row and no audit record. A call with a missing version counts the
  attempt without marking the call `sending`, because the operation never ran. A release without the
  step, or an installed manifest without the operation, is not retried, since a retry cannot fix it. A
  `retryable` result of a [customer-hosted runner](extension-packages.md#customer-hosted-runners) queues a
  `package.call` item due after the same backoff. A runner can take the call only once that item is gone,
  and the worker deletes it when it is due, with no other write, because the call is `pending` and a runner took
  it before. The next take gives the same operation identity.
- **Left unknown.** A call whose operation is not `idempotent`, or whose attempts ran out after an
  unknown outcome, stays `unknown`, and the instance stays `waiting`. The operation may have acted, so
  the platform neither runs it again nor fails the instance. A history row with output
  `{ "operationId", "outcome": "unknown" }` makes it visible. Its error is a fixed sentence, one for a
  timeout, one for a runner that exited and one for a call found `sending`. The call keeps its stored input, and it writes no
  `operation.called` audit record and no `process.failed`, because it has not settled. Packages have
  no lookup, so an operator resolves it, see [Resolve](process-operator-commands.md#resolve).
- **Audit.** Each settled call writes one audit record `operation.called`, with actor `system` and
  details `{ "package", "version", "operation", "operationId", "attempts", "outcome" }`. When it writes
  output fields, it also writes `record.updated`, as `updateRecord` does. A failed call also writes
  `process.failed`.
- **Exceptions.** History holds the exception type of a failed attempt, such as
  `System.FormatException`, never its message, because a message can hold input values or secrets. An
  output that does not fit gets a fixed sentence that names the package and the operation, and holds
  no value.

## Files in calls

_(planned for M40)_

A file is a value that a call carries, so a purchase order goes to a purchasing system as a PDF and a signed
contract comes back into a `file` field, as [D157](../decisions/d157-files-reach-calls-and-emails-show-in-the-page-keep-their-versions-and-documents-come-from-word-templates.md)
agrees. This section covers a file that a connector call sends or receives and a file that a package operation
takes or gives. The files themselves are in [file fields](file-fields.md), and a file that an email carries is in
[email](notifications.md#email).

- **A file input.** An operation input of the type `file` is mapped to the name of a `file` field of the
  subject record, such as `"document": "contractDocument"`, or in a `list` input to a `file` field of the child
  row. It is not an expression, because a file allows only `is null` and `is not null` in one. A required file
  input whose field is `null` fails the step like any required input, with a message that names the input.
- **Multipart.** An operation whose `encoding` is `multipart` sends its body as `multipart/form-data`. The file is
  a part named as the input, with the file's stored name and media type, and each other `body` input is a text
  part. An `object` or `list` input in such an operation is refused when the connector is compiled.
- **In JSON.** An operation with the default encoding sends the file as a base64 string at the place of its
  input in the JSON body.
- **Bytes at send time.** The step stores the id of the file when it stages the call, never the bytes. The
  worker reads them from the storage of the application's [`fileStorage` binding](file-fields.md#the-filestorage-manifest-property)
  when it sends, so a retry sends the same file. A file that has been purged by then fails the attempt with a
  message that names the input.
- **A file output.** An operation output of the type `file` is read from the response: the whole body, or the
  base64 string at its `source`. The step writes it as a new file in the `file` field that `output` names, and
  the outcome's transaction attaches it to the subject record, as it writes any output. The media type comes from
  `Content-Type`, and the name from `Content-Disposition`, or from the output's name when there is none.
- **Checks on a file that comes in.** It meets the checks of an [upload](file-fields.md#upload): its declared type
  is one Axis can check, its extension and content fit that type, it is not an executable or an SVG, and it is
  within `maxSize` of the field and `Files:MaxSize`. A file that fails them fails the call as an output that does
  not fit does, and nothing is stored.
- **A package operation.** A package operation's input or output may have the type `file`, see the
  [value types](extension-packages.md#contracts) of a package. The worker sends the bytes of an input file with
  the call, and the runner never reads the storage. A file that the operation gives is checked and written to a
  `file` field as a connector's output is.
- **No bytes in the records.** Step history, logs and audit hold the file's id and size, never its name or
  bytes, so a file's content reaches only the call, see [file fields](file-fields.md#storage-and-clean-up).
- **Compile checks.** A `file` input names a `file` field of the subject, or of the child entity of its `list`,
  and a `file` output names a `file` field that no other output of the step writes. A `file` input in a
  connector operation with the `GET` method is refused, because a `GET` has no body.
