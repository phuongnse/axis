# Axis — Project Context for Claude

## What is Axis
Multi-tenant low-code SaaS platform for building data-driven workflow applications. Users define custom data models, design visual workflows, create forms, and build UI pages — all without writing code.

## Tech Stack
- **Backend**: .NET 8 / ASP.NET Core — Modular Monolith + DDD + CQRS (MediatR)
- **ORM**: Entity Framework Core + Npgsql (PostgreSQL)
- **Background jobs + messaging**: Wolverine (NOT Hangfire)
- **Auth**: OpenIddict (JWT + refresh tokens, self-hosted)
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
- Modules communicate ONLY via domain events (Wolverine) or defined interfaces — never direct DB joins across modules.
- Domain layer: zero external dependencies (pure C#, fully unit testable).
- Application layer: depends on Domain only — no infrastructure references.
- Infrastructure: implements interfaces defined in Application/Domain.

## Development Rules
- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions.
- **DDD**: apply fully to complex modules (WorkflowEngine, DataModeling). Be pragmatic on simpler CRUD modules (Identity).
- **Diagrams**: add proactively to user stories or docs when a flow is complex enough that text alone doesn't convey it clearly.
- **Docs-first, always — non-negotiable**: Before implementing any user story, feature, or fix, read the relevant feature file in `docs/epics/`. The doc defines the contract; code implements it. Never write code first and update docs after. If the spec needs clarification or refinement before coding starts, update the doc first. Every code change that affects observable behavior (bug fix, design decision, new constraint, deviation from spec) must also update the relevant doc in the same commit.
- **Every command/query maps to a US**: Never invent requirements. If a new requirement is discovered during implementation, add it to the docs first, then implement.
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git workflow**: solo project — always commit directly to `main`. Only create a branch if explicitly asked. When Claude Code auto-creates a worktree with a random branch name, commit the work then fast-forward merge to `main` and delete the branch.
- **CLAUDE.md maintenance**: update this file whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.

## Definition of Done

A US or layer is NOT done until all of the following are complete in the same commit:

### Completing a User Story (any layer)
1. ✅ Tests written first and passing
2. ✅ Feature file updated — add/update the `> **Implementation status**` callout directly after the US's *Out of scope* block:
   ```
   > **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅/⏳ | API: ⏳ | Frontend: ⏳
   > Gaps vs spec: [ACs not yet covered and why — e.g. "pending auth layer", "requires JWT identity"]
   > Decisions: [design choices that affect how an AC is interpreted or implemented]
   ```
3. ✅ If a gap vs spec is found during implementation, document it in the callout — do not silently skip it

### Completing a layer for a module (Domain, Application, Infrastructure, API)
1. ✅ All tests for that layer passing
2. ✅ All US callouts in the relevant feature files updated to reflect the new layer status
3. ✅ Epic README `Implementation Status` table updated (e.g. `Infrastructure: ✅ Done`)
4. ✅ `Implementation Progress` section in this file (CLAUDE.md) updated

### Completing a feature fix or refactor
1. ✅ Tests updated/added and passing
2. ✅ If the fix changes observable behavior: update the affected US callout's Gaps or Decisions line
3. ✅ If the fix closes a documented gap: remove that gap from the callout

## Layer order
Complete Domain → Application for ALL modules before touching Infrastructure. Infrastructure requires Docker (PostgreSQL + Redis via Testcontainers) and is done in one pass after all business logic is proven.

## Implementation Progress
### Shared Kernel ✅
- `Axis.Shared.Domain`: Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Result<T>
- `Axis.Shared.Application`: ICommand/IQuery/ICommandHandler/IQueryHandler, ValidationBehavior, TenantContext/ITenantContext
- `Axis.Shared.Infrastructure`: AxisDbContext, TenantSchemaInterceptor, UnitOfWork, MessageBus

### Identity Module (Domain ✅, Application ✅, Infrastructure ✅, API ⏳, Frontend ⏳)
**Domain**: Organization, User, Role, Invitation aggregates; Email, OrganizationSlug value objects; all domain events
**Application**: RegisterOrganization, InviteUser, AcceptInvitation, DeactivateUser, AssignRoleToUser, CreateRole, UpdateRole, UpdateUserProfile; GetRoles query
**Infrastructure**: IdentityDbContext (public schema), all EF Core configurations, all repositories, BCryptPasswordHasher (work factor 12), MailKitEmailSender, IdentityUnitOfWork

### DataModeling (Domain ✅, Application ✅, Infrastructure ⏳, API ⏳, Frontend ⏳)
### WorkflowBuilder (Domain ✅, Application ✅, Infrastructure ⏳, API ⏳, Frontend ⏳)
### FormBuilder (Domain ✅, Application ✅, Infrastructure ⏳, API ⏳, Frontend ⏳)
### WorkflowEngine (Domain ✅, Application ✅, Infrastructure ⏳, API ⏳, Frontend ⏳)
### PageBuilder (⏳ Phase 2 — not started)

## Epics (MVP = E01–E06, Phase 2 = E07)
All requirements, epics, features, and user stories are in `docs/`.
- `docs/README.md` — master navigation
- `docs/epics/E0{N}-*/README.md` — epic overview
- `docs/epics/E0{N}-*/features/F0{N}-*.md` — feature + user stories with ACs
- `docs/diagrams/` — system-level diagrams (.puml + .png)

## Diagram generation
`docs/scripts/generate-diagrams.ps1` — regenerates PNGs from .puml via Kroki.io POST API.

## Solution structure
```
src/
├── Axis.Api/                          # ASP.NET Core host (sits directly under src/, not in a subfolder)
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

## Key patterns established
- Command/Query files live in `Commands/{CommandName}/` or `Queries/{QueryName}/` subfolders
- Repository interfaces defined in `Application/Repositories/`, service interfaces in `Application/Services/`
- Domain guards throw `InvalidOperationException`; application-layer business rule violations throw `FluentValidation.ValidationException`
- `InternalsVisibleTo` in `AssemblyInfo.cs` used for test helpers on domain aggregates
- `Directory.Packages.props` manages all NuGet versions centrally — never add `Version=` to `<PackageReference>` in .csproj
- `tests/Directory.Build.props` auto-adds FluentAssertions + NSubstitute to all test projects

## NuGet / packaging rules
- **Never use `dotnet add package`** on this repo — it corrupts `Directory.Packages.props` (CPM project). Always edit `Directory.Packages.props` directly.
- **Search NuGet before assuming a package ID** — the NuGet ID often differs from the project name (e.g. `WolverineFx` not `Wolverine`). Run `dotnet package search "<name>"` when unsure.
- **Check transitive dependency versions** after adding any new infrastructure package — do a trial `dotnet build` immediately to catch version conflicts (e.g. WolverineFx 5.x requires EF Core 9.x).
- **`UseInMemoryDatabase` requires `Microsoft.EntityFrameworkCore.InMemory`** — separate package, must be added explicitly to test projects.
- **Non-web test projects needing ASP.NET Core types** — use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, never `<PackageReference Include="Microsoft.AspNetCore.Http" />`.

## Identity module schema strategy
- **Identity uses the global `public` schema** — not a tenant schema. Registration has no tenant context and email uniqueness is platform-wide. `IdentityDbContext` is a plain `DbContext` with no `TenantSchemaInterceptor`.
- All other modules use `AxisDbContext` (which wires in `TenantSchemaInterceptor` to switch schemas per request).

## EF Core aggregate mapping patterns
- **Private backing fields** (`_roleIds`, `_permissions`): use `PrimitiveCollection<List<T>>(fieldName).HasField(fieldName).UsePropertyAccessMode(PropertyAccessMode.Field)` — the type parameter must be the *collection* type, not the element type.
- **No-args EF Core constructor**: when an aggregate's only constructor takes params EF Core can't bind (e.g. `IEnumerable<string>`), add a private no-args constructor: `private Role() : base(default) { Name = null!; }`. Initialize all non-nullable fields to silence CS8618.
- **Migrations strategy**: Infrastructure tests use `context.Database.EnsureCreated()` (fast, no migration files). Production deployments will need EF Core migrations per module — one migration bundle per `DbContext`.

## Testing rules
- Never run `dotnet test --no-build` after editing test code — always let it recompile.
- **Never hardcode Docker endpoint in test code** (e.g. `.WithDockerEndpoint("tcp://localhost:2375")`). Docker host configuration belongs in `%USERPROFILE%\.testcontainers.properties` and `%USERPROFILE%\.wslconfig` — not in source code. Hardcoded values break portability across environments.
- **Testcontainers on WSL2 without Docker Desktop**: configure via user-profile files only — `~/.testcontainers.properties` (set `docker.host`) and `~/.wslconfig` (set `networkingMode`). No code changes needed.
