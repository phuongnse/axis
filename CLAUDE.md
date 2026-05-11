# Axis — Project Context for Claude

## What is Axis
Multi-tenant low-code SaaS platform for building data-driven workflow applications. Users define custom data models, design visual workflows, create forms, and build UI pages — all without writing code.

## Tech Stack
- **Backend**: .NET 8 / ASP.NET Core — Modular Monolith + DDD + CQRS (MediatR)
- **ORM**: Entity Framework Core + Npgsql (PostgreSQL)
- **Background jobs + messaging**: Wolverine (NOT Hangfire)
- **Auth**: OpenIddict 5.x — OAuth2/OIDC server (Authorization Code + PKCE for SPA; Client Credentials for external integrations)
- **Real-time**: ASP.NET Core SignalR
- **Validation**: FluentValidation
- **Logging**: Serilog
- **Frontend**: React 18 + TypeScript + Vite
- **UI components**: shadcn/ui + Tailwind CSS
- **Workflow canvas**: @xyflow/react (React Flow)
- **Page builder DnD**: dnd-kit
- **Data fetching**: TanStack Query
- **State**: Zustand
- **Database**: PostgreSQL 16 — schema-per-tenant
- **Cache**: Redis 7
- **Tests**: xUnit + FluentAssertions + NSubstitute + Testcontainers

## Architecture
Modular Monolith. 6 modules, each with Domain / Application / Infrastructure layers:
- **Identity** — auth, users, roles, RBAC
- **DataModeling** — custom models, field types, data classes, record CRUD
- **WorkflowBuilder** — workflow definitions, step config, triggers, branching, parallel
- **FormBuilder** — form definitions, fields, workflow integration, submissions
- **WorkflowEngine** — execution orchestrator, step handlers, error handling, history, retry
- **PageBuilder** — pages, widgets, drag & drop layout, data binding (Phase 2)

Shared Kernel: `Axis.Shared.Domain`, `Axis.Shared.Application`, `Axis.Shared.Infrastructure`

Multi-tenancy: schema-per-tenant in PostgreSQL (`tenant_{org_slug}`). Tenant resolved from JWT `org_id` claim; schema name cached in Redis.

