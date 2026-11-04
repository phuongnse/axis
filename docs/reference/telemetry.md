# Telemetry

The server and the worker emit correlated logs, metrics and traces with
OpenTelemetry and W3C trace context. Traces and their stored correlation are
built. So are the OTLP logs, the log scope, the problem details `traceId`,
the standard metrics and the work item duration, process instance and call
attempt metrics and the work item and pending computed field gauges. See
[D36](../decisions/d36-correlated-logs-metrics-and-traces.md).

### Signals and export

Both hosts export traces, logs and metrics over OTLP. The standard `OTEL_*`
variables set the export, see
[Telemetry export](host-settings.md#telemetry-export). With no
endpoint, nothing is exported and logs still go to the console. Docker Compose
and `scripts/dev.sh` start no collector. The scenario runner, `Axis.Scenarios`,
is not part of this design.

### Standard instrumentation

- **ASP.NET Core.** Each incoming request is a span. The server follows an
  incoming `traceparent`, as the standard says, so a caller can choose the
  trace id stored with its request.
- **HttpClient.** Each outgoing call is a span, and carries `traceparent`.
  The purchasing system receives it, so both sides can find a call. An
  outgoing span never names a bound address: it drops `server.address`,
  `server.port` and `url.query`. A connector call's `url.full` is the
  operation's path template with `{idempotencyKey}` left unexpanded. Other
  outgoing spans, such as a package operation's calls and the sign-in calls to
  Keycloak, have no `url.full`. A trace processor in each host removes these
  attributes when the span ends, before any exporter sees it.
- **Npgsql.** Each command is a span. It keeps the statement text without
  parameter values. Axis never calls `EnableParameterLogging`, and no
  connection string sets `Include Error Detail`.
- **Metrics.** The standard ASP.NET Core, HttpClient, DNS and .NET runtime
  metric sets. The HttpClient and DNS metrics keep only `error.type`,
  `http.connection.state`, `http.request.method`, `http.response.status_code`,
  `network.protocol.version` and `url.scheme`. So `server.address`,
  `server.port`, `network.peer.address` and `dns.question.name` never reach an
  exporter. The Axis metrics are in
  [worker metrics](process-execution.md#worker-metrics). The server's tenant
  database gauge is in [health checks](health-checks.md), and its counter of
  refused requests, `axis.rate_limits.refused`, is in
  [rate limits](headers-and-rate-limits.md#rate-limits).

### Axis spans

| Span                     | Where             | Attributes                                                                                    |
| ------------------------ | ----------------- | --------------------------------------------------------------------------------------------- |
| `axis.activation`        | server            | `axis.tenant`, `axis.application`                                                             |
| `axis.work_item.run`     | worker            | `axis.tenant`, `axis.work_item.kind`, `axis.outcome`. A link to the span that queued the item |
| `axis.process.step`      | server and worker | `axis.tenant`, `axis.application`, `axis.process`, `axis.step`                                |
| `axis.connector.attempt` | worker            | `axis.tenant`, `axis.application`, `axis.connector`, `axis.operation`, `axis.outcome`         |
| `axis.connector.lookup`  | worker            | `axis.tenant`, `axis.application`, `axis.connector`, `axis.operation`, `axis.outcome`         |
| `axis.package.call`      | worker            | `axis.tenant`, `axis.application`, `axis.package`, `axis.operation`, `axis.outcome`           |

- **Outcomes.** A call span's `axis.outcome` is a call outcome class:
  `succeeded`, `retryable`, `failed` or `unknown`, see
  [call outcomes](process-execution.md#call-outcomes). A work item run's
  outcome is `completed`, `failed` or `fenced`.
- **Links, not children.** A work item stores the trace id and span id of the
  transaction that queued it. Its run starts a new trace with a link to that
  span. Instances wait for days, so a child span would join work days apart
  into one trace. One trace per run stays readable.
- **Errors.** An Axis span that fails records the exception type only, with no
  message.

### Logs

The console always gets logs. OTLP gets them too when an endpoint is set. Each
record carries `TraceId` and `SpanId` in its scope, next to the `TenantId` of
the [log scope](../architecture.md#request-resolution). The console shows scopes, so an operator
can go from a reported `traceId` to its logs with no collector.

- **Failed steps.** A step failure logs the step name, the instance id and the
  same fixed text the step history row stores. It names the error kind, such
  as `DivisionByZero`, and never a value.
- **Failed work items.** A failed work item logs its kind, its id and the
  exception type only. The exception message and its inner exceptions can quote
  a record value, so the log leaves them out.
- **Query timeouts.** An interactive read that runs past `Data:QueryTimeout`,
  or a record write, process start, task complete or inbound endpoint call that
  runs past `Data:WriteTimeout`, logs a warning with the entity, data source or page name as the route spells
  it and how long the read ran, never a value or the query string. The counter
  `axis.queries.timeouts` counts each one by tenant and application, see
  [Errors](record-api.md#errors).
- **Skipped trigger starts.** A trigger that skips a start because its `when`
  or start condition hit a run-time error logs the error kind, never the
  error message.
- **Refused bearer tokens.** A refused token logs a warning with the tenant and
  a reason: a fixed text, or the exception type, never a message or the token.
- **Refused requests.** A `403` logs a warning with the HTTP method and the
  route template, never the path or the query string.
- **Line breaks.** The console formatter indents every line of a message after
  its first, so a value with a line break cannot start a new log entry. It also
  starts each entry with a UTC timestamp.

The [log inventory](log-inventory.md) lists every log.

### Correlation

- **Problem details.** Every problem details answer carries
  a `traceId`, see [Errors](record-api.md#errors).
- **Stored history.** Step history rows and call attempt rows store the
  `trace_id` of their run, see [Tables](process-tables.md#tables). A
  row written with no current span stores null. `traceId` and `trace_id` hold
  the same value, the 32-hex W3C trace id.

So a user reports a `traceId`, and an operator searches the history for it.
Finding one user's failed request goes through the stored history, not the
traces, because telemetry holds no user id.

### What telemetry never holds

- Field values, sensitive or not.
- Secrets.
- Binding values.
- Request and response bodies.
- User ids.
- Npgsql parameter values.
- Exception messages on Axis spans. They hold the type only.

Attributes and log texts name things: the tenant, application, process, step,
connector, package, operation and work item kind. Expression and step log
texts are fixed and hold names, not values. Statement text in Npgsql spans
holds table and column names.
