# Domain and Application Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for Axis domain models, application handlers, business failures, aggregate boundaries, and command idempotency.

---

## Key patterns

- Command/Query files live in `Commands/{CommandName}/` or `Queries/{QueryName}/` subfolders
- Repository interfaces defined in `Application/Repositories/`, service interfaces in `Application/Services/`
- `InternalsVisibleTo` in `AssemblyInfo.cs` used for test helpers on domain aggregates
- `Directory.Packages.props` manages all NuGet versions centrally — never add `Version=` to `<PackageReference>` in .csproj
- `tests/Directory.Build.props` auto-adds FluentAssertions + NSubstitute to all test projects

## Result Pattern vs. exceptions — when to use what

| Layer | Mechanism | When |
|-------|-----------|------|
| Domain aggregate | `throw InvalidOperationException` | Internal invariant violated (guard) |
| Application validator | `AbstractValidator<TCommand>` (FluentValidation) | Input validation — `ValidationBehavior` pipeline catches and converts automatically; never throw `ValidationException` manually |
| Application handler | Return `Result` / `Result<T>` | Business rule violation (e.g. duplicate name, entity not found, state conflict) |
| Infrastructure | `throw Exception` (any) | True infrastructure failure (DB down, network timeout, etc.) |

Never throw `ValidationException` from a handler. Never return `Result` from infrastructure code.

## DDD / Aggregate design pitfalls

### 1. Anemic domain model — logic in handler instead of aggregate

If a handler manipulates an aggregate's internals directly instead of calling a domain method, the aggregate has no behavior and invariants cannot be enforced.

```csharp
// ❌ wrong — handler knows too much, invariants unenforceable
WorkflowDefinition wf = await _repo.GetByIdAsync(id, ct);
wf.Status = WorkflowStatus.Published;   // public setter = no guard
wf.UpdatedAt = DateTimeOffset.UtcNow;
wf.PublishedAt = DateTimeOffset.UtcNow;
await _uow.SaveChangesAsync(ct);

// ✅ correct — aggregate enforces its own invariants
WorkflowDefinition wf = await _repo.GetByIdAsync(id, ct);
wf.Publish(); // throws InvalidOperationException if already published / has no steps
await _uow.SaveChangesAsync(ct);
```

**Rule:** Aggregates must expose named domain methods (`Publish`, `AddStep`, `Archive`) that enforce invariants internally. Properties that represent state transitions must have private or `init`-only setters.

---

### 2. Public collection mutation — bypasses domain invariants

Exposing `List<T>` via a public setter or a mutable public property lets callers bypass any guard the aggregate should enforce.

```csharp
// ❌ wrong — caller can add anything with no validation
public List<WorkflowStep> Steps { get; set; } = new();

// ✅ correct — controlled via domain method
private readonly List<WorkflowStep> _steps = new();
public IReadOnlyList<WorkflowStep> Steps => _steps.AsReadOnly();

public void AddStep(WorkflowStep step)
{
    if (_steps.Count >= 50)
        throw new InvalidOperationException("Workflow cannot exceed 50 steps.");
    _steps.Add(step);
}
```

**Rule:** Collection backing fields are always `private readonly List<T>`. The public surface is `IReadOnlyList<T>`. Mutation only via named domain methods.

---

### 3. Raising domain events before the transaction commits

Domain events raised inside an aggregate are dispatched by Wolverine **after** `SaveChangesAsync` completes (via the UnitOfWork outbox). If you dispatch events manually before the commit, a subsequent exception rolls back the DB write but the event is already in-flight — downstream handlers act on data that was never persisted.

```csharp
// ❌ wrong — event dispatched before DB commit
await _messageBus.PublishAsync(new WorkflowPublishedEvent(wf.Id));
await _uow.SaveChangesAsync(ct);

// ✅ correct — aggregate raises the event internally; Wolverine dispatches after commit
wf.Publish(); // internally calls AddDomainEvent(new WorkflowPublishedEvent(Id))
await _uow.SaveChangesAsync(ct); // UnitOfWork dispatches events here, after commit
```

