# Persistence Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for EF Core mappings, repositories, workspace schemas, and migration-safe persistence behavior.

---

## EF Core JSONB collection change tracking

**Problem:** EF Core uses reference equality to detect changes on `List<T>` properties backed by a `ValueConverter`. When you call `list.Add()` or `list.Remove()` in-place, the list reference stays the same → EF Core sees no change → the mutation is silently not persisted.

**Wrong fix (what not to do):** Overriding `SaveChangesAsync` in the DbContext to forcibly mark the property as modified:
```csharp
// ❌ incomplete workaround — only fires when entity is already Modified.
// If ONLY the JSONB field mutated, entity state is Unchanged → changes are LOST silently.
foreach (var entry in ChangeTracker.Entries<MyAggregate>()
    .Where(e => e.State == EntityState.Modified))
{
    entry.Property("_items").IsModified = true;
}
```

**Correct fix: always pair `HasConversion` with `HasValueComparer`**

The example below uses `MyAggregate` / `ItemDto` as placeholders — substitute your actual aggregate and element types:

```csharp
internal sealed class MyAggregateConfiguration : IEntityTypeConfiguration<MyAggregate>
{
    private static readonly ValueConverter<List<ItemDto>, string> ItemsConverter =
        new(
            items => JsonSerializer.Serialize(items, JsonOptions.Options),
            json => JsonSerializer.Deserialize<List<ItemDto>>(json, JsonOptions.Options)
                    ?? new List<ItemDto>());

    private static readonly ValueComparer<List<ItemDto>> ItemsComparer =
        new(
            (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
            l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            l => l.ToList()); // deep copy — this is what makes the snapshot correct

    public void Configure(EntityTypeBuilder<MyAggregate> builder)
    {
        builder.Property<List<ItemDto>>("_items")
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(ItemsConverter, ItemsComparer) // ← always both together
            .IsRequired();
    }
}
```

With `ValueComparer`:
1. EF Core deep-copies the list at load time (real snapshot, not reference copy)
2. At save time, compares content — not reference
3. If different → automatically marks modified, even when ONLY the JSONB collection changed

**Rule:** Every `HasConversion` on a `List<T>` or collection property stored as JSONB must be accompanied by a `ValueComparer`. No exceptions. A `ValueConverter` without a `ValueComparer` on a mutable collection is a silent data loss bug.

## EF Core common pitfalls

### 1. AsNoTracking on a write path — silent no-save

`AsNoTracking()` detaches entities from the change tracker. Calling `SaveChangesAsync` afterward persists nothing and throws no error.

```csharp
// ❌ wrong — entity is detached, SaveChanges does nothing
WorkflowDefinition? wf = await _context.WorkflowDefinitions
    .AsNoTracking()
    .FirstOrDefaultAsync(w => w.Id == id, ct);
wf!.Publish();
await _context.SaveChangesAsync(ct); // silent no-op

// ✅ correct — tracking query for write paths
WorkflowDefinition? wf = await _context.WorkflowDefinitions
    .FirstOrDefaultAsync(w => w.Id == id, ct);
wf!.Publish();
await _context.SaveChangesAsync(ct);
```

**Rule:** `AsNoTracking()` is for read-only queries only. Any repository method used on a write path must omit it.

---

### 2. SaveChanges inside a repository/store — breaks Unit of Work

Calling `SaveChangesAsync` inside a repository, store, or persistence helper commits a partial transaction. If a second operation in the same handler fails afterward, the first change is already persisted — no rollback possible.

```csharp
// ❌ wrong — partial commit, breaks atomicity
public async Task AddAsync(WorkflowDefinition wf, CancellationToken ct)
{
    await _context.WorkflowDefinitions.AddAsync(wf, ct);
    await _context.SaveChangesAsync(ct); // ← never here
}

// ✅ correct — only add to context; let the handler call UnitOfWork
public async Task AddAsync(WorkflowDefinition wf, CancellationToken ct)
{
    await _context.WorkflowDefinitions.AddAsync(wf, ct);
}
// Handler calls: await _uow.SaveChangesAsync(ct) at the end
```

**Rule:** Repositories, token stores, and persistence helpers only interact with `DbSet<T>` or set-based EF operations. `SaveChangesAsync` is called via `IUnitOfWork` in the handler, never inside these helpers.

---

### 3. Returning IQueryable from a repository — leaks infrastructure

Returning `IQueryable<T>` from a repository lets callers compose queries outside the Infrastructure layer, coupling Application code to EF Core internals.

```csharp
// ❌ wrong — caller can append .Include(), .Where(), anything
public IQueryable<WorkflowDefinition> GetAll() => _context.WorkflowDefinitions;

// ✅ correct — all query logic stays in the repository
public async Task<PagedResult<WorkflowSummaryDto>> GetPagedAsync(
    int page, int pageSize, CancellationToken ct) { ... }
```

