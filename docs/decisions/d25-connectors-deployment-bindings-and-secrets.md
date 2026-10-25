# D25. Connectors, deployment bindings and secrets

Status: **Agreed**

M5 lets a process call external systems. The `Bindings` block in host
configuration, its startup checks, resolving a binding at run time, the
secret value that hides itself from text, JSON and logs, the `connector`
resource with its compile checks and the activation check are built. Outside
Development, startup fails when a secret comes from a configuration file.
Calling a connector, using secrets in its calls and the call-time HTTPS check
are built. Retries are built. Reconciliation is built. See
[configuration](../reference/integration-resources.md#connectors-and-deployment-bindings)
for bindings and the `connector` resource, and
[processes](../reference/process-execution.md#connector-calls) for the call, its
retries and reconciliation.

- **Binding:** a named setting that a release refers to by name and never
  holds as a value. M5 has two kinds, `url` and `secret`. No resource kind
  declares bindings. A connector names one as `{ "binding": "<name>" }`, and
  where the name is used decides its kind: `baseUrl` needs a `url` binding,
  and `auth` needs a `secret` binding.
- **Where values live:** per tenant, in `Tenants:{id}:Bindings:{name}` of
  the server and worker configuration under `Tenancy:Source=configuration`.
  Both hosts must hold them. Under `Tenancy:Source=directory`,
  [D42](d42-tenant-directory-in-the-platform-database.md) moves binding
  values and secrets into the tenant directory, and it stores secrets
  encrypted.
- **Binding kinds in host configuration:** each binding has a `Kind`, `url`
  or `secret`, and a `Value`. A secret is set with an environment variable,
  such as `Tenants__default__Bindings__PurchasingSystemToken__Value`.
- **Activation check:** activating a release for a tenant fails with a
  diagnostic naming the tenant and the binding, when a binding the release
  uses is missing or of the wrong kind.
- **Secret values:** they come from the environment. Only in Development may
  a committed file hold one, and elsewhere startup fails. They never appear in a release, step
  history, audit record, log or problem details. There is no secret store
  such as a vault yet.
- **Resolved at run time:** the worker reads the binding when it makes a
  call, so changing a secret needs no new release.
- **Connector:** a `connector` resource has a `baseUrl` (a `url` binding),
  `auth` and a `timeout`. `auth` is none, a bearer token or a named header,
  each from a `secret` binding. `timeout` defaults to 30 seconds and is at
  most 120.
  - [D91](d91-connector-oauth2-and-structured-payloads.md) adds the `auth`
    type `oauth2` in M17.
- **Operations:** a connector has named `operations`. Each has a method, a
  path template, typed `input` fields sent as a JSON body and typed `output`
  fields read from the JSON response.
  - [D91](d91-connector-oauth2-and-structured-payloads.md) adds path, query
    and header inputs, nested inputs and nested output sources in M17.
- **Idempotency:** `idempotency` names the request header that carries the
  idempotency key, such as `Idempotency-Key`. It is absent when the system
  does not support one.
- **Lookup:** `lookup` is optional and set per operation. It names another
  operation of the same connector that finds an earlier call by its
  idempotency key. That operation is a `GET` whose path holds
  `{idempotencyKey}`, with the same outputs. A `200` means found, with the
  output. A `404` means not found. Anything else is a failed lookup.
- **HTTPS:** `http` base URLs are allowed only in Development. Elsewhere
  they must be `https`.
- **No redirects:** the worker never follows a redirect, and a `3xx`
  response fails the call. Following one could send the secret to another
  host.
- **No `Retry-After`:** the worker ignores it, so backoff is decided in one
  place.
- **Call connector step:** an `operation` step with
  `operation: "callConnector"` names a connector and one of its operations.
  Its `input` maps operation inputs to expressions over the subject record.
  Its `output` maps operation outputs to subject fields. It has `next` and
  an optional `retry`.
- **Staged:** the step's transaction stores the call with a stable
  operation identity and a work item, and the instance waits. No external
  call runs inside a database transaction.
- **Operation identity:** the id of the call's row in
  `axis.connector_calls`, a version 7 UUID. It is stored before any request
  is sent, so every attempt and every lookup uses the same key. A unique
  index on instance and step lets a step stage only one call. An instance
  waits on one call at a time.
- **Stored input:** the worker stores the evaluated input on the call row
  until the call settles, then clears it. A retry then sends the same body
  as the first attempt. Input never appears in history, audit records or
  logs. No response body is stored.
- **The call:** the worker sends the request with the operation identity as
  the idempotency key. A following transaction records the outcome. On
  success it writes the output fields with the record update rules, writes
  the step history and the audit record, and queues the next step.
- **Sending mark:** before each attempt, a short transaction marks the call
  `sending`, counts the attempt and extends the work item's lease past the
  connector's timeout. A worker that later finds a call still `sending`
  treats that attempt's outcome as unknown.
- **Waiting while a call is open:** while its call is pending, retrying or
  unknown, the instance is `waiting` on the step. Settling the call resumes
  it, as completing a task does. So a second start for the same record is
  blocked.
- **No new outbox table:** calls are rows of a new `axis.connector_calls`
  table. The work is a new kind of `axis.process_work_items` item, whose
  `due_at` gives the backoff.
- **Retry policy:** `retry` has `attempts` and `backoff`.
  - `attempts` counts the first attempt. It defaults to 3 and is at most 10.
  - `backoff` is an ISO 8601 initial delay that doubles each attempt, up to
    1 hour. It defaults to `PT30S`. `timeout` is an ISO 8601 duration too.
- **Failed:** a `4xx` other than `408` and `429` fails the call at once.
- **Retryable:** a connection that never sent the request, `408`, `429` and
  `503` are retried.
- **Unknown:** a timeout, a reset after the request was sent, or another
  `5xx` means the system may have acted. It is retried with the same key
  only when the connector declares `idempotency`. Without that, it goes to
  reconciliation. A `2xx` whose body does not fit the declared output is
  unknown too, and goes straight to reconciliation, even with
  `idempotency`.
- **Attempts run out:** the instance becomes `failed` with the error in its
  history. After an unknown outcome, the call goes to reconciliation
  instead. Operator [retry](../reference/process-inspection.md#retry) is built. Alerts and compensation come later.
- **Reconciliation:** the worker calls `lookup` with the operation
  identity.
  - Found settles the call as succeeded, with its output.
  - Not found retries the call with the same identity. When no attempts are
    left, the instance fails.
  - A failed lookup stays `unknown`. It is tried again with doubling
    backoff, capped at 1 hour, with no limit.
- **Never blind:** an unknown outcome is never retried without the
  idempotency key or a lookup. Without a `lookup`, it stays `unknown` and
  visible in the history until an operator [resolves](../reference/process-inspection.md#resolve) it.
- **A late original request:** if a lookup answers not found and the
  original request arrives later, only the system's idempotency key
  prevents a second order. Without `idempotency`, a connector depends on its
  lookup being correct.
- **Audit and history:** each settled call writes one audit record
  `connector.called`, with actor `system`. Its details are the connector,
  the operation, the operation identity, the attempts, the outcome and the
  last HTTP status, with no request or response bodies. When it writes
  output fields, it also writes `record.updated`, as `updateRecord` does. A
  failed call also writes `process.failed`. The step history holds the
  operation identity and the outcome, and no field values.

_Why:_ see [knowledge](../domain/knowledge.md#execution) and
[authoring and packaging](../domain/knowledge.md#authoring-and-packaging), and
[D7](d7-custom-code-lives-in-extension-packages.md) and
[D11](d11-durable-process-engine.md). A call can succeed while its
response is lost. A stable operation identity, stored before the first
attempt, lets every retry and every lookup ask about the same call, so the
approved request creates exactly one purchase order. Bindings keep URLs and
secrets out of releases, so one release runs in every tenant and
environment.