**Rule:** Never call `_messageBus.PublishAsync` or `_messageBus.SendAsync` for domain events in a handler. Aggregates raise events via `AddDomainEvent`; the `UnitOfWork` dispatches them post-commit via Wolverine's outbox.

---

### 4. Mismodeled aggregate boundary — separate AggregateRoot for a dependent entity

A class that cannot exist independently of its parent, shares its lifecycle, and has no standalone identity outside the parent's context should be an `Entity<TId>` inside the parent aggregate — not a separate `AggregateRoot<TId>`.

**Decision rule:** ask three questions:
1. Can this type exist without the parent? (No → entity)
2. Does it have its own lifecycle independent of the parent? (No → entity)
3. Is it only ever accessed via the parent? (Yes → entity)

If all three answers point to "entity", it is part of the parent's aggregate. Modeling it as a separate `AggregateRoot` creates false independence and misplaced domain event responsibilities.

```csharp
// ❌ wrong — ExecutionStep modeled as a separate aggregate root
public sealed class ExecutionStep : AggregateRoot<Guid>
{
    public Guid ExecutionId { get; private set; } // FK back to owner — a red flag
    public void Complete(output) { RaiseDomainEvent(new ExecutionStepCompleted(...)); } // events on child
}

// ✅ correct — ExecutionStep is an entity within WorkflowExecution's aggregate
public sealed class ExecutionStep : Entity<Guid>
{
    public void Complete(output) { /* updates state only — no event raising */ }
}

public sealed class WorkflowExecution : AggregateRoot<Guid>
{
    private List<ExecutionStep> _steps = [];
    public IReadOnlyList<ExecutionStep> Steps => _steps.AsReadOnly();

    public void CompleteStep(Guid stepId, IReadOnlyDictionary<string, object?> output)
    {
        GetStep(stepId).Complete(output);           // delegate state change to entity
        RaiseDomainEvent(new ExecutionStepCompleted(Id, stepId, workspaceId, output)); // event on root
    }
}
```

