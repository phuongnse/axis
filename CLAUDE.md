# Axis — Project Context for Claude

## What is Axis
Multi-tenant low-code SaaS platform for building data-driven workflow applications. Users define custom data models, design visual workflows, create forms, and build UI pages — all without writing code.

## Tech Stack
- **Backend**: .NET 8 / ASP.NET Core — Modular Monolith + DDD + CQRS (MediatR)
- **ORM**: Entity Framework Core 9.x + Npgsql (PostgreSQL)
- **Background jobs + messaging**: Wolverine — handles background jobs AND all domain event dispatch (intra- and inter-module). Not Hangfire.
- **Auth**: OpenIddict 5.x — OAuth2/OIDC server (Authorization Code + PKCE for SPA; Client Credentials for external integrations)
- **Real-time**: ASP.NET Core SignalR
- **Validation**: FluentValidation
- **Logging**: Serilog
- **File storage**: AWS S3 (AWSSDK.S3)
- **Frontend**: React 18 + TypeScript + Vite
- **UI components**: shadcn/ui + Tailwind CSS
- **Workflow canvas**: @xyflow/react (React Flow)
- **Page builder DnD**: dnd-kit
- **Data fetching**: TanStack Query
- **State**: Zustand
- **Database**: PostgreSQL 16 — schema-per-tenant
- **Cache**: Redis 7
- **OpenAPI**: Microsoft.AspNetCore.OpenApi (metadata) + Scalar.AspNetCore (UI)
- **Tests**: xUnit + FluentAssertions + NSubstitute + Testcontainers + Bogus

## Architecture
Modular Monolith. 6 modules, each with Domain / Application / Infrastructure / Api layers:
- **Identity** — auth, users, roles, RBAC
- **DataModeling** — custom models, field types, data classes, record CRUD
- **WorkflowBuilder** — workflow definitions, step config, triggers, branching, parallel
- **FormBuilder** — form definitions, fields, workflow integration, submissions
- **WorkflowEngine** — execution orchestrator, step handlers, error handling, history, retry
- **PageBuilder** — pages, widgets, drag & drop layout, data binding (Phase 2)

Shared Kernel: `Axis.Shared.Domain`, `Axis.Shared.Application`, `Axis.Shared.Infrastructure`

Multi-tenancy: schema-per-tenant in PostgreSQL (`tenant_{org_slug}`). Tenant resolved from JWT `org_id` claim; schema name cached in Redis.