**Rule:** Repository methods always return materialized types (`List<T>`, `T?`, `PagedResult<T>`). Never return `IQueryable<T>`.

---

### 4. Loop + SaveChanges for bulk operations — O(n) round trips

Calling `SaveChangesAsync` inside a loop sends one SQL statement per iteration.

```csharp
// ❌ wrong — N database round trips
foreach (WorkflowDefinition wf in workflows)
{
    wf.Archive();
    await _context.SaveChangesAsync(ct);
}

// ✅ option A — one SaveChanges after all mutations (same transaction)
foreach (WorkflowDefinition wf in workflows)
    wf.Archive();
await _uow.SaveChangesAsync(ct);

// ✅ option B — bulk update without loading entities (large sets)
await _context.WorkflowDefinitions
    .Where(w => w.workspaceId == WorkspaceId && w.Status == WorkflowStatus.Active)
    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkflowStatus.Archived), ct);
```

**Rule:** Never call `SaveChangesAsync` inside a loop. For large bulk mutations, prefer `ExecuteUpdateAsync` / `ExecuteDeleteAsync`.

---

## EF Core OwnsMany pattern

Use `OwnsMany` (not a standalone `DbSet<T>`) for entities that are part of an aggregate and stored in a separate table.

The example below uses `WorkflowExecution`/`ExecutionStep` as concrete types — replace with your own aggregate root and owned entity. Key points are the backing-field accessor, the explicit FK, and the absence of a standalone `DbSet<T>`.

```csharp
// ExecutionConfiguration.cs — inside Configure(EntityTypeBuilder<WorkflowExecution> builder)

// 1. Tell EF to access the navigation via the backing field
builder.Navigation(e => e.Steps)
    .HasField("_steps")
    .UsePropertyAccessMode(PropertyAccessMode.Field);

// 2. Configure owned entity in its own table
builder.OwnsMany(e => e.Steps, stepBuilder =>
{
    stepBuilder.ToTable("execution_steps");

    // 3. Explicitly use the entity's FK property — prevents EF from generating a shadow WorkflowExecutionId column
    stepBuilder.WithOwner().HasForeignKey(s => s.ExecutionId);

    stepBuilder.HasKey(s => s.Id);
    stepBuilder.Property(s => s.Name).IsRequired().HasMaxLength(500);
    // ... other properties
    stepBuilder.HasIndex(s => new { s.ExecutionId, s.workspaceId });
});
```

**Rules:**
- `WithOwner().HasForeignKey(s => s.ExecutionId)` is mandatory — without it EF generates a shadow `{OwnerType}Id` column that duplicates `ExecutionId`
- `Navigation(...).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field)` is required when the backing field name differs from the property or when the property type is `IReadOnlyList<T>` (not `ICollection<T>`)
- Remove the owned entity's `DbSet<T>` from the DbContext — it must NOT have a standalone DbSet
- Remove any standalone `IEntityTypeConfiguration<ChildEntity>` — owned entity config lives entirely inside the owner's `OwnsMany` block
- Owned entities do NOT need `HasQueryFilter` — deletion is handled by cascade from the owner
- Owned entities do NOT need `DeletedAt` — they are hard-deleted when the owner is soft-deleted or removed
- To load with steps: `context.WorkflowExecutions.Include(e => e.Steps)` — owned entities are NOT loaded by default
- Adding a step goes through the aggregate root: `exec.AddStep(...)`, never `ctx.Steps.Add(new ExecutionStep(...))`

---

## Multi-workspace isolation pitfalls

### Raw SQL bypassing global query filters — workspace data leakage

EF Core global query filters (`HasQueryFilter`) automatically inject `WHERE workspace_id = X` and `WHERE deleted_at IS NULL` into every LINQ query. Raw SQL via `FromSqlRaw`, `ExecuteSqlRawAsync`, or `Dapper` bypasses these filters entirely — the query runs against all workspaces.

```csharp
// ❌ wrong — returns rows from ALL workspaces
List<WorkflowDefinition> all = await _context.WorkflowDefinitions
    .FromSqlRaw("SELECT * FROM workflow_definitions")
    .ToListAsync(ct);

// ❌ wrong — deletes across ALL workspaces
await _context.Database.ExecuteSqlRawAsync(
    "DELETE FROM workflow_definitions WHERE status = 'Archived'", ct);

// ✅ correct — always add workspace filter explicitly for raw SQL
string schema = _workspaceContext.Schema;
await _context.Database.ExecuteSqlRawAsync(
    $"DELETE FROM {schema}.workflow_definitions WHERE status = 'Archived'", ct);

// ✅ preferred — use LINQ so filters apply automatically
await _context.WorkflowDefinitions
    .Where(w => w.Status == WorkflowStatus.Archived)
    .ExecuteDeleteAsync(ct);
```

