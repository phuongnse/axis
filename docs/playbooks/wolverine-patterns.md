# Wolverine Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for Wolverine host setup, handlers, idempotency, logging, recurring jobs, and module-local message execution.

---

## Wolverine patterns

### Host setup (ADR-012 — per-module `wolverine` schema)

Each module owns a `wolverine` schema **inside its own PostgreSQL database** ([ADR-011](../TECH_STACK.md#adr-011-per-module-database-with-schema-per-workspace-inside), [ADR-012](../TECH_STACK.md#adr-012-per-module-wolverine-schema-in-the-modules-own-database)). There is **no** `ConnectionStrings:Wolverine` — persistence is wired from each module's connection string.

```csharp
// Axis.Api/Program.cs — excerpt
builder.Host.UseWolverine(opts =>
{
    string identityConnectionString = configuration.GetConnectionString("Identity")!;
    string dataModelingConnectionString = configuration.GetConnectionString("DataModeling")!;
    // ... WorkflowBuilder, FormBuilder, WorkflowEngine ...

    opts.Policies.AddMiddleware<HandlerLoggingMiddleware>();
    opts.UseEntityFrameworkCoreTransactions();

    // Main store: node/agent coordination (Identity DB).
    opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine");

    // Per-module ancillary outbox — enrolled per DbContext.
    opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine", MessageStoreRole.Ancillary)
        .Enroll<IdentityDbContext>();
    opts.PersistMessagesWithPostgresql(dataModelingConnectionString, "wolverine", MessageStoreRole.Ancillary)
        .Enroll<DataModelingDbContext>();
    // ... Enroll WorkflowBuilderDbContext, FormBuilderDbContext, WorkflowEngineDbContext ...

    // Cross-module transports (ADR-013, ADR-024, ADR-025) — Kafka + RabbitMQ ...
});

// Dev + Testing: Wolverine creates each module's `wolverine` tables on startup.
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddResourceSetupOnStartup();
```

**Rules:**

- Never point two modules at one shared `wolverine` schema in a shared database — extraction breaks.
- `AddResourceSetupOnStartup()` is for dev/Testing only; production applies Wolverine DDL per module DB in CI.
- Cross-module messages use Kafka (`*Event`/`*Snapshot`) and RabbitMQ (`*Command`/`*Job`/`*SagaStep`) per [ADR-025](../TECH_STACK.md#adr-025-transport-selection-rule-by-message-name-suffix) — not a central Postgres Wolverine connection string.

### Intra-module domain event handler

Domain events raised by aggregates (`AddDomainEvent`) are collected in `UnitOfWork.SaveChangesAsync` and published via `IMessageBus` after `SaveChangesAsync` commits. With `UseEntityFrameworkCoreTransactions()` and `Enroll<TDbContext>()`, handler code that shares the enlisted `DbContext` participates in the durable outbox. Define the handler in the Application layer:

```csharp
// WorkflowBuilder.Application/EventHandlers/WorkflowPublishedEventHandler.cs
public sealed class WorkflowPublishedEventHandler
{
    // Wolverine resolves by convention — no registration needed.
    // Method name: Handle or HandleAsync.
    public async Task HandleAsync(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        // Runs after the transaction that raised the event commits.
        // Safe to read the fully-persisted aggregate here.
    }
}
```

### Inter-module domain event handler

Same pattern — the handler lives in the Application layer of the **consuming** module. Wolverine routes the event by message type, regardless of which module raised it:

```csharp
// WorkflowEngine.Application/EventHandlers/WorkflowPublishedEventHandler.cs
// Consumes WorkflowPublishedEvent raised by WorkflowBuilder — no direct module reference needed.
public sealed class WorkflowPublishedEventHandler
{
    public async Task HandleAsync(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        // Validate the published workflow is executable, pre-warm caches, etc.
    }
}
```

### Reliable background job

```csharp
// Schedule fire-and-forget work that must not be lost if the process crashes
await _messageBus.SendAsync(new SendWelcomeEmailCommand(userId));

// Handler (Wolverine resolves by convention):
public sealed class SendWelcomeEmailHandler(IEmailSender emailSender)
{
    public async Task HandleAsync(SendWelcomeEmailCommand cmd, CancellationToken ct)
    {
        await emailSender.SendAsync(cmd.UserId, ct);
    }
}
```

### Step handler idempotency — at-least-once delivery

Wolverine delivers messages at least once. Step handlers guard against redelivery using **`step.IsTerminal`** (Completed / Failed / Cancelled), not the Running status.

**Why not the Running guard?** `ExecuteNextStepHandler` starts every step (sets it to `Running`) before dispatching the typed handler message. By the time `ExecuteHttpStepHandler`, `ExecuteConditionStepHandler`, etc. receive the message, the step is **already Running** — this is the expected, normal first-delivery state. A `if (step.Status == Running) return;` guard would therefore block all normal executions.

```csharp
// ✅ correct idempotency guard in typed step handlers (Http, Condition, Script, Notification)
if (step.IsTerminal)   // Completed / Failed / Cancelled — already processed
{
    return;
}

// ⛔ wrong — Running is the normal first-delivery state for these handlers
if (step.Status == StepExecutionStatus.Running)
{
    return;  // this would block every first delivery!
}
```

**Form step exception:** `ExecuteFormStepHandler` checks `step.Status == Waiting` (not Running) for idempotency because its job is to *transition* Running → Waiting. If the step is already Waiting, the form task was already created.

**True concurrent-duplicate protection** uses `UseXminAsConcurrencyToken()` — see the section below.

### Concurrent-duplicate protection via xmin optimistic concurrency

When two Wolverine workers receive the same message simultaneously, the **first writer wins**. The second writer is detected via PostgreSQL's built-in `xmin` system column and exits gracefully.

#### Infrastructure setup — EF Core mapping

```csharp
// On a top-level entity (EntityTypeBuilder<T>):
builder.UseXminAsConcurrencyToken();

// On an owned entity (OwnedNavigationBuilder) — UseXminAsConcurrencyToken() is NOT available;
// configure xmin manually instead:
stepBuilder.Property<uint>("xmin")
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
// "No migration needed — xmin is a PostgreSQL built-in system column present on every row."
```

No `uint RowVersion` field on the domain entity is required. EF Core adds `WHERE xmin = @loadedXmin` to every UPDATE automatically. Any second concurrent UPDATE on the same row fails with `DbUpdateConcurrencyException`.

#### Infrastructure setup — UnitOfWork translation

`DbUpdateConcurrencyException` is an EF Core type and must not leak into the Application layer. The shared `UnitOfWork` translates it:

```csharp
// Axis.Shared.Infrastructure/Persistence/UnitOfWork.cs
catch (DbUpdateConcurrencyException ex)
{
    throw new ConcurrencyException(ex);
}
```

`ConcurrencyException` lives in `Axis.Shared.Application` — Application layer can reference it without taking an EF Core dependency.

#### Application handler pattern — catch and exit

```csharp
// ✅ In every handler that calls uow.SaveChangesAsync:
try
{
    await uow.SaveChangesAsync(ct);
}
catch (ConcurrencyException)
{
    // Another Wolverine worker already committed this change.
    // The winning instance will complete the step — exit gracefully.
    logger.LogInformation(
        "Concurrent delivery detected for step {StepId} — skipping", stepId);
    return;
}
```

**Rules:**
- Never re-throw `ConcurrencyException` as a step failure — the winning instance already completed the work.
- Every `uow.SaveChangesAsync` call on a concurrency-sensitive path must be wrapped.
- Do not use a Running status guard as the concurrency boundary — that blocks normal first deliveries (see idempotency section above).
- `UseXminAsConcurrencyToken()` is NOT available on `OwnedNavigationBuilder<TOwner, TDep>`. For owned entities configured with `OwnsMany`, configure xmin manually:
  ```csharp
  stepBuilder.Property<uint>("xmin")
      .HasColumnName("xmin")
      .HasColumnType("xid")
      .ValueGeneratedOnAddOrUpdate()
      .IsConcurrencyToken();
  ```

### Wolverine handler logging — two-layer rule

Handler logging has two mandatory layers that serve different purposes:

**Layer 1 — Cross-cutting infrastructure logging (Wolverine middleware)**

`HandlerLoggingMiddleware` (in `Axis.Shared.Infrastructure/Wolverine/`) wraps every Wolverine handler automatically. Registered globally in `Program.cs`:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AddMiddleware<HandlerLoggingMiddleware>();
    opts.UseEntityFrameworkCoreTransactions();
    // ...
});
```

The middleware logs unhandled handler exceptions via `OnException(Exception, Envelope)` — do **not** add `Exception` to `Finally`; Wolverine treats it as a DI service. It provides consistent operational traces without touching each handler — enforce it by policy, not by developer memory.

**Layer 2 — Per-handler business milestone logging (mandatory)**

Every Wolverine handler must log:

| Scenario | Level | Example |
|----------|-------|---------|
| Entity not found (execution, step) | `Warning` | `"ExecutionId {Id} not found"` |
| Idempotency skip (step already terminal) | `Information` | `"step {Id} already terminal ({Status}), skipping"` |
| Concurrent delivery detected | `Information` | `"concurrent delivery for step {Id} — skipping"` |
| Config error / invariant violation | `Warning` or `Error` | `"invalid config for step {Id}"` |
| Dispatching StepFailedMessage | `Warning` | `"no branches configured — failing execution {Id}"` |
| Successful business outcome | `Information` | `"step {Id} completed in execution {Id}, advancing"` |
| Execution terminal state reached | `Information` | `"Execution {Id} completed successfully"` |

**Rules:**
- `ILogger<THandler>` is mandatory in every Wolverine handler (Application and Infrastructure layer). No handler may omit it.
- Business milestones must be logged per-handler — the middleware has no domain context (execution ID, step type, branch label).
- Log the failure reason before dispatching `StepFailedMessage` — the log must appear regardless of whether the downstream handler processes the failure.
- Log successful outcomes BEFORE the final `dispatcher.PublishAsync` call — a dispatch that throws would otherwise leave a milestone un-logged.
- The middleware `Error` log catches unhandled exceptions that bubble past the handler; per-handler `Error` logs are for handled error paths (e.g., invalid config, executor exception).
- Domain layer: zero logging — aggregates have no external dependencies. API layer: zero per-endpoint logging — ASP.NET Core request logging middleware handles it.

### Scheduled / recurring job

```csharp
// One-time delayed job:
await _messageBus.ScheduleAsync(new CleanUpExpiredSessionsCommand(), TimeSpan.FromMinutes(30));

// Recurring cron job — registered in UseWolverine opts:
opts.ScheduleJob<ArchiveOldExecutionsCommand>(cron: "0 2 * * *"); // daily at 02:00
```

**Rules:**
- Never use `Task.Run` for background work that must be reliable — use Wolverine `SendAsync`.
- Domain event handlers live in the **Application** layer of the consuming module; never in Domain or Infrastructure.
- Never call `_messageBus.PublishAsync` for a domain event inside a Command handler — the `UnitOfWork` dispatches events automatically via Wolverine after commit.
- Integration events to external systems (webhooks, third-party APIs) are Wolverine jobs scheduled from within a domain event handler, not from the command handler directly.

---
