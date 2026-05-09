# Axis — Project Context for Claude

## What is Axis
Multi-tenant low-code SaaS platform for building data-driven workflow applications. Users define custom data models, design visual workflows, create forms, and build UI pages — all without writing code.

## Tech Stack
- **Backend**: .NET 8 / ASP.NET Core — Modular Monolith + DDD + CQRS (MediatR)
- **ORM**: Entity Framework Core + Npgsql (PostgreSQL)
- **Background jobs + messaging**: Wolverine (NOT Hangfire)
- **Auth**: Custom JWT via JwtTokenService (NOT OpenIddict)
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

**Step 5 — Read [`docs/PATTERNS.md`](docs/PATTERNS.md)** only if the task involves: adding NuGet packages, EF Core aggregate mapping, Minimal API endpoint wiring, or writing tests.

Do NOT read all docs upfront. The feature file defines the contract for the task at hand.

---

## Development Rules

- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions.
- **Testing Database**: Integration and Infrastructure tests MUST use **Testcontainers** (PostgreSQL/Redis). The EF Core In-Memory database provider (`UseInMemoryDatabase`) is strictly forbidden.
- **DDD**: apply fully to complex modules (WorkflowEngine, DataModeling). Be pragmatic on simpler CRUD modules (Identity).
- **Diagrams**: add proactively when a flow is complex enough that text alone doesn't convey it clearly.
- **Docs-first, always — non-negotiable**: Before implementing any user story, feature, or fix, read the relevant feature file in `docs/epics/`. The doc defines the contract; code implements it. Never write code first and update docs after. Every code change that affects observable behavior must also update the relevant doc in the same commit.
- **Every command/query maps to a US**: Never invent requirements. If a new requirement is discovered during implementation, add it to the docs first, then implement.
- **AC compliance is mandatory — no silent skips**: Every US must be implemented to ALL its acceptance criteria. Never defer or skip an AC without explicitly documenting it as a gap in the `> **Implementation status**` callout. A US is not done until every AC is either implemented or documented as a gap.
- **A layer cannot be marked ✅ Done if any US in scope is missing its callout**: Verify every US in every feature file for that module has an `> **Implementation status**` callout before updating layer status. A US with no callout = silently skipped = the layer is not done.
- **Read files in full before making claims**: Never use a line `limit` when the goal is to assert something about a file's content. Partial reads → wrong conclusions.
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git workflow**: Never push directly to `main` — always create a branch and open a PR. Branch naming: `{type}/{short-description}` in kebab-case, where `type` ∈ `feat | fix | docs | refactor | test | chore`. When Claude Code auto-creates a worktree with a random branch name, rename the branch before pushing.
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
- **Code Style & Linting**: All code must pass `dotnet format` without warnings before pushing. Follow standard C# naming conventions.
- **Complexity Guardrails**: Keep methods small and focused. Minimal API endpoints MUST NOT contain any business logic — they only extract parameters, dispatch to MediatR, and map `Result` objects to `IResult` HTTP responses.
- **CQRS & Messaging Boundaries**: **MediatR** is strictly for internal module CQRS (Commands/Queries). **Wolverine** is strictly for inter-module asynchronous domain events and background jobs. Do not mix their purposes.
- **EF Core Configuration**: Always use Fluent API (`IEntityTypeConfiguration<T>`) for EF Core mappings in the Infrastructure layer. **Data Annotations** (e.g., `[Required]`, `[Table]`) are strictly forbidden in Domain entities.
- **NuGet Packages & Dependencies**: Always check `Directory.Packages.props` before adding any new libraries. Do not add libraries that are not explicitly approved or hallucinate versions. Do not upgrade packages unless explicitly requested.
- **Safe Editing & Refactoring**: Only modify code directly related to the current task. **Never** delete, comment out, or massively restructure code outside the scope of your task unless explicitly requested. Preserve existing logic if you don't fully understand it.

## Multi-tenancy & Migrations

- **Schema Management**: Every new migration must be tested against multiple schemas using Testcontainers before being marked ✅ Done.
- **Tenant Isolation**: Direct access to `public` schema from tenant-aware services is strictly forbidden.
- **Identity uses the global `public` schema** — `IdentityDbContext` has no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.

## Priority order

When deciding what to work on next, always follow this order — no exceptions, no asking:
1. **Gaps / bugs / issues** — documented gaps in feature callouts, known correctness bugs, failing tests
2. **Current layer completion** — finish the layer currently in progress across all modules before starting the next layer
3. **Next planned layer** — follow the established layer order (Domain → Application → Infrastructure → API → Frontend)

Never ask the user which direction to take if the priority order makes it unambiguous.

## Definition of Done

A US or layer is NOT done until all of the following are complete in the same commit:

### Completing a User Story (any layer)
1. ✅ Tests written first and passing
2. ✅ Feature file updated — add/update the `> **Implementation status**` callout directly after the US's *Out of scope* block:
   ```
   > **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅/⏳ | API: ⏳ | Frontend: ⏳
   > Gaps vs spec: [ACs not yet covered and why — omit this line if none]
   > Decisions: [design choices that affect how an AC is interpreted — omit this line if none]
   ```
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
