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
- **Docs first**: always read the relevant feature file(s) in `docs/epics/` before implementing. Every command/query must map to a specific US (User Story). Never invent requirements — if it's not in the docs, don't implement it.
- **Layer order**: complete Domain → Application for ALL modules before touching Infrastructure. Infrastructure requires Docker (PostgreSQL + Redis via Testcontainers) and is done in one pass after all business logic is proven.
- **CLAUDE.md maintenance**: update this file whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.
- Language: discuss in Vietnamese, write all code and docs in English.
- **Git workflow**: solo project — always commit directly to `main`. Only create a branch if explicitly asked. When Claude Code auto-creates a worktree with a random branch name, commit the work then fast-forward merge to `main` and delete the branch.

## Implementation Progress
### Shared Kernel ✅
- `Axis.Shared.Domain`: Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Result<T>
- `Axis.Shared.Application`: ICommand/IQuery/ICommandHandler/IQueryHandler, ValidationBehavior, TenantContext/ITenantContext

### Identity Module (Domain ✅, Application ✅, Infrastructure ⏳)
**Domain**: Organization, User, Role, Invitation aggregates; Email, OrganizationSlug value objects; all domain events
**Application**: RegisterOrganization, InviteUser, AcceptInvitation, DeactivateUser, AssignRoleToUser, CreateRole, UpdateRole, UpdateUserProfile; GetRoles query

### DataModeling (Domain ✅, Application ✅, Infrastructure ⏳)
### WorkflowBuilder (Domain ✅, Application ✅, Infrastructure ⏳)
### FormBuilder (Domain ✅, Application ✅, Infrastructure ⏳)
### WorkflowEngine (Domain ✅, Application ✅, Infrastructure ⏳)
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
