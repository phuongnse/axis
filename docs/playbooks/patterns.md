# Technical Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

> **Start with [patterns-index.md](./patterns-index.md)** — one-page map to sections below. Open `patterns.md` only for the section you need. Skip both when the task is trivial.

> Read this file when the task involves any of: adding/updating NuGet packages, EF Core aggregate or JSONB mapping, Minimal API endpoint wiring, writing tests, implementing a list/query endpoint, adding async methods, defining response DTOs, writing repository methods, adding domain methods to an aggregate, working with multi-tenant raw SQL, Wolverine handlers or jobs, implementing a new step or field type, adding a cross-cutting concern, or any design decision about where logic should live. Skip otherwise.

> **Contributing a new entry:** before adding a new section, check whether an existing section can absorb it. Write the *principle* first — the WHY and the class of problems it solves — then show one concrete example. A rule written only at the incident level ("when X happened, do Y") won't help a reader facing a different manifestation of the same problem.

## Contents

- [Patterns index](./patterns-index.md) — task → section map (read this first)
- [Frontend Patterns](#frontend-patterns)
- [Wireframe convention](#wireframe-convention)
- [Key patterns](#key-patterns)
- [Result Pattern vs. exceptions](#result-pattern-vs-exceptions--when-to-use-what)
- [NuGet / packaging rules](#nuget--packaging-rules)
- [EF Core JSONB collection change tracking](#ef-core-jsonb-collection-change-tracking)
- [EF Core common pitfalls](#ef-core-common-pitfalls)
- [DDD / Aggregate design pitfalls](#ddd--aggregate-design-pitfalls)
- [EF Core OwnsMany pattern](#ef-core-ownsmany-pattern)
- [Dependency Injection pitfalls](#dependency-injection-pitfalls)
- [Multi-tenancy pitfalls](#multi-tenancy-pitfalls)
- [Async fire-and-forget pitfalls](#async-fire-and-forget-pitfalls)
- [EF Core aggregate mapping patterns](#ef-core-aggregate-mapping-patterns)
- [Testing rules](#testing-rules)
- [Async patterns](#async-patterns)
- [Query & N+1 patterns](#query--n1-patterns)
- [Response DTO convention](#response-dto-convention)
- [Pagination pattern](#pagination-pattern)
- [Minimal API endpoint wiring](#minimal-api-endpoint-wiring)
- [Axis layering (SRP at a glance)](#axis-layering-srp-at-a-glance)
- [Result → HTTP status code mapping](#result--http-status-code-mapping) ★
- [OpenAPI / Scalar setup](#openapi--scalar-setup) ★
- [Wolverine patterns](#wolverine-patterns) ★
- [Cross-module data pattern](#cross-module-data-pattern) ★
- [Command idempotency pattern](#command-idempotency-pattern) ★
- [Code hygiene checklist](#code-hygiene-checklist)

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

## NuGet / packaging rules

- **Never use `dotnet add package`** — it corrupts `Directory.Packages.props` (CPM project). Always edit `Directory.Packages.props` directly.
- **Search NuGet before assuming a package ID** — NuGet IDs often differ from project names. Run `dotnet package search "<name>"` when unsure of the correct ID.
- **Check transitive dependency versions** after adding any new infrastructure package — run `dotnet build` immediately to catch conflicts introduced by the new dependency.
- **Non-web test projects needing ASP.NET Core types** — use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, never `<PackageReference Include="Microsoft.AspNetCore.Http" />`.

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

### 2. SaveChanges inside a repository — breaks Unit of Work

Calling `SaveChangesAsync` inside a repository method commits a partial transaction. If a second operation in the same handler fails afterward, the first change is already persisted — no rollback possible.

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

**Rule:** Repositories only interact with `DbSet<T>`. `SaveChangesAsync` is always called via `IUnitOfWork` in the handler, never inside a repository.

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
    .Where(w => w.OrganizationId == orgId && w.Status == WorkflowStatus.Active)
    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkflowStatus.Archived), ct);
```

**Rule:** Never call `SaveChangesAsync` inside a loop. For large bulk mutations, prefer `ExecuteUpdateAsync` / `ExecuteDeleteAsync`.

---

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
        RaiseDomainEvent(new ExecutionStepCompleted(Id, stepId, OrganizationId, output)); // event on root
    }
}
```

**Consequences of correct modeling:**
- Domain events are raised by the aggregate root, ensuring they are dispatched within its transaction boundary
- Owned entities do NOT need `DeletedAt` — they are deleted when the root is deleted (EF Cascade)
- EF Core: use `OwnsMany` with a separate table (see [OwnsMany pattern](#ef-core-ownsmany-pattern)) instead of a standalone `DbSet<ChildEntity>`
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
    stepBuilder.HasIndex(s => new { s.ExecutionId, s.OrganizationId });
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

## Dependency Injection pitfalls

### Captive dependency — scoped service inside a singleton

A singleton that captures a scoped service holds it for the application lifetime. The scoped service (e.g. `DbContext`, `ITenantContext`) was designed to be created per-request — holding it in a singleton causes tenant context bleed across requests and DbContext reuse across threads.

```csharp
// ❌ wrong — ITenantContext is scoped; singleton captures it at startup
public class MyCache(ITenantContext tenantContext) // singleton captures scoped
{
    public string GetKey() => $"cache:{tenantContext.Schema}"; // wrong tenant after first request
}

// ✅ correct — inject IServiceScopeFactory and resolve per-operation
public class MyCache(IServiceScopeFactory scopeFactory)
{
    public async Task<string> GetKeyAsync()
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        return $"cache:{tenant.Schema}";
    }
}
```

**Rule:** Singletons must never depend on Scoped services directly. If a singleton needs scoped data, inject `IServiceScopeFactory` and resolve the scoped dependency per-operation. Check all singleton registrations in `Program.cs` — EF Core will warn about this at startup if `ValidateScopes` is enabled (it is in Development by default).

### Eager configuration capture at registration time

DI registrations run at startup. Any value captured at that point is frozen — overrides applied later (e.g., `WebApplicationFactory.ConfigureAppConfiguration` in tests) never take effect. Read configuration lazily inside the lambda, at resolution time.

```csharp
// ❌ wrong — connection string frozen at startup; test container overrides are ignored
public static IServiceCollection AddWorkflowBuilderInfrastructure(
    this IServiceCollection services, string connectionString)
{
    services.AddDbContext<WorkflowBuilderDbContext>(opts =>
        opts.UseNpgsql(connectionString));
}

// ✅ correct — IConfiguration read inside the lambda, at DbContext resolution time.
// Null guard ensures a missing connection string fails fast at startup, not on first request.
public static IServiceCollection AddWorkflowBuilderInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<WorkflowBuilderDbContext>(opts =>
        opts.UseNpgsql(configuration.GetConnectionString("WorkflowBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowBuilder'.")));
}
```

**Rule:** pass `IConfiguration` to every module infrastructure extension; read connection strings inside lambdas, never outside them. The null guard is not optional — it converts a cryptic NullReferenceException at first request into a clear startup failure.

---

## Multi-tenancy pitfalls

### Raw SQL bypassing global query filters — tenant data leakage

EF Core global query filters (`HasQueryFilter`) automatically inject `WHERE tenant_id = X` and `WHERE deleted_at IS NULL` into every LINQ query. Raw SQL via `FromSqlRaw`, `ExecuteSqlRawAsync`, or `Dapper` bypasses these filters entirely — the query runs against all tenants.

```csharp
// ❌ wrong — returns rows from ALL tenants
List<WorkflowDefinition> all = await _context.WorkflowDefinitions
    .FromSqlRaw("SELECT * FROM workflow_definitions")
    .ToListAsync(ct);

// ❌ wrong — deletes across ALL tenants
await _context.Database.ExecuteSqlRawAsync(
    "DELETE FROM workflow_definitions WHERE status = 'Archived'", ct);

// ✅ correct — always add tenant filter explicitly for raw SQL
string schema = _tenantContext.Schema;
await _context.Database.ExecuteSqlRawAsync(
    $"DELETE FROM {schema}.workflow_definitions WHERE status = 'Archived'", ct);

// ✅ preferred — use LINQ so filters apply automatically
await _context.WorkflowDefinitions
    .Where(w => w.Status == WorkflowStatus.Archived)
    .ExecuteDeleteAsync(ct);
```

**Rule:** Avoid raw SQL in tenant-aware contexts. When raw SQL is unavoidable (e.g. performance-critical bulk ops, cross-module reads), always prefix the table with the tenant schema from `ITenantContext.Schema` and add soft-delete filter manually. Document why raw SQL was needed with a comment.

### Tenant schema provisioning (US-003)

After email verification, the host provisions one PostgreSQL schema per organization and migrates every **tenant-scoped** module database into it. Identity stays on `public`.

- **Interface**: `ITenantSchemaProvisioner` in `Axis.Shared.Application` — implement in the **API host** when the provisioner must touch multiple module `DbContext` types.
- **Schema name**: `tenant_{organizationId:N}` (no slug — org slug can change).
- **Idempotency**: `CREATE SCHEMA IF NOT EXISTS` plus `Database.MigrateAsync()` per context; safe to call twice for the same org.
- **Tenant context during migrate**: use `FixedTenantContext` (or equivalent) so `TenantSchemaInterceptor` targets the new schema for each `MigrateAsync` call.
- **Tests**: register `NoOpTenantSchemaProvisioner` in `WebApplicationFactory` fixtures — never run real provisioning in API integration tests.
- **Trigger**: `VerifyEmailHandler` persists `User.VerifyEmail()` via `SaveChangesAsync`, then enqueues `ProvisionTenantMessage` through `ITenantProvisioningScheduler` (Wolverine). Do **not** call `ITenantSchemaProvisioner` synchronously in the verify request — provisioning runs in `ProvisionTenantHandler` in the API host.
- **Message**: `ProvisionTenantMessage(Guid OrganizationId)` in `Axis.Shared.Application.Tenancy`.

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

## EF Core aggregate mapping patterns

- **Private backing fields** (`_roleIds`, `_permissions`): use `PrimitiveCollection<List<T>>(fieldName).HasField(fieldName).UsePropertyAccessMode(PropertyAccessMode.Field)` — the type parameter must be the *collection* type, not the element type.
- **No-args EF Core constructor**: when an aggregate's only constructor takes params EF Core can't bind (e.g. `IEnumerable<string>`), add a private no-args constructor: `private MyAggregate() : base(default) { RequiredField = null!; }`. Initialize all non-nullable reference-type fields to silence CS8618 — EF Core will never use these sentinel values because it always materialises via the real constructor path.
- **Migrations strategy**: Infrastructure tests use `context.Database.EnsureCreated()` (fast, no migration files). Production deployments need one EF Core migration bundle per `DbContext`.
- **Identity uses the global `public` schema** — `IdentityDbContext` is a plain `DbContext` with no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.

## Testing rules

- Never run `dotnet test --no-build` after editing test code — always let it recompile.
- **Never hardcode environment configurations**: connection strings, API URLs, Docker endpoints, secret keys must use environment variables, `appsettings.json`, or `.testcontainers.properties`.
- **Pre-commit / CI**: `dotnet build` then `dotnet test` on the full solution (`Axis.sln`). Includes Testcontainers integration and API tests — Docker required locally.

**Test isolation pattern** — two levels of isolation to understand:

**Level 1 — between test classes (container-per-class):** Each test class gets its own Testcontainers instance via `IAsyncLifetime`. This guarantees no cross-class pollution.

**Level 2 — between tests within the same class:** A fresh container starts empty, so the first test is clean by default. But subsequent tests in the same class accumulate data from previous ones. Handle this with a `ResetAsync()` helper that truncates relevant tables at the start of each test:

```csharp
public class CreateWorkflowTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private WorkflowBuilderDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().Build();
        await _postgres.StartAsync();
        _context = DbContextFactory.Create(_postgres.GetConnectionString());
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // Call at the start of each test method that needs a clean slate
    private async Task ResetAsync()
    {
        await _context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE workflow_definitions CASCADE");
    }

    [Fact]
    public async Task CreateWorkflow_WhenNameIsUnique_Succeeds()
    {
        await ResetAsync();
        // ... arrange, act, assert
    }
}
```

**Rules:**
- Never assume a test runs first — always call `ResetAsync()` at the start of any test that requires a clean state.
- Never rely on data created by a sibling test — each test must arrange its own prerequisites.
- Use `AsNoTracking()` for all read queries in tests to avoid EF Core change tracker interference between assertions.

## Async patterns

- **Never sync-over-async**: `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` on a `Task` inside an async call stack causes thread-pool deadlock under ASP.NET Core. Always `await`.
- **Always propagate `CancellationToken`**: every `async` method signature must accept `CancellationToken cancellationToken` and pass it to every downstream call (EF Core, HttpClient, Redis). Use `CancellationToken.None` only at the outermost entry point (e.g. a Wolverine background job handler where the runtime owns the token).

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

## Query & N+1 patterns

Lazy loading is **disabled** globally. Rules:

1. **Always `Include` explicitly** — if a navigation property is needed in a handler, declare the `Include` in the repository method, not in the handler.
2. **List queries project to DTOs** — never load a full aggregate collection and map in memory.
3. **Never navigate inside a loop** — accessing `execution.Steps[i].Config` in a `foreach` without a prior `Include` is a silent N+1.

```csharp
// ✅ correct — projection at the DB level
public async Task<PagedResult<WorkflowSummaryDto>> GetPagedAsync(
    int page, int pageSize, CancellationToken ct)
{
    IQueryable<WorkflowDefinition> query = _context.WorkflowDefinitions
        .AsNoTracking()
        .Where(w => w.DeletedAt == null);

    int total = await query.CountAsync(ct);
    List<WorkflowSummaryDto> items = await query
        .OrderByDescending(w => w.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(w => new WorkflowSummaryDto(w.Id, w.Name, w.Status, w.CreatedAt))
        .ToListAsync(ct);

    return new PagedResult<WorkflowSummaryDto>(items, total, page, pageSize);
}

// ❌ wrong — loads all columns, maps in memory, potential N+1 if steps accessed
List<WorkflowDefinition> all = await _context.WorkflowDefinitions.ToListAsync(ct);
return all.Select(w => new WorkflowSummaryDto(...)).ToList();
```

## Response DTO convention

- Response types are `record` types defined in `{Module}.Application/Queries/{QueryName}/`.
- Naming: `{Subject}Dto` for embedded objects, `{Subject}Response` for top-level query results.
- Never return a domain entity or EF Core–tracked object from a query handler.
- For commands that need to return the created resource ID, return `Result<Guid>` — not the full aggregate.

```csharp
// Application/Queries/GetWorkflow/WorkflowResponse.cs
public record WorkflowResponse(
    Guid Id,
    string Name,
    WorkflowStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StepDto> Steps);

public record StepDto(Guid Id, string Name, StepType Type);
```

## Pagination pattern

`PagedResult<T>` is defined in `Axis.Shared.Application`:

```csharp
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
```

Endpoint wiring — always clamp `pageSize` to 100:

```csharp
app.MapGet("/api/workflows", async (
    int page = 1,
    int pageSize = 20,
    IMediator mediator,
    CancellationToken ct) =>
{
    pageSize = Math.Min(pageSize, 100);
    Result<PagedResult<WorkflowSummaryDto>> result =
        await mediator.Send(new GetWorkflowsQuery(page, pageSize), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : result.ToProblemDetails();
})
.WithName("GetWorkflows")
.WithSummary("List workflow definitions for the current tenant")
.WithTags("WorkflowBuilder")
.Produces<PagedResult<WorkflowSummaryDto>>()
.ProducesProblem(StatusCodes.Status401Unauthorized)
.RequireAuthorization();
```

## Minimal API endpoint wiring

- Each module exposes a `Map{ModuleName}Endpoints(IEndpointRouteBuilder)` extension method.
- No logic in the mapping file — only `mediator.Send(...)` dispatch and minimal request mapping. Do not parse `HttpContext` claims, build default command payloads, or map enums in the endpoint — that belongs in Application (PR #47 deferred: `FormTaskEndpoints.SubmitFormByToken`, `ExecutionEndpoints.StartExecution`).
- Use `MapGroup` to apply route prefixes and auth policies at group level.
- JSON configuration via `ConfigureHttpJsonOptions`, never via `AddControllers().AddJsonOptions(...)`.
- **Required annotations on every endpoint**: `.WithName()`, `.WithSummary()`, `.WithTags()`, `.Produces<T>()`, `.ProducesProblem()` for each applicable status code (400, 401, 403, 404).

## Axis layering (SRP at a glance)

Handlers orchestrate one command or query; aggregates own invariants and raise domain events; repositories persist only. Do not publish domain events from handlers, send email from handlers, or mutate aggregate state without calling aggregate methods.

Long-form SOLID and Gang-of-Four catalogs are intentionally omitted here — they duplicate generic material and are not Axis-specific. Use [patterns-index.md](./patterns-index.md) to jump to the section you need; add new **principle + one example** entries to `patterns.md` only when the pattern is project-specific.

| Task | Section in patterns.md |
|------|------------------------|
| Business failures vs exceptions | [Result Pattern vs. exceptions](#result-pattern-vs-exceptions--when-to-use-what) |
| HTTP status from `Result` | [Result → HTTP status code mapping](#result--http-status-code-mapping) |
| Domain events / jobs | [Wolverine patterns](#wolverine-patterns) |
| Another module's data | [Cross-module data pattern](#cross-module-data-pattern) |
| Handler / repository layout | [Key patterns](#key-patterns) |

---

## OpenAPI annotation reference

```csharp
group.MapPost("/", async (...) => { ... })
    .WithName("CreateWorkflow")
    .WithSummary("Create a new workflow definition")
    .WithTags("WorkflowBuilder")
    .Produces<WorkflowResponse>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization();
```

---

## Result → HTTP status code mapping

All `Result` failures from Command/Query handlers map to `ProblemDetails` (RFC 7807). Use this table consistently across all modules:

| Failure reason | HTTP status | Typical error code |
|---|---|---|
| Entity not found | 404 Not Found | `"not_found"` |
| Duplicate / unique constraint | 409 Conflict | `"conflict"` |
| Business rule violation | 422 Unprocessable Entity | `"business_rule"` |
| Plan / subscription limit | 402 Payment Required | `"plan_limit"` |
| Input validation (FluentValidation) | 400 Bad Request | Handled automatically by `ValidationBehavior` → middleware |
| Unauthenticated | 401 Unauthorized | Handled by JWT middleware |
| RBAC denied | 403 Forbidden | Handled by `PermissionAuthorizationHandler` |

**Endpoint pattern — always use `result.ToProblemDetails()`:**

```csharp
private static async Task<IResult> CreateWorkflow(
    [FromBody] CreateWorkflowRequest request,
    CurrentUser currentUser,
    ISender mediator,
    CancellationToken ct)
{
    Result<Guid> result = await mediator.Send(
        new CreateWorkflowCommand(request.Name, request.Description, currentUser.OrgId, currentUser.UserId.ToString()), ct);

    return result.Match(
        id  => Results.Created($"/api/workflows/{id}", new { id }),
        err => err.ToProblemDetails());
}
```

`ToProblemDetails()` is a shared extension in `Axis.Shared.Application` that maps a well-known error code to the correct HTTP status:

```csharp
// Axis.Shared.Application/Extensions/ResultExtensions.cs
public static IResult ToProblemDetails(this Error error) => error.Code switch
{
    "not_found"     => Results.Problem(error.Message, statusCode: 404),
    "conflict"      => Results.Problem(error.Message, statusCode: 409),
    "plan_limit"    => Results.Problem(error.Message, statusCode: 402),
    _               => Results.Problem(error.Message, statusCode: 422),
};
```

**Rule:** Never hardcode a status code in an endpoint handler. Always call `result.ToProblemDetails()`. Never return custom JSON error shapes.

---

## OpenAPI / Scalar setup

Packages already in `Directory.Packages.props`:

```xml
<PackageVersion Include="Swashbuckle.AspNetCore" Version="6.9.0" />
<PackageVersion Include="Scalar.AspNetCore" Version="2.6.0" />
```

Wire up in `Program.cs`:

```csharp
// Registration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new OpenApiInfo { Title = "Axis API", Version = "v1" });
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    opts.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            []
        },
    });
});

// After app.Build():
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.MapScalarApiReference(options =>
    {
        options.WithOpenApiRoutePattern("/swagger/v1/swagger.json");
        options.Title = "Axis API";
        options.Theme = ScalarTheme.Moon;
    });
}
```

Every endpoint must be fully annotated — see CLAUDE.md API Layer section for required metadata.

---

## Wolverine patterns

### Host setup

```csharp
// Program.cs
builder.Host.UseWolverine(opts =>
{
    // Auto-wrap handlers that use EF Core DbContext in a transaction
    opts.Policies.AutoApplyTransactions();

    // Integrates Wolverine's outbox with EF Core SaveChangesAsync
    opts.UseEntityFrameworkCoreTransactions();

    // NOTE: Durable PostgreSQL outbox is NOT yet configured. Domain events are dispatched
    // in-memory after SaveChangesAsync. The durable outbox is deferred until the
    // Wolverine persistence schema strategy is decided — tracked as E01 gap.
});
```

### Intra-module domain event handler

Domain events raised by aggregates (`AddDomainEvent`) are dispatched by `UnitOfWork.SaveChangesAsync` via Wolverine after the transaction commits (in-memory today; durable when outbox is configured — see section above). Define the handler in the Application layer:

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

## Cross-module data pattern

**Core rule: a module only queries its own tables. Always. No exceptions.**

If module A needs data owned by module B, A must maintain its own local copy of that data, kept in sync via Wolverine domain events. This is the Share Nothing principle — modules are data-sovereign.

### ❌ Anti-pattern: cross-module raw SQL

```csharp
// WRONG — module A querying a table owned by module B
int count = await context.Database
    .SqlQueryRaw<int>("SELECT COUNT(*) FROM workflow_definitions WHERE steps @> {0}::jsonb", ...)
    .FirstAsync(ct);
```

This silently breaks module isolation, causes test failures when modules use separate databases, and creates hidden coupling that makes the system fragile.

### ✅ Correct pattern: event-driven local denormalization

When module A needs to know something about module B's data, B publishes domain events when that data changes. A listens via Wolverine and maintains its own local copy.

**Example** (FormBuilder / WorkflowBuilder — the principle applies to any two modules): FormBuilder needs to know whether a form is referenced by any workflow step.

**Step 1 — Source module (WorkflowBuilder) raises events when its state changes:**

```csharp
// WorkflowBuilder.Domain/Events/FormStepAdded.cs
public sealed record FormStepAdded(Guid WorkflowId, Guid StepId, Guid FormId) : IDomainEvent;

// WorkflowBuilder.Domain/Events/FormStepRemoved.cs
public sealed record FormStepRemoved(Guid WorkflowId, Guid StepId, Guid FormId) : IDomainEvent;

// Raised inside WorkflowDefinition.AddStep() when StepType == Form
RaiseDomainEvent(new FormStepAdded(Id, step.Id, formId));
```

**Step 2 — Consuming module (FormBuilder) stores its own local copy:**

```csharp
// FormBuilder.Domain/Entities/FormWorkflowReference.cs
// Simple record — no aggregate, no lifecycle; owned by FormBuilder's DB
public sealed class FormWorkflowReference
{
    public Guid FormId { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid StepId { get; init; }
}

// FormBuilder.Infrastructure/Handlers/FormStepAddedHandler.cs
public sealed class FormStepAddedHandler(FormBuilderDbContext db)
{
    public async Task Handle(FormStepAdded evt, CancellationToken ct)
    {
        // Idempotent: skip if already synced (Wolverine at-least-once delivery)
        bool exists = await db.FormWorkflowReferences.AnyAsync(
            r => r.FormId == evt.FormId && r.WorkflowId == evt.WorkflowId && r.StepId == evt.StepId, ct);
        if (exists) return;

        db.FormWorkflowReferences.Add(new FormWorkflowReference
        {
            FormId = evt.FormId, WorkflowId = evt.WorkflowId, StepId = evt.StepId
        });
        await db.SaveChangesAsync(ct);
    }
}
```

**Step 3 — Consuming module queries only its own table:**

```csharp
// FormRepository.IsReferencedByWorkflowAsync — queries FormBuilder's own DB
public async Task<bool> IsReferencedByWorkflowAsync(Guid formId, CancellationToken ct = default)
    => await context.FormWorkflowReferences.AnyAsync(r => r.FormId == formId, ct);
```

### Rules

- **Never** use `SqlQueryRaw`, `ExecuteSqlRaw`, `FromSqlRaw`, or any raw SQL that references a table from another module.
- **Never** inject another module's `DbContext` into your repositories or handlers.
- The source module (B) owns the event — define it in B's Domain layer.
- The consuming module (A) owns the handler and the local copy table — both in A's Infrastructure layer.
- The Wolverine handler in A is idempotent: use upsert or check-before-insert to handle duplicate events.
- **Cross-module event handlers must filter by `OrganizationId`** in addition to the entity foreign key. Filtering only by the entity ID leaves tenant isolation implicit — always make it explicit by including both keys in every `Where` clause.
- If the consuming module needs derived state computed (e.g., "is the form referenced at all?"), compute it from the local copy at query time — do not try to sync aggregated state.

### Pre-commit violation sweep

Run this before every commit that touches Infrastructure code:

```powershell
grep -rn "SqlQueryRaw\|ExecuteSqlRaw\|FromSqlRaw\|ExecuteSqlInterpolated" src/Modules/ --include="*.cs"
```

For every match: confirm the SQL only references tables owned by that match's own module. If it references another module's table → P0 violation, must fix before committing.

---

## Command idempotency pattern

All Command handlers must be safe to retry without producing duplicate side effects.

**Pattern 1 — check-before-create (most commands):**

```csharp
public async Task<Result<Guid>> Handle(CreateWorkflowCommand cmd, CancellationToken ct)
{
    bool exists = await _repo.ExistsByNameAsync(cmd.Name, cmd.OrganizationId, ct);
    if (exists)
        return Result.Failure<Guid>(Error.Conflict($"A workflow named '{cmd.Name}' already exists."));

    WorkflowDefinition wf = WorkflowDefinition.Create(cmd.Name, cmd.OrganizationId, cmd.CreatedByUserId);
    await _repo.AddAsync(wf, ct);
    await _uow.SaveChangesAsync(ct);
    return Result.Success(wf.Id);
}
```

**Pattern 2 — caller-supplied idempotency key (for external triggers):**

Commands triggered by external systems (webhooks, scheduled jobs) accept a caller-supplied `IdempotencyKey` (UUID). The handler checks if a record with that key already exists and returns the existing result without re-executing.

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
            WorkflowActiveStatus.Activated(@event.WorkflowId, @event.OrganizationId));
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

## Code hygiene checklist

Run this checklist before every commit. Items are ordered from most to least likely to be missed.

### 1. No inline fully-qualified type names

CLAUDE.md rule: **always use `using` directives — never write the namespace inline**.

**Wrong:**
```csharp
string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
    System.Text.Encoding.UTF8.GetBytes(token)));

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { … });

opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
```

**Right:**
```csharp
using System.Security.Cryptography;
using System.Text;
// …
string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
```

**Detection command** — run this before committing; output must be empty:
```bash
grep -rn --include="*.cs" \
  -E "(System\.(Collections\.Generic|Linq|Threading|IO|Text|Net|Security)\.|Microsoft\.(AspNetCore|Extensions|EntityFrameworkCore)\.[A-Z])" \
  src/ tests/ \
  | grep -v "obj/" \
  | grep -v "\.cs:.*using " \
  | grep -v "^\s*//"
```

Common namespaces that agents forget to add as `using` directives:

| Inline form seen in code | `using` to add |
|---|---|
| `System.Text.Encoding.UTF8` | `using System.Text;` |
| `System.Text.RegularExpressions.Regex` | `using System.Text.RegularExpressions;` |
| `System.Text.Json.JsonNamingPolicy` | `using System.Text.Json;` |
| `System.Text.Json.Serialization.JsonStringEnumConverter` | `using System.Text.Json.Serialization;` |
| `System.Security.Cryptography.SHA256` / `RandomNumberGenerator` | `using System.Security.Cryptography;` |
| `System.Net.HttpStatusCode` | `using System.Net;` |
| `Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions` | `using Microsoft.AspNetCore.Diagnostics.HealthChecks;` |

### 2. No restructuring to avoid a `using` directive

When replacing `var` with an explicit type, if that type requires a new `using` directive — add the directive. Never restructure or inline code just to avoid importing a type. That is a workaround, not a fix.

**Wrong** — chains `.Id` onto the query to avoid declaring an explicit type:
```csharp
Guid itemId = ctx.Items.First(i => i.Name == "target" && i.OrgId == orgId).Id;
// … then uses itemId directly — the full object is discarded
```

**Right** — declares the explicit type and adds the `using` directive:
```csharp
using My.Module.Domain.Aggregates;
// …
MyEntity item = ctx.Items.First(i => i.Name == "target" && i.OrgId == orgId);
// … then uses item.Id, item.Name, etc. as needed
```

The restructured version hides what type is being worked with, discards future flexibility (e.g. if a second property is later needed), and violates the intent of "no `var`" — which is to make types explicit, not to obscure them by different means.

### 3. Verify `!` is actually needed before adding it

Before using the null-forgiving operator `!` to suppress a nullable annotation, check whether the assignment compiles without it. If the existing codebase already uses the same API without `!`, adding it is a workaround — not a fix.

**Wrong** — `!` added without verifying it is necessary:
```csharp
MyType result = (await SomeApiAsync())!;
```

**Right** — if the return type is non-nullable (e.g. a value type or a `Task<T>` where T is a struct), `!` is unnecessary:
```csharp
MyType result = await SomeApiAsync();
```

The rule: grep the codebase for existing call sites of the same API before reaching for `!`. If they compile without it, yours should too. Never add `!` just because the compiler warns — resolve the underlying nullability issue instead.

### 4. No scaffold placeholder files

Visual Studio scaffolds `Class1.cs` when creating a new project. These files must be deleted immediately — never committed. A `Class1.cs` anywhere in `src/` or `tests/` is always wrong.

**Detection command:**
```bash
find src/ tests/ -name "Class1.cs" -not -path "*/obj/*"
```

### 5. User input flowing into external identifiers

Any string derived from user input that becomes an external identifier — filename, ZIP entry name, URL slug, S3 key, Redis key — needs two checks:

1. **Character safety**: use an allowlist, never a denylist or single-char replace. For filenames and ZIP entries the safe set is `[a-z0-9\-_]` after lowercasing and replacing spaces with `-`.
2. **Uniqueness**: when the identifier is used in a set (ZIP archive, directory), handle the collision case — two workflows with the same name produce the same slug and will clash.

```csharp
// ❌ handles spaces only — "/" ":" "?" survive and corrupt filenames on Windows
string slug = name.ToLowerInvariant().Replace(' ', '-');

// ✅ allowlist — only safe chars survive; handle collisions at the call site
private static string ToSafeSlug(string name)
{
    string slug = name.ToLowerInvariant().Replace(' ', '-');
    slug = new string(slug.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_').ToArray());
    return slug.Trim('-', '_');
}
```

Collision handling in a bulk operation:
```csharp
Dictionary<string, int> seen = new(StringComparer.Ordinal);
foreach (WorkflowExportDto dto in workflows)
{
    string baseSlug = ToSafeSlug(dto.Name);
    seen.TryGetValue(baseSlug, out int count);
    string entrySlug = count == 0 ? baseSlug : $"{baseSlug}_{count + 1}";
    seen[baseSlug] = count + 1;
    // use entrySlug as the ZIP entry name
}
```

### 4. No direct commits to `main`

Every change — including one-line fixes — goes through a branch + PR. Steps:

```bash
git checkout -b chore/my-fix   # branch off current HEAD
# make changes
git add <files>
git commit -m "chore: ..."
git push -u origin chore/my-fix
gh pr create …
```


## Frontend Patterns

### Feature folder anatomy

Every feature lives under `frontend/src/features/{feature-name}/`:

```text
features/workflows/
├── components/        # React components owned by this feature
│   ├── WorkflowList.tsx
│   └── WorkflowCard.tsx
├── hooks/             # Custom hooks — mandatory use prefix
│   └── useWorkflows.ts
├── api.ts             # All queryFn / mutationFn for this feature
├── types.ts           # Shared types for this feature
└── index.ts           # Barrel export — public API of the feature
```

- Component files: `PascalCase.tsx`. Hook files: `camelCase.ts` with mandatory `use` prefix.
- Cross-feature imports must go through `index.ts`, never directly into `components/` or `hooks/`.
- Shared UI primitives: `frontend/src/components/ui/`. Utilities: `frontend/src/lib/`.

### TanStack Query patterns

**Query key factory** — one per feature, avoids magic strings:

```ts
// features/workflows/api.ts
export const workflowKeys = {
  all: ['workflows'] as const,
  list: (filters: WorkflowFilters) => [...workflowKeys.all, 'list', filters] as const,
  detail: (id: string) => [...workflowKeys.all, 'detail', id] as const,
};
```

**All `queryFn` / `mutationFn` live in `api.ts`** — never inline in a component:

```ts
// features/workflows/api.ts
export async function fetchWorkflows(filters: WorkflowFilters): Promise<PagedResult<WorkflowDto>> {
  return fetchApi(`/api/workflows?page=${filters.page}&pageSize=${filters.pageSize}`);
}

// features/workflows/hooks/useWorkflows.ts
export function useWorkflows(filters: WorkflowFilters) {
  return useQuery({
    queryKey: workflowKeys.list(filters),
    queryFn: () => fetchWorkflows(filters),
  });
}
```

**Components call custom hooks only** — never call `useQuery` directly with a `queryFn` inside a component file.

### TypeScript discipline

- **No `as any`** — use `as unknown as T` only when a double-assertion is genuinely necessary, with a comment explaining why.
- **Entity IDs are `string`** — backend serialises `Guid` as string. Never type an ID field as `number`.
- **`unknown` at API boundaries** — catch blocks and raw response data use `unknown`, then narrow with `instanceof` / type guards:

```ts
// ✅
} catch (error: unknown) {
  if (error instanceof ApiError) { ... }
}

// ❌
} catch (error: any) {
  if (error.status === 401) { ... }
}
```

- **Mock objects in tests** use `as unknown as Response`, not `as any`:

```ts
vi.mocked(fetch).mockResolvedValueOnce({
  ok: true,
  status: 200,
  text: () => Promise.resolve('{"id":"abc"}'),
} as unknown as Response);
```

### Routing patterns

- All routes beyond the root are **lazy-loaded** — use TanStack Router's `lazy()`:

```ts
// routes/workflows/index.lazy.tsx — filename convention triggers lazy loading
export const Route = createLazyFileRoute('/workflows/')({ component: WorkflowsPage });
```

- **Auth guard** lives in a root layout route `beforeLoad`, not inside individual pages:

```ts
// routes/__authenticated.tsx
export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ({ context }) => {
    if (!context.auth.isAuthenticated) throw redirect({ to: '/login' });
  },
});
```

- TanStack Router auto-generates `routeTree.gen.ts` — never edit it manually, always exclude from linting.

### Frontend testing patterns

**Test behaviour, not implementation:**

```ts
// ✅ Tests what the user sees
it('should show error message when submission fails', async () => {
  render(<CreateWorkflowForm />);
  await userEvent.click(screen.getByRole('button', { name: /create/i }));
  expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
});

// ❌ Tests implementation detail
it('should set isSubmitting to true', () => { ... });
```

**Use `userEvent` over `fireEvent`** — simulates real browser event sequences:

```ts
import userEvent from '@testing-library/user-event';
const user = userEvent.setup();
await user.type(screen.getByLabelText('Name'), 'My Workflow');
await user.click(screen.getByRole('button', { name: /save/i }));
```

**Thrown errors in tests** — use `unknown` + `instanceof`, not `any`:

```ts
let thrownError: unknown;
try { await fetchApi('/fail'); } catch (e) { thrownError = e; }

expect(thrownError).toBeInstanceOf(ApiError);
if (thrownError instanceof ApiError) {
  expect(thrownError.status).toBe(400);
}
```

### Build gate

Before every push involving `frontend/` changes:

```bash
npm run ci      # tsc -b --noEmit && biome ci . — must be zero errors/warnings
npm run test    # vitest run — all tests must pass
```

`npm run lint:fix` auto-fixes safe Biome issues. `npm run format` reformats all files.

### Wireframe convention

Wireframes use Excalidraw (`.excalidraw` JSON). Both files — source and SVG preview — are committed.

**File location:**
```
docs/wireframes/
├── E02-identity-access/
│   ├── login.excalidraw        ← source (JSON, diffable on GitHub)
│   └── login.svg               ← rendered preview (vector, renders inline on GitHub)
├── E03-data-modeling/
│   └── ...
└── _shared/                    ← screens not belonging to a single module
```

One subfolder per epic, mirroring `docs/epics/`. Shared screens (error pages, global settings) go in `_shared/`.

**Naming:** screen slug in kebab-case matching the primary route segment — `login`, `data-models`, `workflow-detail`.

**One wireframe per screen.** Multiple user stories on the same screen share one wireframe file.

**Linking from a feature file** — add directly after the feature title, before the first user story:

```markdown
> **Wireframe**: [docs/epics/E02-identity-access/wireframes/login.excalidraw](../../epics/E02-identity-access/wireframes/login.excalidraw) · [preview](../../epics/E02-identity-access/wireframes/login.svg)
```

**Excalidraw settings** for consistent sketch aesthetic:
- `roughness: 1` on all shapes
- `fillStyle: "solid"` for filled shapes
- `fontFamily: 1` (Virgil — hand-drawn feel)
- `strokeWidth: 2` for card/container outlines, `1` for inputs

**Generate SVG** after every edit: run `docs/scripts/generate-wireframes.ps1` — regenerates all `.svg` files from `.excalidraw` source via Kroki.io. Commit both `.excalidraw` and `.svg` together.

**Deterministic regeneration:** `generate-screens.mjs` must call each screen through `runScreen(screenKey, generator)` so Excalidraw `seed` values do not depend on global generation order. See [`wireframes.md` § Deterministic seed rule](wireframes.md#deterministic-seed-rule-non-negotiable). Before committing generator changes, run the script twice and confirm `git diff` is empty after the second run.

**Pitfall:** committing only `.excalidraw` without `.svg` means the wireframe is invisible on GitHub without the VS Code extension. Always run the script and commit both.

**Pitfall:** editing `generate-screens.mjs` without per-screen seeds causes dozens of unrelated epic wireframes to change in the same PR — always verify the second-run diff is empty.
