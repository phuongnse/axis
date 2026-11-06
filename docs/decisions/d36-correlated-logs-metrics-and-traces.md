# D36. Correlated logs, metrics and traces

Status: **Agreed**

## Context

[Operations](../domain/knowledge.md#operations) asks that every request, instance, step attempt and
connector call carry correlation identifiers, so an operator can go from a user-visible error to the
exact attempt and its logs. It also asks for log protection, and for durable history rather than
sampled telemetry as the record.

## Decision

- **OpenTelemetry.** The server and the worker emit traces, metrics and logs with OpenTelemetry and
  W3C trace context, exported over OTLP as the standard variables set, and always log to the console.
- **Axis adds spans** for activation, a work item run, a process step, a connector call attempt, a
  lookup and a package call. A work item's run starts a new trace with a link to the one that queued
  it, not a child span, and connector calls send `traceparent`.
- **The stored history carries the trace id.** Step history and call attempt rows store the
  `trace_id` of their run, every problem details answer carries `traceId`, and the process inspector
  shows it.
- **Metrics show stuck and failed work,** besides the standard metrics.
- **Telemetry names things and never holds values:** no field value, secret, binding value, body or
  user id.

The spans, logs and what they leave out are in [telemetry](../reference/telemetry.md), and the metrics
in [worker metrics](../reference/process-execution.md#worker-metrics) and
[health checks](../reference/health-checks.md).

## Consequences

- Any collector can receive the data.
- A user reports a `traceId`, and an operator finds the run in the history and the logs. The trace id
  links them without copying the history's content.
- Finding one user's failed request goes through the stored history, not the traces.

## Alternatives

- **One trace for a whole instance.** Instances wait for days, so it would not stay readable.
