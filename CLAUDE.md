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
- Modules communicate ONLY via **asynchronous domain events** (Wolverine) or explicit Application-layer interfaces. 
- **No shared database transactions** across module boundaries.
- Cross-module data consistency is handled via Eventual Consistency.
- Domain layer: zero external dependencies (pure C#, fully unit testable).
- Application layer: depends on Domain only — no infrastructure references.
- Infrastructure: implements interfaces defined in Application/Domain.

## Development Rules
- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions.
- **DDD**: apply fully to complex modules (WorkflowEngine, DataModeling). Be pragmatic on simpler CRUD modules (Identity).
- **Diagrams**: add proactively to user stories or docs when a flow is complex enough that text alone doesn't convey it clearly.
- **Docs-first, always — non-negotiable**: Before implementing any user story, feature, or fix, read the relevant feature file in `docs/epics/`. The doc defines the contract; code implements it. Never write code first and update docs after. If the spec needs clarification or refinement before coding starts, update the doc first. Every code change that affects observable behavior (bug fix, design decision, new constraint, deviation from spec) must also update the relevant doc in the same commit.
- **Every command/query maps to a US**: Never invent requirements. If a new requirement is discovered during implementation, add it to the docs first, then implement.
- **AC compliance is mandatory — no silent skips**: Every US must be implemented to ALL its acceptance criteria. Never defer or skip an AC without explicitly documenting it as a gap in the `> **Implementation status**` callout with the reason. If during implementation or discussion a requirement changes or a new constraint emerges, update the AC in the feature file first, then implement. A US is not done until every AC is either implemented or documented as a gap.
- **A layer cannot be marked ✅ Done if any US in scope is missing its callout**: Before updating a layer status to ✅ in the epic README or CLAUDE.md, verify that every US in that feature set has an `> **Implementation status**` callout. A US with no callout = silently skipped = the layer is not done.
- **Read files in full before making claims**: Never use a line `limit` when the goal is to assert something about a file's content (e.g. "this file has no X"). Read the whole file. Partial reads → wrong conclusions.
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git workflow**: Never push directly to `main` — always create a branch and open a PR. Branch naming must follow `{type}/{short-description}` in kebab-case, where `type` is one of: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`. When Claude Code auto-creates a worktree with a random branch name, rename the branch to follow the convention before pushing.
- **CLAUDE.md maintenance**: update this file whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.
- **API endpoint style — Minimal API (mandatory)**: All new endpoint work uses Minimal API (`MapGroup` + `IEndpointRouteBuilder` extension methods), not traditional controllers. Rationale: cleaner MediatR dispatch (no constructor injection overhead), per-handler DI, less ceremony, .NET 8 first-class support. Never default silently to traditional controllers — surface the choice explicitly if there is genuine ambiguity. Use `ConfigureHttpJsonOptions` for JSON configuration (not `AddControllers().AddJsonOptions(...)`).
- **Explicit over Implicit**: Never use `var` if the type is not crystal clear from the assignment.
- **Minimal API Structure**: Each module must implement an `IMapEndpoints` interface or use a consistent `Map{ModuleName}Endpoints` extension method. No logic in the mapping file—only MediatR dispatching.
- **Idempotency**: All Command handlers and Migrations must be designed to be idempotent to ensure stability in a multi-tenant environment.
- **Surface architectural decisions before implementing**: Before starting any new layer, module, or API surface, list the 2–3 key design choices (endpoint style, auth approach, error format, data access pattern, test strategy) and confirm with the user. Defaulting silently to familiar patterns wastes effort on later migration.
- **No inline fully-qualified type names**: Always use `using` directives at the top of the file. Never write `typeof(Axis.Some.Long.Namespace.MyType)` inline — add `using Axis.Some.Long.Namespace;` and refer to `MyType` directly. Applies everywhere: `typeof(...)`, generic constraints, new expressions, casts. Keeps files readable and imports explicit.
- **Mandatory Result Pattern**: All Application-layer logic and Command/Query handlers must return `Result` or `Result<T>` to signal business rule violations or expected failures. Throwing exceptions is strictly reserved for infrastructure failures (e.g., Database down, Network timeout) or true "exceptional" system states. Domain aggregates may use guards that throw `InvalidOperationException` for internal invariant violations.
- **Centralized Global Usings**: Use a `GlobalUsings.cs` file in each project or define `<Using Include="..." />` in `Directory.Build.props` for ubiquitous namespaces (e.g., `MediatR`, `Axis.Shared.Domain`, `Axis.Shared.Application`). This eliminates boilerplate `using` directives in individual files and keeps the focus on implementation logic.
- **Comment policy — WHY not WHAT**: Default to no comments; well-named identifiers speak for themselves. Add a comment only when the WHY is non-obvious: a hidden constraint, a multi-step flow with a non-obvious invariant, a framework quirk, or business logic that would surprise a reader (e.g. token rotation, JTI blacklisting TTL, admin-count guard). Never describe WHAT the code does — only why it must be done this way.

## Multi-tenancy & Migrations
- **Schema Management**: Every new migration must be tested against multiple schemas using `Testcontainers` before being marked as ✅ Done.
- **Tenant Isolation**: Tenant resolution is a "hard-link" in `AxisDbContext`. Direct access to `public` schema from tenant-aware services is strictly forbidden.

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
   > Gaps vs spec: [ACs not yet covered and why — e.g. "pending auth layer", "requires JWT identity"]
   > Decisions: [design choices that affect how an AC is interpreted or implemented]
   ```
3. ✅ If a gap vs spec is found during implementation, document it in the callout — do not silently skip it

### Completing a layer for a module (Domain, Application, Infrastructure, API)
1. ✅ All tests for that layer passing
2. ✅ Every US in every feature file for that module has an `> **Implementation status**` callout — no US left without one
3. ✅ All US callouts updated to reflect the new layer status
4. ✅ Epic README `Implementation Status` table updated (e.g. `Infrastructure: ✅ Done`)
5. ✅ `Implementation Progress` section in this file (CLAUDE.md) updated

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

### Identity Module (Domain ✅, Application ✅, Infrastructure ✅, API ✅, Frontend ⏳)
**Domain**: Organization, User, Role, Invitation aggregates; Email, OrganizationSlug value objects; all domain events
**Application**: RegisterOrganization, InviteUser, AcceptInvitation, DeactivateUser, AssignRoleToUser, CreateRole, UpdateRole, UpdateUserProfile; AuthenticateUser, VerifyEmail, ResendVerificationEmail, RequestPasswordReset, ResetPassword, ChangePassword, RevokeSession; GetRoles, GetUserSessions queries
**Infrastructure**: IdentityDbContext (public schema), all EF Core configurations, all repositories, BCryptPasswordHasher (work factor 12), MailKitEmailSender, IdentityUnitOfWork, PasswordResetTokenStore, RefreshTokenStore (refresh_tokens table), SessionStoreService
**API**: AuthController (signin/refresh/signout/verify-email/forgot-password/reset-password), OrganizationsController (register + invite), InvitationsController (preview + accept), UsersController (profile/sessions/deactivate/assign-role), RolesController (list/create/update). Custom JWT via JwtTokenService (NOT OpenIddict); PermissionPolicyProvider for fine-grained RBAC; RedisJtiBlacklist; ValidationExceptionMiddleware (422). 27 integration tests (WebApplicationFactory + Testcontainers).

### DataModeling (Domain ✅, Application ✅, Infrastructure ✅, API ✅, Frontend ⏳)
**Infrastructure**: DataModelingDbContext (public), EF Core configurations (DataModel/DataClass/DataRecord), JSONB FieldDefinition converter (polymorphic FieldConfig), JSONB DataRecord._data, 3 repositories (incl. GetPagedAsync with search via `data::text ILIKE`), DataModelingUnitOfWork, integration tests (Testcontainers)
**API**: Minimal API — `/api/models` (9 endpoints: CRUD + field management + reorder), `/api/data-classes` (7 endpoints), `/api/models/{id}/records` (5 endpoints with pagination+search). FieldConfigHelper for discriminated deserialization. Integration tests (WebApplicationFactory).
### WorkflowBuilder (Domain ✅, Application ✅, Infrastructure ✅, API ⏳ (Partial), Frontend ⏳)
**Infrastructure**: WorkflowBuilderDbContext, WorkflowDefinition config (steps/transitions/triggers as JSONB with custom WorkflowStepConverter), WorkflowRepository, 7 integration tests (Testcontainers)
### FormBuilder (Domain ✅, Application ✅, Infrastructure ✅, API ⏳, Frontend ⏳)
**Infrastructure**: FormBuilderDbContext, EF Core config (FormDefinition with fields as JSONB via FormFieldConverter — 9 field types, polymorphic FormFieldConfig), FormRepository (IsReferencedByWorkflowAsync cross-module JSONB query), 8 integration tests (Testcontainers)
### WorkflowEngine (Domain ✅, Application ✅, Infrastructure ✅, API ⏳, Frontend ⏳)
**Infrastructure**: WorkflowEngineDbContext, EF Core config (WorkflowExecution with `_context` as JSONB), ExecutionRepository (4 methods: AddAsync, GetByIdAsync, GetAllAsync, GetByWorkflowAsync), WorkflowDefinitionReader (cross-module raw SQL query on `workflow_definitions.status`), WorkflowEngineUnitOfWork, 8 integration tests (Testcontainers)
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
- **Never hardcode environment configurations in code**: Any configuration values that vary across environments (e.g., connection strings, API URLs, Docker endpoints like `tcp://localhost:2375`, secret keys) must NOT be hardcoded in the source or test code. Use environment variables, `appsettings.json`, or `.testcontainers.properties` instead. Hardcoded values break portability and security.
- **AI Agent Testing Scope**: AI agents must run only unit tests locally using `dotnet test unit-tests.slnf`. Integration tests require Docker/Testcontainers and are verified by CI/CD on PR submission.
- **`unit-tests.slnf`**: Solution filter at the repo root that includes only Domain + Application test projects. When a new unit test project is added to the solution, it must also be added to this file.