**Rule:** Avoid raw SQL in workspace-aware contexts. When raw SQL is unavoidable (e.g. performance-critical bulk ops, cross-module reads), always prefix the table with the workspace schema from `IWorkspaceContext.Schema` and add soft-delete filter manually. Document why raw SQL was needed with a comment.

### Workspace schema provisioning ([register-workspace § workspace-provisioning](../use-cases/platform-foundation/register-workspace/README.md#workspace-provisioning))

After email verification, every **workspace-scoped** module provisions its own PostgreSQL schema for the Workspace. Identity stays on `public` and only publishes the verification event — it never touches another module's DB.

- **Ownership**: each workspace-scoped module's Infrastructure project owns an `WorkspaceVerifiedHandler` (e.g. `Axis.DataModeling.Infrastructure.Messaging.WorkspaceVerifiedHandler`) that subscribes to Identity's `WorkspaceVerifiedEvent` Kafka topic. There is **no** central `IWorkspaceSchemaProvisioner` — extraction of a module is a redeploy of its own handler ([ADR-010](../TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)).
- **Schema name**: `workspace_{workspaceId:N}` (no slug — workspace slug can change).
- **Idempotency**: `CREATE SCHEMA IF NOT EXISTS` plus `Database.MigrateAsync()` per context; safe to call twice for the same workspace (Kafka delivers at-least-once).
- **Workspace context during migrate**: each handler constructs a `FixedWorkspaceContext(workspaceId)` from `Axis.Shared.Infrastructure.Workspaces` so `WorkspaceSchemaInterceptor` targets the new schema for the `MigrateAsync` call.
- **Trigger**: `User.VerifyEmail()` raises an `WorkspaceVerified` domain event; `IdentityUnitOfWork` maps it to `WorkspaceVerifiedEvent` (Avro) and publishes via Wolverine outbox → Kafka ([ADR-019](../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). Do **not** provision synchronously in the verify request handler.
- **Schema + topic**: `Axis.Identity.Contracts/Schemas/WorkspaceVerifiedEvent.avsc` + topic `axis.identity.workspace-verified` (see `IdentityKafkaTopics`).

---

## EF Core aggregate mapping patterns

- **Domain relationships are tables, not primitive id arrays**: do not store relationship ids in `PrimitiveCollection<List<Guid>>` or query private fields with `EF.Property(..., "_field")`. Model the relationship explicitly as an entity/join table so invariants, indexes, and future metadata stay visible.
- **Private backing fields for owned value state**: backing fields are fine for aggregate-owned value/child state (for example form fields, workflow steps, role permission strings) when repositories do not query them by magic string. Use `HasField(...)`/field access for encapsulation, but expose relationship queries through named entities and repositories.
- **No-args EF Core constructor**: when an aggregate's only constructor takes params EF Core can't bind (e.g. `IEnumerable<string>`), add a private no-args constructor: `private MyAggregate() : base(default) { RequiredField = null!; }`. Initialize all non-nullable reference-type fields to silence CS8618 — EF Core will never use these sentinel values because it always materialises via the real constructor path.
- **Migrations strategy** ([ADR-023](../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)): every environment uses `Database.MigrateAsync()` — production, dev bootstrap, workspace provisioning, and Testcontainers fixtures. One EF migration chain per `DbContext`; never `EnsureCreated`/`EnsureCreatedAsync`.
- **Identity uses the global `public` schema** — `IdentityDbContext` is a plain `DbContext` with no `WorkspaceSchemaInterceptor`. All other modules use `AxisDbContext` with `WorkspaceSchemaInterceptor`.
- **Workspace schema on every connection** — `WorkspaceSchemaInterceptor` sets `search_path` to `workspace_{WorkspaceId:N}, public` on every `ConnectionOpened` (including pooled reconnects), so a leased connection always targets the current request's workspace ([workspace isolation](../use-cases/platform-foundation/workspace-scope/) — schema-per-workspace).
- **Schema name resolution** — `HttpWorkspaceContext` derives `workspace_{WorkspaceId:N}` from the JWT `workspace_id` claim (no DB/Redis lookup; immutable after provisioning). `WorkspaceAccessMiddleware` on `Axis.Api` returns HTTP 403 when the workspace is missing, archived/deleted, or still provisioning — workspace module routes only; Identity/settings routes are unchanged.
- **Cross-workspace proof** — `tests/Api/Axis.Api.Tests/Workspaces/WorkspaceIsolationEndpointTests.cs` (DataModeling list/get by id).
