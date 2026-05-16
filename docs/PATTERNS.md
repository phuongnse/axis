# Technical Patterns

> Read this file when the task involves any of: adding/updating NuGet packages, EF Core aggregate or JSONB mapping, Minimal API endpoint wiring, writing tests, implementing a list/query endpoint, adding async methods, defining response DTOs, writing repository methods, adding domain methods to an aggregate, working with multi-tenant raw SQL, Wolverine handlers or jobs, implementing a new step or field type, adding a cross-cutting concern, or any design decision about where logic should live. Skip otherwise.

## Contents

- [Frontend Patterns](#frontend-patterns)
- [Wireframe convention](#wireframe-convention)
- [Key patterns](#key-patterns)
- [Result Pattern vs. exceptions](#result-pattern-vs-exceptions----when-to-use-what)
- [NuGet / packaging rules](#nuget--packaging-rules)
- [EF Core JSONB collection change tracking](#ef-core-jsonb-collection-change-tracking)
- [EF Core common pitfalls](#ef-core-common-pitfalls)
- [DDD / Aggregate design pitfalls](#ddd--aggregate-design-pitfalls)
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
- [Result → HTTP status code mapping](#result--http-status-code-mapping) ★
- [OpenAPI / Scalar setup](#openapi--scalar-setup) ★
- [Wolverine patterns](#wolverine-patterns) ★
- [Cross-module read pattern](#cross-module-read-pattern) ★
- [Command idempotency pattern](#command-idempotency-pattern) ★
- [Clean Code Principles](#clean-code-principles)
- [Design Patterns in Practice](#design-patterns-in-practice)

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
- **Search NuGet before assuming a package ID** — NuGet IDs often differ from project names (e.g. `WolverineFx` not `Wolverine`). Run `dotnet package search "<name>"` when unsure.
- **Check transitive dependency versions** after adding any new infrastructure package — run `dotnet build` immediately to catch conflicts (e.g. WolverineFx 5.x requires EF Core 9.x).
- **`UseInMemoryDatabase` requires `Microsoft.EntityFrameworkCore.InMemory`** — separate package, must be added explicitly to test projects.
- **Non-web test projects needing ASP.NET Core types** — use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, never `<PackageReference Include="Microsoft.AspNetCore.Http" />`.

## EF Core JSONB collection change tracking

**Problem:** EF Core uses reference equality to detect changes on `List<T>` properties backed by a `ValueConverter`. When you call `list.Add()` or `list.Remove()` in-place, the list reference stays the same → EF Core sees no change → the mutation is silently not persisted.

**Wrong fix (what not to do):** Overriding `SaveChangesAsync` in the DbContext to forcibly mark the property as modified:
```csharp
// ❌ incomplete workaround — only fires when entity is already Modified.
// If ONLY the JSONB field mutated, entity state is Unchanged → changes are LOST silently.
foreach (var entry in ChangeTracker.Entries<DataModel>()
    .Where(e => e.State == EntityState.Modified))
{
    entry.Property("_fields").IsModified = true;
}
```

**Correct fix: always pair `HasConversion` with `HasValueComparer`**

```csharp
internal sealed class DataModelConfiguration : IEntityTypeConfiguration<DataModel>
{
    private static readonly ValueConverter<List<FieldDefinition>, string> FieldsConverter =
        new(
            fields => JsonSerializer.Serialize(fields, FieldJsonOptions.Options),
            json => JsonSerializer.Deserialize<List<FieldDefinition>>(json, FieldJsonOptions.Options)
                    ?? new List<FieldDefinition>());

    private static readonly ValueComparer<List<FieldDefinition>> FieldsComparer =
        new(
            (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
            l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            l => l.ToList()); // deep copy — this is what makes the snapshot correct

    public void Configure(EntityTypeBuilder<DataModel> builder)
    {
        builder.Property<List<FieldDefinition>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter, FieldsComparer) // ← always both together
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
- **No-args EF Core constructor**: when an aggregate's only constructor takes params EF Core can't bind (e.g. `IEnumerable<string>`), add `private Role() : base(default) { Name = null!; }`. Initialize all non-nullable fields to silence CS8618.
- **Migrations strategy**: Infrastructure tests use `context.Database.EnsureCreated()` (fast, no migration files). Production deployments need one EF Core migration bundle per `DbContext`.
- **Identity uses the global `public` schema** — `IdentityDbContext` is a plain `DbContext` with no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.

## Testing rules

- Never run `dotnet test --no-build` after editing test code — always let it recompile.
- **Never hardcode environment configurations**: connection strings, API URLs, Docker endpoints (e.g. `tcp://localhost:2375`), secret keys must use environment variables, `appsettings.json`, or `.testcontainers.properties`.
- **AI Agent Testing Scope**: run only unit tests locally via `dotnet test unit-tests.slnf`. Integration tests require Docker/Testcontainers and are verified by CI/CD on PR submission.
- **`unit-tests.slnf`**: solution filter at repo root including only Domain + Application test projects. When adding a new unit test project, also add it to this file.

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
- No logic in the mapping file — only `mediator.Send(...)` dispatch and minimal request mapping.
- Use `MapGroup` to apply route prefixes and auth policies at group level.
- JSON configuration via `ConfigureHttpJsonOptions`, never via `AddControllers().AddJsonOptions(...)`.
- **Required annotations on every endpoint**: `.WithName()`, `.WithSummary()`, `.WithTags()`, `.Produces<T>()`, `.ProducesProblem()` for each applicable status code (400, 401, 403, 404).

## Clean Code Principles

### SRP — Single Responsibility

Every class has exactly one reason to change.

- **Handler**: one command, one behavior. No direct email sending, no publishing Wolverine events manually, no audit logging — those belong in domain event handlers or pipeline behaviors.
- **Repository**: only persistence. No business logic, no calling other services.
- **Aggregate**: only its own invariants. No EF Core, no HTTP, no external calls.

```csharp
// ❌ wrong — handler doing too much
public async Task<Result> Handle(PublishWorkflowCommand cmd, CancellationToken ct)
{
    WorkflowDefinition? wf = await _repo.GetByIdAsync(cmd.Id, ct);
    wf!.Status = WorkflowStatus.Published;          // bypasses aggregate invariant
    wf.PublishedAt = DateTimeOffset.UtcNow;
    await _emailSender.SendAsync(wf.CreatedBy, "Published"); // wrong layer
    await _bus.PublishAsync(new WorkflowPublishedEvent(wf.Id)); // wrong — use domain events
    await _uow.SaveChangesAsync(ct);
    return Result.Success();
}

// ✅ correct — each concern in its proper place
public async Task<Result> Handle(PublishWorkflowCommand cmd, CancellationToken ct)
{
    WorkflowDefinition? wf = await _repo.GetByIdAsync(cmd.Id, ct);
    if (wf is null) return Result.Failure("Workflow not found.");
    wf.Publish(); // aggregate enforces: must have steps, must be Draft
                  // raises WorkflowPublishedEvent internally → Wolverine dispatches post-commit
                  // email sent by WorkflowPublishedEventHandler, not here
    await _uow.SaveChangesAsync(ct);
    return Result.Success();
}
```

---

### OCP — Open/Closed

Open for extension, closed for modification. Add behavior by adding new classes — not by editing existing ones.

The canonical example: **WorkflowEngine step types**. Each new step type is a new `IStepHandler` — never a new `case` in an existing switch.

```csharp
// ❌ wrong — every new step type requires editing this class
public Task ExecuteAsync(WorkflowStep step) => step.Type switch
{
    StepType.Form   => HandleForm(step),
    StepType.Http   => HandleHttp(step),
    StepType.Script => HandleScript(step),
    _               => throw new NotSupportedException() // ← fragile
};

// ✅ correct — new step type = new class, nothing else changes
public interface IStepHandler
{
    StepType StepType { get; }
    Task<StepResult> ExecuteAsync(WorkflowStep step, ExecutionContext ctx, CancellationToken ct);
}

// Adding Notification step: create NotificationStepHandler, register in DI. Done.
// HttpStepHandler, ScriptStepHandler remain untouched.
```

The same applies to **form field types** and **data field types** — always a new subclass of `FormFieldConfig` / `FieldConfig`, never a new branch in existing parsing logic.

---

### DIP — Dependency Inversion

Application layer defines interfaces. Infrastructure implements them. Never reference concrete infrastructure types from Application.

```csharp
// ❌ wrong — Application knows about EF Core
public class GetWorkflowHandler(WorkflowBuilderDbContext context) { ... }

// ✅ correct — Application depends only on its own interface
public class GetWorkflowHandler(IWorkflowRepository repository) { ... }
// IWorkflowRepository: defined in Application layer
// WorkflowRepository : EF Core implementation in Infrastructure layer
```

---

### DRY — Don't Repeat Yourself

Consolidate logic that has one canonical reason to exist. But only consolidate when the two things are the same concept — not just similar-looking code.

```csharp
// ❌ wrong — "not found" check copy-pasted across every handler
WorkflowDefinition? wf = await _repo.GetByIdAsync(id, ct);
if (wf is null) return Result.Failure("Workflow not found.");

// ✅ correct — one method, one place
// In IWorkflowRepository:
Task<Result<WorkflowDefinition>> GetRequiredAsync(Guid id, CancellationToken ct);
// Implementation returns Failure if null — handlers just unwrap the Result.

// ❌ wrong — same validation rule duplicated across validators
RuleFor(x => x.Name).NotEmpty().MaximumLength(200); // in CreateWorkflowValidator
RuleFor(x => x.Name).NotEmpty().MaximumLength(200); // in UpdateWorkflowValidator

// ✅ correct — one shared extension
public static IRuleBuilderOptions<T, string> IsValidWorkflowName<T>(
    this IRuleBuilder<T, string> rule) =>
    rule.NotEmpty().MaximumLength(200).WithMessage("Name must be 1–200 characters.");
```

---

### KISS — Keep It Simple

Prefer the simplest solution that correctly solves the problem. Don't introduce abstractions until you have ≥ 3 concrete cases that clearly need them.

```csharp
// ❌ wrong — generic base class for something that doesn't need generics
public abstract class BaseAggregateCommandHandler<TCommand, TAggregate>
    where TAggregate : AggregateRoot { ... } // premature abstraction

// ✅ correct — just write the handler
public class PublishWorkflowHandler(IWorkflowRepository repo, IUnitOfWork uow)
    : ICommandHandler<PublishWorkflowCommand, Result>
{
    public async Task<Result> Handle(PublishWorkflowCommand cmd, CancellationToken ct) { ... }
}
```

---

### YAGNI — You Ain't Gonna Need It

Never implement features or extensibility points not required by a current user story. Speculative abstractions become dead weight.

```csharp
// ❌ wrong — adding a plugin registry "for future extensibility"
services.AddSingleton<IWorkflowPluginRegistry>();    // no US requires this
services.AddSingleton<IStepExecutionHookProvider>(); // no US requires this

// ✅ correct — implement what the US says; add extensibility when a concrete
// requirement arrives, not before.
```

---

### Tell Don't Ask

Tell objects what to do. Don't ask for their state and make decisions externally — that logic belongs inside the object.

```csharp
// ❌ wrong — handler asks about state and decides (Ask)
if (workflow.Status == WorkflowStatus.Draft && workflow.Steps.Count > 0)
{
    workflow.Status = WorkflowStatus.Published;
    workflow.PublishedAt = DateTimeOffset.UtcNow;
}

// ✅ correct — handler tells aggregate what to do (Tell)
workflow.Publish(); // aggregate owns: what "valid to publish" means,
                    // what state to set, what event to raise
```

---

## Design Patterns in Practice

### Strategy — behavior dispatch by type

Use when a type discriminator (enum or string) determines behavior. Never switch on it — dispatch to a registered strategy.

```csharp
// Resolution pattern — executor resolves strategy from DI
IStepHandler handler = _handlers.FirstOrDefault(h => h.StepType == step.Type)
    ?? throw new InvalidOperationException($"No handler registered for {step.Type}.");
StepResult result = await handler.ExecuteAsync(step, context, ct);
```

This pattern already exists in WorkflowEngine for step types. Apply the same pattern anywhere you see a type-based `switch` controlling behavior: form field rendering, notification channel selection, trigger type dispatching.

---

### Factory Method — aggregate creation via static factory

Never expose a public aggregate constructor. Use a static factory to enforce creation invariants and raise the `Created` domain event in one place.

```csharp
// ❌ wrong — public constructor, no invariant, no event
WorkflowDefinition wf = new(name, orgId, userId);

// ✅ correct — static factory method
public static WorkflowDefinition Create(string name, Guid organizationId, Guid createdByUserId)
{
    if (string.IsNullOrWhiteSpace(name))
        throw new InvalidOperationException("Workflow name is required.");

    WorkflowDefinition wf = new(name, organizationId, createdByUserId);
    wf.AddDomainEvent(new WorkflowCreatedEvent(wf.Id, organizationId, createdByUserId));
    return wf;
}

// Domain aggregate constructor is private or internal (via InternalsVisibleTo for tests)
```

---

### Guard Clauses — flat, readable invariant enforcement

Use early-return guards in domain methods. The happy path stays at the bottom, readable at a glance.

```csharp
// ❌ wrong — nested if pyramid, hard to read
public void AddStep(WorkflowStep step)
{
    if (Status == WorkflowStatus.Draft)
    {
        if (_steps.Count < 50)
        {
            if (!_steps.Any(s => s.Name == step.Name))
            {
                _steps.Add(step);
            }
            else throw new InvalidOperationException("Duplicate step name.");
        }
        else throw new InvalidOperationException("Maximum 50 steps.");
    }
    else throw new InvalidOperationException("Cannot add steps to a published workflow.");
}

// ✅ correct — guards up front, happy path at the bottom
public void AddStep(WorkflowStep step)
{
    if (Status != WorkflowStatus.Draft)
        throw new InvalidOperationException("Cannot add steps to a published workflow.");
    if (_steps.Count >= 50)
        throw new InvalidOperationException("Workflow cannot exceed 50 steps.");
    if (_steps.Any(s => s.Name == step.Name))
        throw new InvalidOperationException($"A step named '{step.Name}' already exists.");

    _steps.Add(step);
    AddDomainEvent(new StepAddedEvent(Id, step.Id));
}
```

---

### Specification — reusable query predicates

Extract repeated `Where(...)` conditions into named specifications. Keeps query logic out of handlers and composable.

```csharp
public static class WorkflowSpecs
{
    public static Expression<Func<WorkflowDefinition, bool>> IsActive() =>
        w => w.DeletedAt == null;

    public static Expression<Func<WorkflowDefinition, bool>> IsPublished() =>
        w => w.Status == WorkflowStatus.Published;

    public static Expression<Func<WorkflowDefinition, bool>> BelongsToOrg(Guid orgId) =>
        w => w.OrganizationId == orgId;
}

// Repository method stays clean:
IQueryable<WorkflowDefinition> query = _context.WorkflowDefinitions
    .Where(WorkflowSpecs.IsActive())
    .Where(WorkflowSpecs.IsPublished())
    .Where(WorkflowSpecs.BelongsToOrg(orgId));
```

Use specifications for predicates that appear in more than one repository method. For one-off conditions, inline `Where(...)` is fine (YAGNI).

---

### Pipeline Behavior — cross-cutting concerns via MediatR

Add cross-cutting concerns (logging, performance tracking, authorization checks) as MediatR pipeline behaviors — never inline in handlers.

```csharp
// ❌ wrong — cross-cutting concern polluting every handler
public async Task<Result> Handle(CreateWorkflowCommand cmd, CancellationToken ct)
{
    Stopwatch sw = Stopwatch.StartNew();
    _logger.LogInformation("Handling {Command}", nameof(CreateWorkflowCommand));
    // ... actual logic ...
    _logger.LogInformation("Completed in {Ms}ms", sw.ElapsedMilliseconds);
}

// ✅ correct — one behavior, registered once, applies to all handlers
public class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Stopwatch sw = Stopwatch.StartNew();
        TResponse response = await next();
        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("Slow handler {Request} took {Ms}ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return response;
    }
}
```

Existing behaviors in the pipeline: `ValidationBehavior`. New cross-cutting concerns follow the same pattern.

---

### Value Object — when to use vs primitive

Use a value object when a primitive carries validation rules, formatting, or domain behavior. Don't wrap everything — only where the type has meaning beyond its raw value.

```csharp
// ❌ wrong — email validated in multiple places (validator, handler, maybe domain)
public string Email { get; private set; }

// ✅ correct — one value object owns the rule, equality is by content
public sealed record Email
{
    public string Value { get; }
    private Email(string value) => Value = value;

    public static Result<Email> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.Contains('@'))
            return Result.Failure<Email>("Invalid email address.");
        return Result.Success(new Email(raw.Trim().ToLowerInvariant()));
    }

    public override string ToString() => Value;
}
```

**Use a value object for:** Email, OrganizationSlug, Money, DateRange, PhoneNumber — anything with a format rule or behavior.  
**Keep as primitive:** `Guid` IDs, simple `int` counts, plain `bool` flags — no behavior, no format rules.

---

### Builder — readable test data construction

For tests requiring complex aggregates with many fields, use a test builder. Builders live in the test project only — never in production code.

```csharp
// ❌ wrong — fragile and unreadable
WorkflowDefinition wf = WorkflowDefinition.Create("Flow", Guid.NewGuid(), Guid.NewGuid());
wf.AddStep(new WorkflowStep(Guid.NewGuid(), "Collect", StepType.Form, ...));
wf.AddStep(new WorkflowStep(Guid.NewGuid(), "Notify", StepType.Http, ...));
wf.Publish();

// ✅ correct — builder with sensible defaults
WorkflowDefinition wf = new WorkflowDefinitionBuilder()
    .WithName("Approval Flow")
    .WithOrganization(orgId)
    .WithStep(StepType.Form, "Collect Request")
    .WithStep(StepType.Http, "Notify Manager")
    .Published()
    .Build();
```

---

### Outbox via Wolverine — reliable domain event dispatch

Domain events must survive infrastructure failures. Wolverine's outbox pattern stores events in the same transaction as the aggregate write, then dispatches them asynchronously. This guarantees at-least-once delivery even if the process crashes between `SaveChanges` and the dispatch.

```csharp
// This happens automatically when you use UnitOfWork correctly:
// 1. wf.Publish() → AddDomainEvent(new WorkflowPublishedEvent(...))
// 2. await _uow.SaveChangesAsync(ct)
//    └─ UnitOfWork: saves aggregate to DB + writes event to Wolverine outbox (same transaction)
//    └─ Wolverine: reads outbox, dispatches WorkflowPublishedEvent to handler (async, after commit)

// You never need to manually publish a domain event.
// If you find yourself calling _bus.PublishAsync() for a domain event inside a handler → stop.
```

**Rule:** Domain events are dispatched by the UnitOfWork via Wolverine's outbox — never manually in a handler. Integration events to external systems (webhooks, third-party APIs) use Wolverine jobs scheduled from within the domain event handler.

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
    // Durable outbox + transport via PostgreSQL (same DB, no extra broker)
    opts.UsePostgresqlPersistenceAndTransport(
        builder.Configuration.GetConnectionString("Default")!);

    // Auto-wrap handlers that use EF Core DbContext in a transaction
    opts.Policies.AutoApplyTransactions();

    // Local queues are durable by default (survive process restart)
    opts.Policies.UseDurableLocalQueues();
});
```

### Intra-module domain event handler

Domain events raised by aggregates (`AddDomainEvent`) are stored in the Wolverine outbox by `UnitOfWork.SaveChangesAsync`, then dispatched asynchronously after the transaction commits. Define the handler in the Application layer:

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
- Never call `_messageBus.PublishAsync` for a domain event inside a Command handler — the `UnitOfWork` dispatches events automatically via the outbox.
- Integration events to external systems (webhooks, third-party APIs) are Wolverine jobs scheduled from within a domain event handler, not from the command handler directly.

---

## Cross-module read pattern

Modules never share DbContexts and never reference each other's repositories or Application assemblies.

**Option A — Wolverine request/response (preferred for non-critical paths):**

Publish a query message via `_messageBus.InvokeAsync<TResponse>`. The responding module handles it in its own Application layer. Clean separation, but adds a small round-trip.

**Option B — Raw SQL reader (for critical hot paths):**

A thin reader class in the consuming module's Infrastructure layer executes a targeted SQL query directly on the source module's table. Acceptable when Wolverine round-trip latency is unacceptable (e.g., execution hot path). Must be documented with a comment.

```csharp
// WorkflowEngine.Application/CrossModule/IWorkflowDefinitionReader.cs
// Interface defined in Application — Infrastructure implements it.
public interface IWorkflowDefinitionReader
{
    Task<WorkflowSnapshot?> GetPublishedAsync(Guid workflowId, CancellationToken ct);
}

// WorkflowEngine.Infrastructure/CrossModule/WorkflowDefinitionReader.cs
internal sealed class WorkflowDefinitionReader(NpgsqlDataSource dataSource, ITenantContext tenant)
    : IWorkflowDefinitionReader
{
    // Cross-module raw SQL read — justified because WorkflowEngine reads the workflow definition
    // on every step execution; Wolverine request/response would add unacceptable latency here.
    public async Task<WorkflowSnapshot?> GetPublishedAsync(Guid workflowId, CancellationToken ct)
    {
        string schema = tenant.Schema;
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        // SELECT only the columns needed — never SELECT *
        // Always filter by schema and status to avoid cross-tenant reads
    }
}
```

**Rules:**
- `IWorkflowDefinitionReader` is defined in the Application layer of the consuming module — never imported from the source module's assembly.
- Always prefix the table name with `ITenantContext.Schema`.
- Always add a comment explaining why raw SQL cross-module was used over Wolverine request/response.
- Select only the columns needed — never `SELECT *`.

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

### 2. No scaffold placeholder files

Visual Studio scaffolds `Class1.cs` when creating a new project. These files must be deleted immediately — never committed. A `Class1.cs` anywhere in `src/` or `tests/` is always wrong.

**Detection command:**
```bash
find src/ tests/ -name "Class1.cs" -not -path "*/obj/*"
```

### 3. No direct commits to `main`

Every change — including one-line fixes — goes through a branch + PR. Steps:

```bash
git checkout -b chore/my-fix   # branch off current HEAD
# make changes
git add <files>
git commit -m "chore: ..."
git push -u origin chore/my-fix
gh pr create …
```

If a commit lands on `main` by mistake, move it before pushing:
```bash
git checkout -b chore/rescue-branch   # create branch at current (wrong) HEAD
git checkout main
git reset --hard origin/main          # reset main to remote — commit stays on rescue-branch
git push                              # push clean main
# then open a PR from rescue-branch
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
├── {screen-slug}.excalidraw   ← source (JSON, diffable on GitHub)
└── {screen-slug}.svg          ← rendered preview (vector, renders inline on GitHub)
```

**Naming:** kebab-case matching the primary route segment — `login`, `data-models`, `workflow-detail`.

**One wireframe per screen.** Multiple user stories on the same screen share one wireframe file.

**Linking from a feature file** — add directly after the feature title, before the first user story:

```markdown
> **Wireframe**: [docs/wireframes/login.excalidraw](../../wireframes/login.excalidraw) · [preview](../../wireframes/login.svg)
```

**Excalidraw settings** for consistent sketch aesthetic:
- `roughness: 1` on all shapes
- `fillStyle: "solid"` for filled shapes
- `fontFamily: 1` (Virgil — hand-drawn feel)
- `strokeWidth: 2` for card/container outlines, `1` for inputs

**Generate SVG** after every edit: run `docs/scripts/generate-wireframes.ps1` — regenerates all `.svg` files from `.excalidraw` source via Kroki.io. Commit both `.excalidraw` and `.svg` together.

**Pitfall:** committing only `.excalidraw` without `.svg` means the wireframe is invisible on GitHub without the VS Code extension. Always run the script and commit both.
