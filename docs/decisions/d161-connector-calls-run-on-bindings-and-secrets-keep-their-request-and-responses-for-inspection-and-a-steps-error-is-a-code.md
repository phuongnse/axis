# D161. Connector calls run on bindings and secrets, keep their request and responses for inspection, and a step's error is a code

Status: **Agreed**

## Context

Processes must call external systems, such as the purchasing system that creates a purchase order.
A call can succeed while its response is lost, so a blind retry can create a second order, and
[D11](d11-durable-process-engine.md) keeps every external call out of a transaction. The URLs and
secrets of a system also differ per tenant and environment, while one release must run in all of
them.

An operator who inspects a failed call cannot see what was sent or what the outside system answered,
because no body is stored. A stored error is English text, so a page cannot word it in the reader's
language. This decision replaces D25 and keeps every part of it that still holds, as
[D155](d155-processes-run-paths-at-once-repeat-steps-give-tasks-to-many-people-and-take-error-paths.md)
does.

## Decision

- **A release names its URLs and secrets as deployment bindings** and never holds their values. The
  values live per tenant in the host configuration or the
  [tenant directory](d42-tenant-directory-in-the-platform-database.md), and a host reads a binding
  when it makes a call. Activation fails when a binding the release uses is missing or of the wrong
  kind.
- **Secret values come from the environment,** except in Development, and never appear in a
  release, history, audit record, log or problem details.
- **A `connector` resource describes a system:** its base URL, its authentication, its timeout and
  typed operations. An operation may name the system's idempotency header and a lookup that finds an
  earlier call by its key. [D91](d91-connector-oauth2-and-structured-payloads.md) adds OAuth2 and
  structured payloads.
- **Calls go only to `https`** outside Development, and never follow a redirect, which could send a
  secret to another host.
- **A call is staged.** The step's transaction stores the call with a stable operation identity, and
  the instance waits. The worker sends the call later, with the identity as the idempotency key of
  every attempt and lookup, and sends the input stored with the call.
- **Each outcome is failed, retryable or unknown.** A step's retry policy sets the attempts and a
  doubling backoff, and the worker ignores `Retry-After`, so backoff is decided in one place.
- **An unknown outcome is never retried blindly.** It is sent again only under the system's
  idempotency key, else reconciled through the lookup, else it stays visible until an operator
  resolves it ([D35](d35-operator-retry-cancel-and-resolve.md)).
- **A settled call writes one audit record.**
- **A kept request and response.** _(planned)_ A connector call keeps its request and each attempt's
  response, with the body as text, unchanged. A body longer than a size limit is cut and marked
  `truncated`. Every secret is kept as `[secret]`, under the masking rule of the call's input, and the
  call keeps them for as long as the instance's history. They are never copied to history, audit
  records or logs.
- **A step's error is a code.** _(planned)_ A path entry and an attempt carry `problem`, a code with
  its values, such as `timeout` with `seconds`. The page words it through `shell.stepProblem.<code>`
  in the reader's language, and a row with no `problem` shows its stored error.
- **No stack trace or exception message on an application's pages.** _(planned)_ Errors are
  structured, so an application's pages never show an exception's type, message or stack trace.
- **The instance detail carries what the instance page needs.** _(planned)_ The
  [instance detail](../reference/process-inspection.md#instance-detail) holds the process title, the
  labels, who started the instance, its input and result, and the values that a run read and made,
  each under the masking rule.

The detail is in
[connectors and deployment bindings](../reference/integration-resources.md#connectors-and-deployment-bindings),
[connector calls](../reference/process-execution.md#connector-calls) and
[process inspection](../reference/process-inspection.md#call-input-kept).

## Consequences

- The approved purchase request creates exactly one purchase order, even after a lost response or a
  crash.
- One release runs in every tenant and environment, and changing a secret needs no new release.
- A connector without an idempotency header depends on its lookup being correct, and one without
  either needs an operator for every unknown outcome.
- The server and the worker must both be able to read each tenant's binding values.
- **An operator sees each call's traffic.** _(planned)_ An operator sees what a call sent and what each
  attempt answered. Each kept body is bounded by the size limit, which bounds the tenant database's growth
  per call.
- **A page words each error.** _(planned)_ A page words an error in the reader's language, and a row
  without a code falls back to its stored error.

## Alternatives

- **URLs and secrets in the release.** A release would then differ per tenant and environment, and
  carry secrets.
- **Storing no request or response body.** An operator could not see what was sent or answered, and
  the call would still have to be explained from the stored error alone.
- **Showing the stored error text.** It is English text that a page cannot word in the reader's
  language, so the page would show one language to every reader.
