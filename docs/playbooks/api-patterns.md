# API Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for list queries, response DTOs, pagination, Minimal API endpoints, OpenAPI annotations, and HTTP status mapping.

---

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
.WithSummary("List workflow definitions for the current workspace")
.WithTags("WorkflowBuilder")
.Produces<PagedResult<WorkflowSummaryDto>>()
.ProducesProblem(StatusCodes.Status401Unauthorized)
.RequireAuthorization();
```

## Minimal API endpoint wiring

- Each module exposes a `Map{ModuleName}Endpoints(IEndpointRouteBuilder)` extension method.
- No logic in the mapping file — only `mediator.Send(...)` dispatch and minimal request mapping. Do not parse `HttpContext` claims, build default command payloads, or map enums in the endpoint — that belongs in Application. Inject `ICurrentUser` (`Axis.Shared.Application.Identity`) into handlers to resolve the caller; let handlers default optional payloads.
- Use `MapGroup` to apply route prefixes and auth policies at group level.
- JSON configuration via `ConfigureHttpJsonOptions`, never via `AddControllers().AddJsonOptions(...)`.
- **Required annotations on every endpoint**: `.WithName()`, `.WithSummary()`, `.WithTags()`, `.Produces<T>()`, `.ProducesProblem()` for each applicable status code (400, 401, 403, 404).

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
        new CreateWorkflowCommand(request.Name, request.Description, currentUser.WorkspaceId, currentUser.UserId.ToString()), ct);

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

Every endpoint must be fully annotated — see AGENTS.md API Layer section for required metadata.

---