**Consequences of correct modeling:**
- Domain events are raised by the aggregate root, ensuring they are dispatched within its transaction boundary
- Owned entities do NOT need `DeletedAt` — they are deleted when the root is deleted (EF Cascade)
- EF Core: use `OwnsMany` with a separate table (see [OwnsMany pattern](./persistence-patterns.md#ef-core-ownsmany-pattern)) instead of a standalone `DbSet<ChildEntity>`
- Child entities are always accessed through the root — no standalone repository for them

**Correct boundary (keep as separate AggregateRoot) when:**
- The entity can be created without the parent (e.g. a submission aggregate that references a workflow execution by ID cross-module)
- The entity has its own soft-delete, audit lifecycle, and events independent of the parent
- Volume is large enough that loading via parent is impractical (e.g. a records aggregate in a data-modeling module — can number in the millions)

### 4a. When to split one concept into two aggregates

The question "should these be one aggregate or two?" is separate from "entity vs aggregate root." It applies when two related concepts are both candidates to be aggregate roots. Only split when at least one of the following is true:

1. **Relationship is 1:\*** — the relationship can grow to one-to-many. A 1:1 relationship that will never become 1:\* does not justify the overhead of a second aggregate root.
2. **Independent domain behavior** — the second concept has its own domain methods, invariants, or events that make sense to reason about independently of the first. A pure data holder with no domain logic does not qualify.
3. **Independent lifecycle** — the second concept can transition state, be created, or be deleted independently of the first. If both always change together (same command, same transaction), combining them is simpler and correct.

**When to combine into one aggregate:** the relationship is 1:1, the second concept has no domain behavior of its own (it is a data record or state snapshot), and both concepts share the same lifecycle transitions. Creating a second aggregate root in this case adds indirection and a cross-aggregate join without any modelling benefit.

```text
// Diagnostic questions — if all answers are "no", combine into one aggregate:
// 1. Can the second concept exist without the first? (No → combine)
// 2. Does the second concept have domain methods that make sense alone? (No → combine)
// 3. Can the relationship become 1:* in this bounded context? (No → combine)
```

---

## Axis layering (SRP at a glance)

Handlers orchestrate one command or query; aggregates own invariants and raise domain events; repositories persist only. Do not publish domain events from handlers, send email from handlers, or mutate aggregate state without calling aggregate methods.

Long-form SOLID and Gang-of-Four catalogs are intentionally omitted here — they duplicate generic material and are not Axis-specific. Use [patterns-index.md](./patterns-index.md) to jump to the section you need; add new **principle + one example** entries to the focused owner doc for the surface.

| Task | Owner |
|------|-------|
| Business failures vs exceptions | [Result Pattern vs. exceptions](#result-pattern-vs-exceptions--when-to-use-what) |
| HTTP status from `Result` | [API patterns](./api-patterns.md#result--http-status-code-mapping) |
| Domain events / jobs | [Wolverine patterns](./wolverine-patterns.md) |
| Another module's data | [Cross-module patterns](./cross-module-patterns.md) |
| Handler / repository layout | [Key patterns](#key-patterns) |

---

## Command idempotency pattern

All Command handlers must be safe to retry without producing duplicate side effects.

**Pattern 1 — check-before-create (most commands):**

```csharp
public async Task<Result<Guid>> Handle(CreateWorkflowCommand cmd, CancellationToken ct)
{
    bool exists = await _repo.ExistsByNameAsync(cmd.Name, cmd.workspaceId, ct);
    if (exists)
        return Result.Failure<Guid>(Error.Conflict($"A workflow named '{cmd.Name}' already exists."));

    WorkflowDefinition wf = WorkflowDefinition.Create(cmd.Name, cmd.workspaceId, cmd.CreatedByUserId);
    await _repo.AddAsync(wf, ct);
    await _uow.SaveChangesAsync(ct);
    return Result.Success(wf.Id);
}
```

**Pattern 2 — caller-supplied idempotency key (for external triggers):**

Commands triggered by external systems (webhooks, scheduled jobs) accept a caller-supplied `IdempotencyKey` (UUID). The handler checks if a record with that key already exists and returns the existing result without re-executing.

When a handler releases a caller-supplied idempotency key after a business failure, the release must not flush unrelated tracked entities from the same `DbContext`. Prefer a set-based status update (`ExecuteUpdateAsync`) in the idempotency repository, and add a regression test when the failure path happens after the handler has built other aggregate objects.

**Pattern 3 — try-catch `DbUpdateException` for Wolverine at-least-once concurrent INSERT race:**

Check-before-insert (Pattern 1) handles the *sequential* duplicate case but not the *concurrent* race: two handler invocations can both read `existing = null`, both attempt INSERT, and one will throw a unique constraint violation. For Wolverine event handlers where at-least-once delivery can cause parallel execution, wrap `SaveChangesAsync` in a try-catch.

The example below uses `WorkflowPublished`/`WorkflowActiveStatus` as concrete types — substitute your own event and read model:

```csharp
public async Task Handle(WorkflowPublished @event, CancellationToken ct)
{
    WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
        .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

    if (existing is null)
        context.WorkflowActiveStatuses.Add(
            WorkflowActiveStatus.Activated(@event.WorkflowId, @event.workspaceId));
    else
        existing.Reactivate();

    try
    {
        await context.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
        // Concurrent duplicate event delivery — row already inserted by a parallel invocation.
    }
}
```

Use this pattern whenever a Wolverine handler does a check-before-insert and concurrent execution is plausible — check-before-insert is not race-safe on its own.

**Migrations — idempotent raw SQL:**

EF-scaffolded migrations are idempotent by default. When using `migrationBuilder.Sql(...)` for custom DDL or data migrations, always add an existence check:

```csharp
migrationBuilder.Sql(@"
    DO $$ BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'workflow_status') THEN
            CREATE TYPE workflow_status AS ENUM ('Draft', 'Published', 'Archived');
        END IF;
    END $$;
");
```