### Module dependency rules
- Modules communicate **only via asynchronous domain events** (Wolverine) or explicit Application-layer interfaces. No shared DB transactions across module boundaries.
- Cross-module consistency via Eventual Consistency.
- **Domain**: zero external dependencies (pure C#). **Application**: depends on Domain only. **Infrastructure**: implements Application/Domain interfaces.

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

**Step 5 — Read [`docs/PATTERNS.md`](docs/PATTERNS.md)** when the task involves any of: NuGet packages, EF Core mapping or JSONB, Minimal API wiring, writing tests, list/query endpoints, async patterns, response DTOs, repository methods, domain aggregate methods, multi-tenant raw SQL, Wolverine handlers or jobs, new step/field types, cross-cutting concerns, or any design decision about where logic lives.

**Step 6 — Read [`docs/TECH_STACK.md`](docs/TECH_STACK.md)** when making any architectural decision, adding a library, or referencing an ADR.

---

## Development Rules

### Process & Workflow
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git**: never push to `main` — always branch (`{type}/{short-description}` kebab-case, `type` ∈ `feat|fix|docs|refactor|test|chore`) and open a PR. When Claude Code auto-creates a worktree with a random branch name, rename before pushing.
- **Conventional Commits**: `feat: add workflow step handler` — subject ≤ 72 chars, imperative mood, no period.
- **Docs-first for new features**: before implementing any user story or new feature, read the relevant feature file. The doc defines the contract; code implements it. Never write code first and update docs after. For bug fixes, a doc update is only required if the fix reveals a spec deviation.
- **Every command/query maps to a US**: never invent requirements. New requirement discovered → add to docs first, then implement.
- **AC compliance is mandatory**: implement ALL acceptance criteria. Never skip an AC without documenting it as a gap in the `> **Implementation status**` callout.
- **Surface architectural decisions first**: list the 2–3 key design choices and confirm with the user before starting any new layer, module, or API surface.
- **Diagrams**: add proactively when a flow is complex enough that text alone doesn't convey it clearly.
- **CLAUDE.md maintenance**: update whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.

### Testing
- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions.
- **Test naming**: `{Subject}_{Condition}_{ExpectedOutcome}` — e.g. `CreateWorkflow_WhenNameIsDuplicate_ReturnsConflictError`.
- **Test isolation**: each integration test class implements `IAsyncLifetime` (container per class). Each test method calls `ResetAsync()` at its start to truncate relevant tables. See PATTERNS.md for the full pattern.
- **No InMemoryDatabase**: `UseInMemoryDatabase` is strictly forbidden for all new tests. All database tests use Testcontainers (PostgreSQL/Redis).
- **Unit tests before every commit**: run `dotnet test unit-tests.slnf`. When adding a new unit test project, add it to this file too. Note: The agent is only required to verify that unit tests pass. Integration tests and other tests requiring third-party dependencies can be skipped unless explicitly instructed otherwise.
- **Integration test maintenance**: any change affecting API response shape, status codes, or request contract must include updating all relevant files under `tests/Api/Axis.Api.Tests/` in the same PR. "Cannot run locally" is not an excuse.

### Code Style
- **Rules apply everywhere**: all rules, conventions, and patterns defined in the project instruction documents apply repository-wide unless explicitly overridden. Do not lower the code quality standard when writing tests.
- **No `var`**: always write the explicit type, even when the assignment makes it obvious.
- **Comments — WHY only**: default to no comments. Add one only when the WHY is non-obvious — a hidden constraint, a framework quirk, or surprising business logic. No WHAT comments.
- **No inline fully-qualified type names**: always use `using` directives. Never write `System.Text.Encoding.UTF8`, `System.Security.Cryptography.SHA256`, `Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions`, etc. inline. Run the detection grep in PATTERNS.md § "Code hygiene checklist" before every commit.
- **One type per file**: every class, record, interface, or enum lives in its own file named after the type. Never group multiple types in one file, including test helpers. This applies equally to `src/` and `tests/`. Allowed exceptions:
  - Generic overloads of the same concept may share a file (e.g. `Result` + `Result<T>` in `Result.cs`, `ICommand` + `ICommand<T>` in `ICommand.cs`).
  - An xUnit `[CollectionDefinition]` class may be co-located with its fixture class (xUnit convention requires them to be in the same assembly scope).
  - A large sealed polymorphic hierarchy (5+ subtypes, all `sealed`, all deriving from the same base) may be kept in one file named after the base type (e.g. `FieldConfig.cs`).
  - A simple DTO that exists solely as the return type of a single interface method may be co-located with that interface (e.g. `UserSession` alongside `ISessionStore`).
- **No scaffold placeholder files**: delete `Class1.cs` immediately when Visual Studio creates it. A `Class1.cs` anywhere in `src/` or `tests/` must never be committed.
- **Centralized global usings**: `GlobalUsings.cs` per project or `<Using Include="..." />` in `Directory.Build.props`.
- **`dotnet format`** must pass without warnings before pushing.
- **Scope discipline**: only modify code directly related to the current task. Never restructure code outside task scope unless explicitly requested.
- **Read in full before asserting**: Grep to locate, then Read without a `limit` to assert content. Partial reads lead to wrong conclusions. Grep-first and read-in-full are complementary, not conflicting.

### Architecture & DDD
- **DDD depth by module**: rich aggregates for complex modules (WorkflowEngine, DataModeling) — full invariants, factory methods, domain events. Pragmatic for simpler CRUD modules (Identity) — still apply Result pattern, repository interfaces, value objects for typed IDs and Email, no Data Annotations on domain types.
- **Result Pattern**: Command/Query handlers return `Result` or `Result<T>` for business rule violations. `ValidationBehavior` pipeline handles FluentValidation — never throw `ValidationException` manually in a handler. Exceptions are for infrastructure failures only. Aggregates guard with `throw InvalidOperationException` for internal invariants.
- **CQRS & messaging**: MediatR = Commands and Queries only (intra-module). Domain events = Wolverine outbox, regardless of whether the consumer is in the same module or another. Never use MediatR to dispatch domain events.
- **Domain events**: raised inside aggregates via `AddDomainEvent`. Dispatched by `UnitOfWork` via Wolverine outbox after `SaveChangesAsync`. Never call `_messageBus.PublishAsync` for a domain event inside a handler.
- **Response DTOs**: query handlers return dedicated `*Response` / `*Dto` record types defined in the Application layer. Domain entities must never appear in HTTP responses. Mapping happens inside the query handler.
- **Commands that create resources**: return `Result<Guid>` — not the full aggregate.
- **Idempotency**: all Command handlers and Migrations must be idempotent. See PATTERNS.md.

### API Layer
- **Minimal API (mandatory)**: all new endpoints use Minimal API (`MapGroup` + `IEndpointRouteBuilder` extension methods), not controllers. No logic in mapping files — only `mediator.Send(...)` and `Result` → `IResult` mapping. Use `ConfigureHttpJsonOptions` for JSON config.
- **Authorization**: every endpoint must call `.RequireAuthorization()` unless explicitly public (login, register, health check). RBAC enforcement goes in the Command/Query handler via the user's claims — not in the endpoint mapping.
- **OpenAPI**: every endpoint must declare `.WithName()`, `.WithSummary()`, `.WithTags()`, `.Produces<T>()`, `.ProducesProblem()` for each applicable status (400, 401, 403, 404, 409 as relevant). Scalar UI enabled in Development and Staging. See PATTERNS.md for setup.
- **Error responses**: all failures map to `ProblemDetails` (RFC 7807) via `result.ToProblemDetails()`. No custom error JSON shapes or raw strings. See PATTERNS.md for the Result → HTTP status code mapping table.
- **Pagination**: no endpoint returns an unbounded collection. All list endpoints accept `int page = 1`, `int pageSize = 20`, hard cap `pageSize ≤ 100`. Return `PagedResult<T>` from `Axis.Shared.Application`. See PATTERNS.md.

### Infrastructure & EF Core
- **Fluent API only**: use `IEntityTypeConfiguration<T>` for all EF Core mappings. Data Annotations (`[Required]`, `[Table]`, etc.) are forbidden on domain entities.
- **JSONB collections**: every `HasConversion` on a `List<T>` stored as JSONB must be paired with `HasValueComparer` in the same call. Converter without comparer = silent data loss. See PATTERNS.md.
- **Read vs write**: `AsNoTracking()` on read-only paths only. Write paths must use tracked queries.
- **Unit of Work**: `SaveChangesAsync` called only via `IUnitOfWork` in the handler, never inside a repository. Repositories only add/query `DbSet<T>`.
- **No `IQueryable` from repositories**: repository methods return materialized types (`T?`, `List<T>`, `PagedResult<T>`).
- **No N+1**: lazy loading disabled globally. Always explicit `Include`/`ThenInclude`. List queries project to DTOs via `.Select()`. See PATTERNS.md.
- **NuGet**: check `Directory.Packages.props` before adding any library. Never `dotnet add package` — it corrupts CPM. See PATTERNS.md for the correct procedure.

### Multi-tenancy & Migrations
- **Identity uses `public` schema** — `IdentityDbContext` has no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.
- **No direct `public` schema access** from tenant-aware services.
- **Raw SQL in tenant-aware contexts** must prefix the table with `ITenantContext.Schema` and apply the soft-delete filter manually. Prefer LINQ so global filters apply automatically. See PATTERNS.md.
- **Migration workflow**: `dotnet ef migrations add {PascalCaseName} --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/Axis.Api`. Applied automatically at startup via `MigrateAsync()`. Never apply manually in production. All migrations must be idempotent and tested against multiple schemas via Testcontainers before marking ✅.

### Cross-cutting Concerns
- **CancellationToken**: all `async` methods in Application and Infrastructure accept and forward `CancellationToken` to every EF Core, repository, and HttpClient call.
- **No sync-over-async**: never `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on a `Task`. Always `await`. Causes thread-pool starvation under ASP.NET Core.
- **Audit fields**: every tenant-owned aggregate declares `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`, `string CreatedBy`. Set in the Application layer via `ICurrentUser` / `ITenantContext` — never inside the domain.
- **Soft delete**: tenant-owned aggregates use `DateTimeOffset? DeletedAt` + global EF Core query filter (`HasQueryFilter(e => e.DeletedAt == null)`). Platform-level `public` schema data uses hard delete.
- **Optimistic concurrency**: aggregates subject to concurrent writes include `uint RowVersion` mapped via `.IsRowVersion()`. Handlers catch `DbUpdateConcurrencyException` and return `Result.Failure`.
- **Logging**: Serilog structured logging only — `Error` for system failures, `Warning` for unexpected handled edge cases, `Information` for critical business milestones. No PII or credentials in logs.
- **Correlation ID**: every request carries `X-Correlation-Id`. Middleware generates a GUID if absent and echoes it on the response. All log entries enriched with `CorrelationId` via `LogContext.PushProperty`.
- **No hardcoded secrets**: use `appsettings.Development.json` (gitignored) or environment variables. Testcontainers generates ephemeral credentials — never reference external credentials in test code.
- **Rate limiting**: required on auth endpoints (`/connect/token`, `/connect/authorize`, password reset) and any unauthenticated input endpoints.
- **CORS**: named policy with explicit origin allowlist. Never `AllowAnyOrigin()` in production. Apply `app.UseCors()` before `app.UseAuthentication()`.
- **Health checks**: `GET /health` (liveness) and `GET /health/ready` (readiness, includes PostgreSQL + Redis checks). Both anonymous, excluded from rate limiting.

### Frontend
- **Folder structure**: feature-based — `src/features/{feature-name}/` with components, hooks, and types co-located. Shared UI in `src/components/ui/`.
- **State**: TanStack Query owns all server state. Zustand owns global client-only state. Never store server data in Zustand; never cache client UI state in TanStack Query.
- **Forms**: `react-hook-form` + Zod. Define the Zod schema first (source of truth), infer TypeScript type via `z.infer<typeof schema>`.
- **No `any`**: TypeScript strict mode on. Use `unknown` + type guards when shape is genuinely unknown.
- **API errors**: all TanStack Query mutations handle errors explicitly — surface via toast or inline message. Use a shared `ApiError` type for typed error responses.
- **Error Boundaries**: wrap every top-level route. Render a user-actionable fallback, never a blank screen.
- **Three async states**: every data-fetching component handles loading (skeleton/spinner), empty (descriptive message), and error (message + retry). Silent empty render is a bug.
- **Component size**: small and single-purpose. Extract hooks for non-trivial logic.

---

## Agent Integrity Rules

These rules exist to prevent a specific failure mode: an agent hitting a blocker, silently working around it, updating docs to justify the deviation, then marking work as done. This has happened before on this project (OpenIddict replaced with custom JWT without user approval). These rules are non-negotiable.

- **Tech stack is immutable without explicit user approval**: the Tech Stack section above is the authoritative list. Never substitute, add, or remove a library — even temporarily or "just to unblock". If a specified library cannot be used, STOP, describe the exact blocker, and wait for the user to decide.
- **Spec → code. Never code → spec**: deviations from a feature file AC, Tech Stack entry, or ADR are gaps to document — never a reason to retroactively update the spec to match what you did.
- **Never work around a failing test**: fix the production code. Never weaken assertions, add `.Skip()`, introduce excessive mocks that bypass the behavior under test, or change what a test verifies to make it green.
- **Tech stack compliance before marking ✅**: verify every library used in that layer appears in the approved Tech Stack. Any deviation → mark ⚠️ and document the gap. Never mark ✅ to avoid a difficult conversation.
- **Architectural decisions require user confirmation**: any decision affecting which library is used, the structure of a cross-cutting concern, or module communication patterns must be confirmed before implementation.
- **"No exceptions, no asking" does not apply to blockers**: the Priority Order governs direction of work only. Technical blockers or architectural ambiguity → always stop and ask.

---

## Priority Order

When deciding what to work on next, always follow this order — no exceptions, no asking:
1. **Gaps / bugs / issues** — documented gaps in feature callouts, known correctness bugs, failing tests
2. **Current layer completion** — finish the layer in progress across all modules before starting the next
3. **Next planned layer** — Domain → Application → Infrastructure → API → Frontend

**Always stop and ask when:**
- A Tech Stack library cannot be used for a concrete technical reason
- An AC is ambiguous enough that two reasonable interpretations lead to different implementations
- Completing a task requires an architectural decision not already documented
- A test is failing and fixing it would require deviating from a rule in this file

---

## Definition of Done

A US or layer is NOT done until all of the following are complete in the **same PR**:

### Completing a User Story (any layer)
1. ✅ Tests written first and passing
2. ✅ Feature file updated — add/update the `> **Implementation status**` callout directly after the US's *Out of scope* block:
   ```
   > **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅/⏳ | API: ⏳ | Frontend: ⏳
   > Gaps vs spec: [ACs not yet covered and why — omit if none]
   > Decisions: [design choices affecting how an AC is interpreted — omit if none]
   ```
   AC checkboxes (`- [ ]`) in feature files are **spec structure only** — do not check them off. Completion is tracked exclusively via the callout above.
3. ✅ If a gap vs spec exists, document it in the callout — never silently skip.

### Completing a layer for a module
1. ✅ All tests for that layer passing
2. ✅ Every US in every feature file for that module has a `> **Implementation status**` callout — a US with no callout = the layer is not done
3. ✅ All callouts updated to reflect the new layer status
4. ✅ Epic README `Implementation Status` table updated
5. ✅ [`docs/PROGRESS.md`](docs/PROGRESS.md) updated — **not** CLAUDE.md

### Completing a fix or refactor
1. ✅ Tests updated/added and passing
2. ✅ If the fix changes observable behavior: update the affected US callout's Gaps or Decisions line
3. ✅ If the fix closes a documented gap: remove that gap from the callout
4. ✅ If the fix resolves any gap documented anywhere in `docs/`, remove that ⚠️ note in the same PR.

---

## Layer Order

For any **new** module: Domain → Application (no Docker needed) → Infrastructure (requires Testcontainers) → API → Frontend. Complete each layer fully before starting the next.

---

## Epics & Docs Navigation

- `docs/README.md` — master navigation
- `docs/TECH_STACK.md` — approved libraries, versions, and ADRs
- `docs/PROGRESS.md` — current implementation status per module and layer
- `docs/PATTERNS.md` — implementation patterns and pitfalls; read before any non-trivial implementation
- `docs/epics/E0{N}-*/README.md` — epic overview + implementation status table
- `docs/epics/E0{N}-*/features/F0{N}-*.md` — feature + user stories with ACs
- `docs/diagrams/` — system-level diagrams (.puml + .png)
- `docs/scripts/generate-diagrams.ps1` — regenerates PNGs from .puml via Kroki.io POST API

---

## Solution Structure

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