## Module dependency rules
- Modules communicate ONLY via **asynchronous domain events** (Wolverine) or explicit Application-layer interfaces.
- **No shared database transactions** across module boundaries.
- Cross-module data consistency via Eventual Consistency.
- Domain layer: zero external dependencies (pure C#, fully unit testable).
- Application layer: depends on Domain only — no infrastructure references.
- Infrastructure: implements interfaces defined in Application/Domain.

---

## How to navigate this project

Before starting any task, read only what is relevant — not everything.

**Step 1 — Identify the module(s) affected.**

| Module | Epic folder |
|--------|-------------|
| Identity | `docs/epics/E02-identity-access/` |
| DataModeling | `docs/epics/E03-data-modeling/` |
| WorkflowBuilder | `docs/epics/E04-workflow-builder/` |
| FormBuilder | `docs/epics/E05-form-builder/` |
| WorkflowEngine | `docs/epics/E06-workflow-engine/` |
| PageBuilder | `docs/epics/E07-page-builder/` |

**Step 2 — Read the epic README** for that module: `docs/epics/{folder}/README.md`

**Step 3 — Read only the feature file(s)** for the task: `docs/epics/{folder}/features/F0{N}-*.md`

**Step 4 — Check implementation status** in [`docs/PROGRESS.md`](docs/PROGRESS.md)

**Step 5 — Read [`docs/PATTERNS.md`](docs/PATTERNS.md)** only if the task involves: adding NuGet packages, EF Core aggregate mapping or JSONB mapping, Minimal API endpoint wiring, writing tests, implementing a list/query endpoint (pagination, N+1, projection), adding async methods (CancellationToken, sync-over-async, fire-and-forget), defining response DTOs, writing repository methods, adding domain methods to an aggregate, working with multi-tenant raw SQL, implementing a new step type or field type, adding a cross-cutting concern, or any design decision about where logic should live.

Do NOT read all docs upfront. The feature file defines the contract for the task at hand.

---

## Development Rules

- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions.
- **Test naming convention**: `{Subject}_{Condition}_{ExpectedOutcome}` — e.g. `CreateWorkflow_WhenNameIsDuplicate_ReturnsConflictError`. No generic names like `Test1` or `ShouldWork`.
- **Test isolation**: Each integration test class must implement `IAsyncLifetime` for per-class Testcontainers setup/teardown. Tests within a class must never share mutable database state — each test method must call a `ResetAsync()` cleanup helper at its start to truncate relevant tables. Never assume a test runs first. Flaky tests caused by ordering dependencies are bugs. See PATTERNS.md for the full pattern.
- **Testing Database**: Integration and Infrastructure tests MUST use **Testcontainers** (PostgreSQL/Redis). The EF Core In-Memory database provider (`UseInMemoryDatabase`) is strictly forbidden.
- **DDD**: Apply fully to complex modules (WorkflowEngine, DataModeling) — rich aggregates, domain events, value objects, all invariants enforced in the domain. For simpler CRUD modules (Identity), pragmatic means: aggregates may have minimal behavior, but still apply the Result pattern, repository interfaces, value objects for typed IDs and Email, and no Data Annotations on domain types.
- **Diagrams**: add proactively when a flow is complex enough that text alone doesn't convey it clearly.
- **Docs-first, always — non-negotiable**: Before implementing any user story, feature, or fix, read the relevant feature file in `docs/epics/`. The doc defines the contract; code implements it. Never write code first and update docs after. Every code change that affects observable behavior must also update the relevant doc in the same commit.
- **Every command/query maps to a US**: Never invent requirements. If a new requirement is discovered during implementation, add it to the docs first, then implement.
- **AC compliance is mandatory — no silent skips**: Every US must be implemented to ALL its acceptance criteria. Never defer or skip an AC without explicitly documenting it as a gap in the `> **Implementation status**` callout. A US is not done until every AC is either implemented or documented as a gap.
- **A layer cannot be marked ✅ Done if any US in scope is missing its callout**: Verify every US in every feature file for that module has an `> **Implementation status**` callout before updating layer status. A US with no callout = silently skipped = the layer is not done.
- **Read files in full before making claims**: Never use a line `limit` when the goal is to assert something about a file's content. Partial reads → wrong conclusions.
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git workflow**: Never push directly to `main` — always create a branch and open a PR. Branch naming: `{type}/{short-description}` in kebab-case, where `type` ∈ `feat | fix | docs | refactor | test | chore`. When Claude Code auto-creates a worktree with a random branch name, rename the branch before pushing. Commit messages follow Conventional Commits: `feat: add workflow step handler`, `fix: resolve tenant schema collision`, `docs: update FormBuilder US callout`. Subject line ≤ 72 chars, imperative mood, no period.
- **CLAUDE.md maintenance**: update this file whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.
- **Explicit over Implicit — no `var`**: Always write the explicit type. Never use `var`, even when the assignment makes the type obvious.
- **Minimal API (mandatory)**: All new endpoint work uses Minimal API (`MapGroup` + `IEndpointRouteBuilder` extension methods), not traditional controllers. Use `ConfigureHttpJsonOptions` for JSON configuration. Never default silently to traditional controllers.
- **Minimal API Structure**: Each module exposes a `Map{ModuleName}Endpoints(IEndpointRouteBuilder)` extension method. No logic in the mapping file — only MediatR dispatching.
- **API Error Responses**: All endpoints MUST map `Result` failures to standard HTTP `ProblemDetails` (RFC 7807) responses. Do not return custom error JSON structures or raw strings.
- **Idempotency**: All Command handlers and Migrations must be idempotent.
- **Surface architectural decisions before implementing**: Before starting any new layer, module, or API surface, list the 2–3 key design choices and confirm with the user.
- **No inline fully-qualified type names**: Always use `using` directives. Never write `typeof(Axis.Some.Long.Namespace.MyType)` inline.
- **Mandatory Result Pattern**: Command/Query handlers return `Result` or `Result<T>` for business rule violations. FluentValidation validators run via `ValidationBehavior` pipeline — never throw `ValidationException` manually in a handler. Exceptions are reserved for infrastructure failures. Domain aggregates guard with `throw InvalidOperationException` for internal invariants only.
- **Centralized Global Usings**: Use `GlobalUsings.cs` per project or `<Using Include="..." />` in `Directory.Build.props` for ubiquitous namespaces.
- **Comment policy — WHY not WHAT**: Default to no comments. Add one only when the WHY is non-obvious: a hidden constraint, a framework quirk, or business logic that would surprise a reader.
- **Logging Policy**: Use Serilog for all logging. Always use **structured logging** (e.g., `_logger.LogInformation("Processing order {OrderId} for user {UserId}", orderId, userId)`). Log `Error` for exceptions/system failures, `Warning` for unexpected but handled edge cases, and `Information` for critical business milestones. Do not log sensitive PII/Credentials.
- **Correlation ID**: Every request must carry a `X-Correlation-Id` header. Middleware generates a new GUID if the header is absent and echoes it on the response. All Serilog log entries must be enriched with `CorrelationId` via `LogContext.PushProperty`. This is the primary key for tracing a request across all log lines in production — never omit it.
- **Code Style & Linting**: All code must pass `dotnet format` without warnings before pushing. Follow standard C# naming conventions.
- **Complexity Guardrails**: Keep methods small and focused. Minimal API endpoints MUST NOT contain any business logic — they only extract parameters, dispatch to MediatR, and map `Result` objects to `IResult` HTTP responses.
- **Endpoint Authorization**: Every endpoint MUST call `.RequireAuthorization()` unless it is explicitly an anonymous/public endpoint (e.g. login, register, health check). RBAC policy enforcement goes in the Command/Query handler via the current user's claims — not in the endpoint mapping.
- **OpenAPI documentation**: Every Minimal API endpoint must declare `.WithName()` (unique operation ID), `.WithSummary()` (one-line description), `.WithTags()` (module name), `.Produces<TResponse>()`, and `.ProducesProblem()` for each error status it can return (400, 401, 403, 404 as applicable). Swagger UI must be enabled in Development and Staging environments.
- **CancellationToken propagation**: All `async` methods in Application and Infrastructure layers must accept and forward `CancellationToken`. Pass it to every EF Core query, repository call, and HTTP client call. Never substitute `CancellationToken.None` inside a handler.
- **No sync-over-async**: Never call `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on a `Task` in an async context. Always `await` directly. This causes thread-pool starvation and deadlocks in ASP.NET Core under load — it is a production incident waiting to happen.
- **No N+1 queries**: Lazy loading is disabled globally. Always use explicit `.Include()` / `.ThenInclude()` for navigation properties needed in a query. List queries must project to DTOs via `.Select()` — never load full aggregates then map in memory for collections. Accessing an un-`Include`d navigation property in a handler is a bug.
- **Pagination mandatory**: No endpoint may return an unbounded collection. All list endpoints must accept `int page = 1` and `int pageSize = 20` query parameters with a hard cap of `pageSize ≤ 100`. Return a `PagedResult<T>` (defined in `Axis.Shared.Application`) containing `Items`, `TotalCount`, `Page`, and `PageSize`.
- **CQRS & Messaging Boundaries**: **MediatR** is strictly for internal module CQRS (Commands/Queries). **Wolverine** is strictly for inter-module asynchronous domain events and background jobs. Do not mix their purposes.
- **EF Core Configuration**: Always use Fluent API (`IEntityTypeConfiguration<T>`) for EF Core mappings in the Infrastructure layer. **Data Annotations** (e.g., `[Required]`, `[Table]`) are strictly forbidden in Domain entities.
- **EF Core JSONB collections — always pair converter with comparer**: Any `List<T>` or collection stored as JSONB via `HasConversion` MUST also declare a `ValueComparer` in the same configuration call (`HasConversion(converter, comparer)`). A converter without a comparer means EF Core uses reference equality — mutations to the list are silently not persisted. See PATTERNS.md for the correct pattern. Never work around this by overriding `SaveChangesAsync` to force-mark properties as modified.
- **NuGet Packages & Dependencies**: Always check `Directory.Packages.props` before adding any new libraries. Do not add libraries that are not explicitly approved or hallucinate versions. Do not upgrade packages unless explicitly requested.
- **Safe Editing & Refactoring**: Only modify code directly related to the current task. **Never** delete, comment out, or massively restructure code outside the scope of your task unless explicitly requested. Preserve existing logic if you don't fully understand it.
- **No hardcoded secrets**: Connection strings, JWT signing keys, Redis passwords, SMTP credentials, and API keys must never appear in source code or committed config files. Use `appsettings.Development.json` (gitignored) or environment variables for local dev. Testcontainers generates its own ephemeral credentials — never reference external service credentials in test code.
- **Response DTOs — never leak domain models**: Query handlers must return dedicated response record types (`*Response` or `*Dto`) defined in the Application layer. Domain entities, aggregates, EF Core–tracked objects, and value objects must never appear in HTTP responses. All mapping happens inside the query handler, not in the endpoint.
- **Audit fields**: Every tenant-owned aggregate must declare `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`, and `string CreatedBy` (user ID). These are set by the Application layer via `ICurrentUser` / `ITenantContext` before calling the repository — never set inside the domain aggregate itself.
- **Soft delete policy**: Tenant-owned aggregates use **soft delete** — add `DateTimeOffset? DeletedAt` and register a global EF Core query filter (`HasQueryFilter(e => e.DeletedAt == null)`) in `IEntityTypeConfiguration`. Platform-level data in the `public` schema (organizations, subscriptions) uses **hard delete**. Never mix strategies within the same module.
- **Optimistic concurrency**: Aggregates subject to concurrent writes must include a `uint RowVersion` concurrency token mapped via `.IsRowVersion()` in EF Core configuration. Handlers must catch `DbUpdateConcurrencyException` and return `Result.Failure` — never let it surface as an unhandled 500.
- **Rate limiting**: Auth endpoints (`/connect/token`, `/connect/authorize`, password reset) must be protected by a rate limiter (`AddRateLimiter` with a fixed-window policy). Unauthenticated endpoints that accept user input must also be rate-limited. Authenticated API endpoints do not require rate limiting by default.
- **CORS**: Configure a named CORS policy in `Program.cs` that explicitly allowlists the SPA origin(s). Never use `AllowAnyOrigin()` in production. The policy must be applied via `app.UseCors()` before `app.UseAuthentication()`.
- **Health checks**: The host must expose `GET /health` (liveness) and `GET /health/ready` (readiness) via `AddHealthChecks()`. Readiness must include checks for PostgreSQL connectivity and Redis connectivity. Both endpoints are anonymous (no auth required) and excluded from rate limiting.

## Agent Integrity Rules

These rules exist to prevent a specific failure mode: an agent hitting a blocker, silently working around it, updating docs to justify the deviation, then marking work as done. This has happened before on this project (OpenIddict replaced with custom JWT without user approval). These rules are non-negotiable.

- **Tech stack is immutable without explicit user approval**: The Tech Stack section above is the authoritative list. Never substitute, add, or remove a library — even temporarily or "just to unblock". If a specified library cannot be used (version conflict, missing feature, etc.), STOP immediately, describe the exact blocker, and wait for the user to decide. Do not implement an alternative.

- **Spec → code. Never code → spec**: The relationship is always one direction. If your implementation deviates from a feature file AC, a Tech Stack entry, or an ADR, that deviation is a GAP to be documented — never a reason to update the spec to match what you did. Updating CLAUDE.md's Tech Stack, an ADR in TECH_STACK.md, or an AC list to retroactively approve something you already implemented is forbidden.

- **Never work around a failing test**: Tests exist to verify production code. If a test is failing, fix the production code. Never: weaken assertions to make them pass, add `.Skip()` / `[Fact(Skip=...)]`, introduce excessive mocking that bypasses the behavior under test, or change what a test verifies in order to make it green. A failing test that correctly describes the expected behavior is more valuable than a passing test that hides a bug.

- **Tech stack compliance check is mandatory before marking ✅**: Before marking any layer ✅ in PROGRESS.md or a feature callout, explicitly verify: every library used in that layer appears in the approved Tech Stack in CLAUDE.md. If any deviation exists — even a small one — mark the layer ⚠️ and document the gap. Never mark ✅ to avoid a difficult conversation.

- **Architectural decisions always require user confirmation**: Any decision affecting which library or framework is used, the structure of a cross-cutting concern (auth tokens, tenant isolation, event dispatch), or module communication patterns must be surfaced and confirmed with the user before implementation — even when the choice seems obvious. "It seemed like the right thing to do" is not sufficient justification.

- **"No exceptions, no asking" does not apply to architectural blockers**: The Priority Order rule ("no exceptions, no asking") governs the *direction of work* — which module or layer to tackle next. It does not apply when you hit a technical blocker or an architectural ambiguity. In those cases, always stop and ask. Proceeding silently is worse than asking.

## Multi-tenancy & Migrations

- **Schema Management**: Every new migration must be tested against multiple schemas using Testcontainers before being marked ✅ Done.
- **Tenant Isolation**: Direct access to `public` schema from tenant-aware services is strictly forbidden.
- **Identity uses the global `public` schema** — `IdentityDbContext` has no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.
- **Migration workflow**: Run `dotnet ef migrations add {MigrationName} --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/Axis.Api`. Migration names use PascalCase describing the schema change — e.g. `AddWorkflowStepTable`, `AddTenantIdToFormSubmission`. Migrations are applied automatically at startup via `MigrateAsync()` in the host; never apply manually in production. Every migration must be idempotent (use `migrationBuilder.Sql` with existence checks when EF scaffolding is insufficient).

## Priority order

When deciding what to work on next, always follow this order — no exceptions, no asking:
1. **Gaps / bugs / issues** — documented gaps in feature callouts, known correctness bugs, failing tests
2. **Current layer completion** — finish the layer currently in progress across all modules before starting the next layer
3. **Next planned layer** — follow the established layer order (Domain → Application → Infrastructure → API → Frontend)

Never ask the user which direction to take if the priority order makes it unambiguous.

**Exception — always stop and ask when:**
- A specified library in the Tech Stack cannot be used for a concrete technical reason
- An AC or feature file requirement is ambiguous enough that two reasonable interpretations lead to different implementations
- Completing a task requires making an architectural decision not already documented
- A test is failing and fixing it would require deviating from a rule in this file

## Definition of Done

A US or layer is NOT done until all of the following are complete in the same commit:

### Completing a User Story (any layer)
1. ✅ Tests written first and passing
2. ✅ Feature file updated — add/update the `> **Implementation status**` callout directly after the US's *Out of scope* block:
   ```
   > **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅/⏳ | API: ⏳ | Frontend: ⏳
   > Gaps vs spec: [ACs not yet covered and why — omit this line if none]
   > Decisions: [design choices that affect how an AC is interpreted — omit this line if none]
   ```
   Each layer is tracked independently. Use ✅ when complete, ⏳ when in progress or not yet started.
3. ✅ If a gap vs spec is found during implementation, document it in the callout — do not silently skip it

### Completing a layer for a module (Domain, Application, Infrastructure, API)
1. ✅ All tests for that layer passing
2. ✅ Every US in every feature file for that module has an `> **Implementation status**` callout
3. ✅ All US callouts updated to reflect the new layer status
4. ✅ Epic README `Implementation Status` table updated (e.g. `Infrastructure: ✅ Done`)
5. ✅ [`docs/PROGRESS.md`](docs/PROGRESS.md) updated — **not** CLAUDE.md

### Completing a feature fix or refactor
1. ✅ Tests updated/added and passing
2. ✅ If the fix changes observable behavior: update the affected US callout's Gaps or Decisions line
3. ✅ If the fix closes a documented gap: remove that gap from the callout

## Layer order

For any **new** module: complete Domain → Application before touching Infrastructure. Infrastructure requires Docker (PostgreSQL + Redis via Testcontainers) and is done in one pass after all business logic is proven.

> Existing modules (Identity, DataModeling, WorkflowBuilder, FormBuilder, WorkflowEngine) have already completed Domain → Application → Infrastructure. The current focus is the API layer for WorkflowBuilder, FormBuilder, and WorkflowEngine. See [`docs/PROGRESS.md`](docs/PROGRESS.md) for current state.

## Frontend Rules

- **Folder structure**: Feature-based, not type-based. Each feature lives in `src/features/{feature-name}/` with its own components, hooks, and types co-located. Shared UI goes in `src/components/ui/`.
- **State boundaries**: TanStack Query owns all server state (fetching, caching, mutations). Zustand owns global client-only state (e.g. sidebar open, active tenant). Never store server data in Zustand; never cache client UI state in TanStack Query.
- **Forms**: Use `react-hook-form` + Zod schema for all forms. Define the Zod schema first (it is the source of truth for validation), then infer the TypeScript type from it.
- **API error handling**: All TanStack Query mutations must handle error state explicitly — surface errors via toast or inline message, never silently swallow them. Use a shared `ApiError` type for typed error responses from the backend.
- **No `any`**: TypeScript strict mode is on. Never use `any`. Use `unknown` + type guards when the shape is genuinely unknown.
- **Component size**: Keep components small and single-purpose. Extract hooks for any non-trivial logic — components should be mostly JSX.
- **Error Boundaries**: Wrap every top-level route in a React Error Boundary. Never allow an unhandled render error to crash the entire app. Each boundary must render a user-actionable fallback — not a blank screen.
- **Loading and empty states**: Every component that fetches async data must explicitly handle three states: loading (skeleton or spinner), empty (descriptive zero-state message), and error (user-actionable message with retry). Rendering nothing silently while data loads is a bug.

## Epics & docs navigation

- `docs/README.md` — master navigation
- `docs/epics/E0{N}-*/README.md` — epic overview + implementation status table
- `docs/epics/E0{N}-*/features/F0{N}-*.md` — feature + user stories with ACs
- `docs/diagrams/` — system-level diagrams (.puml + .png)
- `docs/scripts/generate-diagrams.ps1` — regenerates PNGs from .puml via Kroki.io POST API

## Solution structure

```
src/
├── Axis.Api/                          # ASP.NET Core host
├── Shared/
│   ├── Axis.Shared.Domain/
│   ├── Axis.Shared.Application/
│   └── Axis.Shared.Infrastructure/
└── Modules/
    ├── Identity/
    ├── DataModeling/
    ├── WorkflowBuilder/
    ├── FormBuilder/
    ├── WorkflowEngine/
    └── PageBuilder/
tests/
├── Shared/
└── Modules/
```
