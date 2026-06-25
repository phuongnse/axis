# Runtime Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Runtime code must be cancellable, observable, and safe under retry.

## Async fire-and-forget pitfalls

Do not drop tasks. Use Wolverine/background infrastructure for durable work, or explicitly observe/log task failures when fire-and-forget is truly local.

### Unhandled exceptions in fire-and-forget tasks

Unhandled async exceptions can disappear or crash late. Prefer awaited or queued work.

## Async patterns

Forward `CancellationToken`. Avoid sync-over-async. Keep async void out of production code except framework-required event handlers.

## OpenTelemetry observability

Trace module boundaries, background jobs, external calls, and workspace context without PII.

### Host wiring (modulith today, per-module tomorrow)

Wire telemetry at `Axis.Api` composition in modulith mode and keep module boundaries visible in spans.

### Correlation and workspace isolation

Propagate correlation IDs and workspace identifiers; do not log sensitive payloads.

### Local Grafana stack

Use local observability only when debugging telemetry or performance.

### Rules

Logs are structured, cancellation is forwarded, background work is durable when side effects matter, and health endpoints stay anonymous.
