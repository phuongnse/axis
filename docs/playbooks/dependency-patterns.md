# Dependency Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for NuGet package changes and dependency-injection lifetimes.

---

## NuGet / packaging rules

- **Never use `dotnet add package`** — it corrupts `Directory.Packages.props` (CPM project). Always edit `Directory.Packages.props` directly.
- **Search NuGet before assuming a package ID** — NuGet IDs often differ from project names. Run `dotnet package search "<name>"` when unsure of the correct ID.
- **Check transitive dependency versions** after adding any new infrastructure package — run `dotnet build` immediately to catch conflicts introduced by the new dependency.
- **Non-web test projects needing ASP.NET Core types** — use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, never `<PackageReference Include="Microsoft.AspNetCore.Http" />`.

## Dependency Injection pitfalls

### Captive dependency — scoped service inside a singleton

A singleton that captures a scoped service holds it for the application lifetime. The scoped service (e.g. `DbContext`, `IWorkspaceContext`) was designed to be created per-request — holding it in a singleton causes workspace context bleed across requests and DbContext reuse across threads.

```csharp
// ❌ wrong — IWorkspaceContext is scoped; singleton captures it at startup
public class MyCache(IWorkspaceContext workspaceContext) // singleton captures scoped
{
    public string GetKey() => $"cache:{workspaceContext.Schema}"; // wrong workspace after first request
}

// ✅ correct — inject IServiceScopeFactory and resolve per-operation
public class MyCache(IServiceScopeFactory scopeFactory)
{
    public async Task<string> GetKeyAsync()
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWorkspaceContext workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceContext>();
        return $"cache:{workspace.Schema}";
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
