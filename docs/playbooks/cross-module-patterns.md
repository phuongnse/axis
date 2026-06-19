# Cross-module Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for module data sovereignty, event-driven local read models, and cross-module violation sweeps.

---

## Cross-module communication pattern

**Core rule: a module only queries its own database. Always. No exceptions.** This is the Share Nothing principle from [ADR-010](../TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy) — modules are data-sovereign so that extraction is a redeploy, not a refactor.

Cross-module needs are met by exactly two mechanisms:

| Need | Mechanism | When to use |
|---|---|---|
| React to state change in another module | **Kafka event** (Avro payload + CloudEvents envelope) | Always the default. The consuming module maintains a **local read model** populated by the event. |
| Read fresh data the local read model cannot satisfy | **gRPC sync call** to the source module's `IdentityService` / `WorkflowService` / etc. | Escape hatch only. Used when eventual consistency is unacceptable (e.g. fresh permission check). |

Direct DbContext access, in-process method calls into another module's services, raw SQL across modules, and `MediatR` dispatch across modules are all P0 violations.

### ❌ Anti-pattern A: cross-module raw SQL

```csharp
// WRONG — module A querying a table owned by module B
int count = await context.Database
    .SqlQueryRaw<int>("SELECT COUNT(*) FROM workflow_definitions WHERE steps @> {0}::jsonb", ...)
    .FirstAsync(ct);
```

### ❌ Anti-pattern B: in-process call dressed as an interface

```csharp
// WRONG — module A injecting module B's Application service
public class SomethingHandler(IWorkflowQueryService workflowQueries)  // ← B's service
{
    public async Task Handle(...)
        => await workflowQueries.GetWorkflowAsync(...);   // ← in-process call, no contract
}
```

This compiles and runs in the modulith, but the moment module B is extracted, the project reference disappears and `IWorkflowQueryService` is no longer reachable. Cross-module sync **must** go through gRPC (with a versioned `.proto` contract) — see [ADR-014](../TECH_STACK.md#adr-014-grpc-for-internal-sync-rpc-and-rest-openapi-for-external-api).

### ✅ Pattern 1: event-driven local read model (default)

When module A needs to know something about module B's data, B publishes an event when that data changes. A maintains its own local copy via a Wolverine handler that consumes from a Kafka topic.

**Example** (FormBuilder / WorkflowBuilder): FormBuilder needs to know whether a form is referenced by any workflow step.

**Step 1 — Define the event schema in B's Contracts project (Avro + CloudEvents):**

Implemented for WorkflowBuilder lifecycle events in `Axis.WorkflowBuilder.Contracts/Schemas/` (regenerate C# with `avrogen` — see project `Schemas/` folder). Example shape:

```text
src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas/FormStepAdded.avsc
{
  "type": "record",
  "namespace": "axis.workflowbuilder",
  "name": "FormStepAdded",
  "fields": [
    { "name": "workflowId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "stepId",     "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "formId",     "type": { "type": "string", "logicalType": "uuid" } }
  ]
}
```

The Avro file is registered with Confluent Schema Registry at build time. CI rejects breaking changes per [ADR-019](../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope).

**Step 2 — Source module (WorkflowBuilder) raises the domain event and publishes it via the outbox:**

```csharp
// WorkflowBuilder.Domain — raise the domain event inside the aggregate
RaiseDomainEvent(new FormStepAdded(Id, step.Id, formId));

// WorkflowBuilder.Infrastructure — Wolverine handler maps domain event → Kafka envelope
public sealed class FormStepAddedPublisher
{
    public async Task Handle(FormStepAdded evt, IMessageBus bus, CancellationToken ct)
    {
        // Wolverine routes to the configured Kafka topic; Avro+CloudEvents serialisation
        // is handled by middleware so handlers stay free of transport concerns.
        await bus.PublishAsync(evt);
    }
}
```

**Step 3 — Consuming module (FormBuilder) stores its own local read-model copy:**

```csharp
// FormBuilder.Domain — local read model, owned by FormBuilder's DB
public sealed class FormWorkflowReference
{
    public Guid FormId { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid StepId { get; init; }
    public Guid workspaceId { get; init; }
}

// FormBuilder.Infrastructure — Wolverine handler reads from the Kafka inbox
public sealed class FormStepAddedHandler(FormBuilderDbContext db)
{
    public async Task Handle(FormStepAdded evt, CancellationToken ct)
    {
        // Idempotent: Kafka delivers at-least-once; Wolverine inbox dedupes by envelope ID
        // but defensive upsert at the read-model level is still good practice.
        bool exists = await db.FormWorkflowReferences.AnyAsync(
            r => r.FormId == evt.FormId
              && r.WorkflowId == evt.WorkflowId
              && r.StepId == evt.StepId, ct);
        if (exists) return;

        db.FormWorkflowReferences.Add(new FormWorkflowReference
        {
            FormId = evt.FormId,
            WorkflowId = evt.WorkflowId,
            StepId = evt.StepId,
            workspaceId = evt.workspaceId,   // always denormalise workspace
        });
        await db.SaveChangesAsync(ct);
    }
}
```

**Step 4 — Consuming module queries only its own table:**

```csharp
// FormRepository.IsReferencedByWorkflowAsync — queries FormBuilder's own DB
public async Task<bool> IsReferencedByWorkflowAsync(Guid formId, Guid WorkspaceId, CancellationToken ct = default)
    => await context.FormWorkflowReferences.AnyAsync(
           r => r.FormId == formId && r.workspaceId == WorkspaceId, ct);
```

For synchronous RPC, Buf, and JWKS validation details, see [gRPC patterns](./grpc-patterns.md).

### Rules (P0)

- **Never** use `SqlQueryRaw`, `ExecuteSqlRaw`, `FromSqlRaw`, or any raw SQL that references a table from another module.
- **Never** inject another module's `DbContext`, repository, or Application service into your code. Only `Axis.{Module}.Contracts` may be referenced cross-module.
- **Never** dispatch a cross-module event through `IMediator` — `MediatR` is intra-module only.
- The source module owns the event schema — define it in its `.Contracts` project's Avro file.
- The consuming module owns the handler and the local read-model table — both in its Infrastructure layer.
- Kafka handlers are idempotent: at-least-once delivery is the rule; design for replay.
- **Cross-module handlers must filter by `workspaceId`** in addition to entity foreign keys. Workspace isolation is always explicit.
- Sync RPC (gRPC) is the escape hatch only. If you reach for it more than once or twice per module, you probably need another local read model instead.
- **JWT validation is JWKS-only.** Consuming modules verify JWTs against Identity's JWKS endpoint locally; never call `IdentityDbContext` or any Identity service per request just to authenticate a user.
- **Cross-module gRPC services derive `workspace_id` from the caller's JWT `workspace_id` claim**, never from a request field. The proto must not declare an `workspace_id` input; the server reads `ServerCallContext.GetHttpContext().User.FindFirst("workspace_id")` and throws `Unauthenticated` when the claim is absent.

### Pre-commit violation sweep

```bash
grep -rn "SqlQueryRaw\|ExecuteSqlRaw\|FromSqlRaw\|ExecuteSqlInterpolated" src/Modules/ --include="*.cs"
```

For every match: confirm the SQL only references tables owned by that match's own module. If it references another module's table → P0 violation, must fix before committing. `python scripts/axis.py check doc-drift` also enforces this on PR.

---
