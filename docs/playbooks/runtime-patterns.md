# Runtime Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for async safety, background work, cancellation, and OpenTelemetry host behavior.

---

## Async fire-and-forget pitfalls

### Unhandled exceptions in fire-and-forget tasks

Calling an async method without `await` discards the `Task`. If that task throws, the exception is silently swallowed — no log, no retry, no error surface.

```csharp
// ❌ wrong — exception from SendEmailAsync is lost forever
_ = _emailSender.SendEmailAsync(to, subject, body);

// ❌ wrong — same problem, different syntax
Task.Run(() => _emailSender.SendEmailAsync(to, subject, body));

// ✅ correct — use Wolverine for all background work
await _messageBus.SendAsync(new SendWelcomeEmailCommand(userId));

// ✅ correct for truly one-off background work — log exceptions explicitly
_ = Task.Run(async () =>
{
    try { await _emailSender.SendEmailAsync(to, subject, body, CancellationToken.None); }
    catch (Exception ex) { _logger.LogError(ex, "Failed to send email to {To}", to); }
});
```

**Rule:** Never fire-and-forget async operations that can fail silently. Use Wolverine's `SendAsync` for background work that needs reliability. If fire-and-forget is truly necessary, always wrap in try/catch with structured error logging.

---

## Async patterns

- **Never sync-over-async**: `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` on a `Task` inside an async call stack causes thread-pool deadlock under ASP.NET Core. Always `await`.
- **Always propagate `CancellationToken`**: every `async` method signature must accept `CancellationToken cancellationToken` and pass it to every downstream call (EF Core, HttpClient, Redis). Use `CancellationToken.None` only at the outermost entry point (e.g. a Wolverine background job handler where the runtime owns the token).

These rules are enforced at build time by **`Microsoft.VisualStudio.Threading.Analyzers`**, wired in [`Directory.Build.props`](../../Directory.Build.props). The relevant diagnostics:

- **VSTHRD002** — synchronously waiting on a Task may cause deadlocks. Catches `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` with type information (no grep false positives from domain types named `Wait` / `Result`).
- **VSTHRD100** — async void methods are unrecoverable; use `async Task` instead.
- **VSTHRD110** — observe the return value of async methods (catches forgotten `await`).

Two rules are intentionally disabled in [`.editorconfig`](../../.editorconfig):

- **VSTHRD200** (Async-suffix naming) clashes with MediatR/Wolverine handler discovery — those frameworks bind on the literal method name `Handle`, not `HandleAsync`.
- **VSTHRD111** (`ConfigureAwait(bool)`) is a WPF/WinForms safeguard; modern ASP.NET Core does not install a `SynchronizationContext` so `.ConfigureAwait(false)` is no-op.

```csharp
// ✅ correct
public async Task<Result<WorkflowDto>> Handle(
    GetWorkflowQuery query,
    CancellationToken cancellationToken)
{
    WorkflowDefinition? wf = await _repository.GetByIdAsync(query.Id, cancellationToken);
    ...
}

// ❌ wrong — deadlock risk + cancellation ignored
public async Task<Result<WorkflowDto>> Handle(GetWorkflowQuery query, CancellationToken _)
{
    WorkflowDefinition? wf = _repository.GetByIdAsync(query.Id).Result;
    ...
}
```

## OpenTelemetry observability

**Principle:** Every host entrypoint emits traces, metrics, and structured logs via the OpenTelemetry SDK ([ADR-018](../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)). Backends are swappable (OTLP → Grafana Tempo/Loki/Mimir in production); application code never references vendor SDKs outside `Axis.Shared.Infrastructure`.

### Host wiring (modulith today, per-module tomorrow)

Register once on the host builder, before module infrastructure:

```csharp
builder.AddAxisOpenTelemetry();

builder.Host.UseSerilog((ctx, services, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<TraceContextSerilogEnricher>());
```

After `WebApplication` is built:

```csharp
app.UseAxisOpenTelemetry(); // Prometheus scrape endpoint when enabled
app.UseAuthentication();
app.UseMiddleware<CorrelationIdMiddleware>(); // after auth so workspace_id → WorkspaceId
app.UseAuthorization();
```

Implementation lives in `Axis.Shared.Infrastructure/Observability/OpenTelemetryServiceExtensions.cs`. Configuration section: `OpenTelemetry` in `appsettings.json`.

| Signal | Export path (dev) | Production backend |
|--------|-------------------|--------------------|
| Traces | OTLP/gRPC (`Otlp:Endpoint`) | Grafana Tempo |
| Metrics | Prometheus scrape at `Prometheus:ScrapeEndpointPath` (default `/metrics`) + optional OTLP | Prometheus → Mimir |
| Logs | Serilog console + `ILogger` → OTLP when `ExportLogs` is true | Grafana Loki |

### Correlation and workspace isolation

- `CorrelationIdMiddleware` uses `X-Correlation-Id` when present, otherwise the active W3C `Activity.TraceId`, and echoes the value on the response.
- Serilog enricher `TraceContextSerilogEnricher` adds `TraceId` / `SpanId` so Loki queries join logs to Tempo traces.
- When the JWT contains `workspace_id`, logs include `WorkspaceId` and the span tag `workspace.id`.

### Local Grafana stack

```bash
python scripts/axis.py local-dev observability up
python scripts/axis.py dotnet run-api
```

Grafana UI: `http://localhost:3001`. OTLP endpoint for an API run outside compose: `http://localhost:4317` (default in `appsettings.json`).

### Rules

- Do **not** register OpenTelemetry in individual module Infrastructure projects while the modulith hosts all modules — one `AddAxisOpenTelemetry` on `Axis.Api` (or each extracted module's entrypoint when that module is deployed standalone).
- Disable telemetry in integration tests via `OpenTelemetry:DisableInTesting` (default `true`) — no Testcontainers for Tempo required.
- Skip instrumentation for `/health`, `/metrics`, and `/swagger` paths (configured in `OpenTelemetryServiceExtensions`).
- **Deferred follow-up:** propagate trace context through Wolverine envelope headers and gRPC interceptors for cross-process module calls ([ADR-018](../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)).

---
