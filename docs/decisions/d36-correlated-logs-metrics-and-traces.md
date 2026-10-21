# D36. Correlated logs, metrics and traces

Status: **Agreed**

The server and the worker emit correlated logs, metrics and traces. Traces,
the stored `trace_id`, OTLP logs, the log scope and the problem details
`traceId` are built. So are the standard metrics and the work item duration,
process instance and call attempt metrics, and the work item gauges and the
computed fields gauge. So is the tenant database gauge of D37. See
[telemetry](../architecture.md#telemetry) for the spans and what telemetry never
holds.
The owner confirmed these points on issue #479 on 2026-10-17.

- **Standard:**
  - The server and the worker use OpenTelemetry for traces, metrics and logs,
    with W3C trace context.
  - Export is OTLP, set by the standard `OTEL_EXPORTER_OTLP_ENDPOINT`
    variables.
  - With no endpoint, nothing is exported and logs still go to the console.
  - Docker Compose and `scripts/dev.sh` start no collector.
- **Traces:**
  - The standard ASP.NET Core, HTTP client and Npgsql instrumentation trace
    incoming requests, outgoing HTTP calls and PostgreSQL commands.
  - Axis adds spans for activation, a work item run, a process step, a
    connector call attempt, a lookup and a package call.
  - A work item stores the trace context of the transaction that queued it.
    Its run starts a new trace with a **link** to that one, not a child span.
    Instances wait for days, so one trace per run stays readable.
  - Connector calls send `traceparent` to the external system.
  - Outgoing HTTP spans drop `server.address`, `server.port` and `url.query`.
    A connector call's `url.full` is the operation's path template. Other
    outgoing spans have no `url.full`.
- **Npgsql:** spans keep the statement text without parameter values.
  Npgsql parameter logging stays off.
- **Stored correlation:**
  - Each history row and each call attempt row stores the `trace_id` of its
    run.
  - Every problem details answer carries `traceId`, so a user can report it
    and an operator can find the run.
  - The process inspector shows the trace id of each history row and call
    attempt.
- **Metrics:**
  - The standard ASP.NET Core, HTTP client and .NET runtime metrics.
  - `axis.work_items.due`, a gauge of due items per kind.
  - `axis.work_items.oldest_due_age`, the age in seconds of the oldest due
    item per kind. Stuck work shows here.
  - `axis.work_items.duration`, a histogram per kind and outcome.
  - `axis.process.instances.ended`, a counter per process and end state.
  - `axis.calls.attempts`, a counter per connector or package, operation and
    outcome class.
  - `axis.computed.pending_fields`, a gauge of pending computed fields, see
    [D33](d33-recomputing-stored-computed-fields.md).
  - `axis.tenant.database.available`, a gauge per tenant, 1 when its database
    answered the server's last readiness probe and 0 when it did not.
- **What telemetry may hold:**
  - Attributes name the tenant, application, process, step, connector,
    operation and work item kind.
  - Telemetry never holds a field value, a secret, a binding value, a request
    or response body, or a user id.
  - Log messages from expressions and steps use fixed texts with names, not
    values. A field marked sensitive is therefore never logged.
  - Finding one user's failed request goes through the stored history, not
    the traces.

_Why:_ [Operations](../domain/knowledge.md#operations) asks that every request,
instance, step attempt and connector call carry correlation identifiers, so an
operator can go from a user-visible error to the exact attempt and its logs.
It also asks for log protection and for durable history instead of sampled
telemetry, so telemetry names things and never holds values, and the stored
history stays the source of truth. Standard OpenTelemetry lets any collector
receive the data. [D11](d11-durable-process-engine.md) keeps step and call
history in the tenant database, and
[D25](d25-connectors-deployment-bindings-and-secrets.md) keeps secrets in
bindings, so the `trace_id` links them without copying their
content.
