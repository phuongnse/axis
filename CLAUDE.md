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
- **OpenAPI**: Swashbuckle.AspNetCore 6.9.0 (metadata + Swagger JSON) + Scalar.AspNetCore (UI)
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
- **Hard stops — never do any of the following:**
  - Reference another module's `Infrastructure` project from any layer
  - Query another module's database tables directly — via `DbSet<T>`, `SqlQueryRaw`, `ExecuteSqlRaw`, `FromSqlRaw`, or any other mechanism. **This applies to raw SQL too.** If module A needs data owned by module B, A must maintain its own local copy synced via Wolverine domain events (see `docs/playbooks/patterns.md § Cross-module data pattern`).
  - Share a `DbContext` instance across module boundaries
  - Use `IMediator` to dispatch a domain event (use Wolverine outbox only)

---

## Machine Rules — Quick Reference

Scan this before acting. Full explanations are in the sections below.

**P0 — Hard stops (violating any of these is always wrong):**
- Tech stack is immutable — no substitutions, additions, or removals without explicit user approval
- Never weaken tests, skip assertions, add `.Skip()`, or mock around the behavior under test
- Spec → code only — never retroactively update docs or feature files to justify what the code does
- Never bypass auth, silently skip an AC, or mark ✅ to avoid a difficult conversation
- Domain layer: zero external dependencies (pure C#)
- Modules communicate only via Wolverine events or Application-layer interfaces — no shared DB transactions

**P1 — Architectural (require user confirmation before deviating):**
- Layer order: Domain → Application → Infrastructure → API → Frontend — no skipping
- Result pattern for all business rule violations; exceptions only for infrastructure failures
- CQRS: MediatR for commands/queries only; Wolverine outbox for all domain events
- All Minimal API endpoints must call `.RequireAuthorization()` unless explicitly public

**P2 — Quality gates (verify before every commit):**
- Zero test failures and zero build warnings
- Docs updated in the same PR as code changes
- No TODO/FIXME, placeholder code, or commented-out code introduced

**When blocked:** state exact blocker → list relevant constraints → propose 2–3 options with tradeoffs → wait for user decision. Never self-unblock on a P0 path.

---

## Source of Truth Priority

When sources conflict, resolve in this order — higher wins:

1. **Feature file ACs** — the contract for what to build; never override with code reality
2. **CLAUDE.md** — architectural and process rules; always current
3. **Playbooks** (`docs/playbooks/`) — implementation patterns and how-to detail
4. **Existing implementation** — reference for local conventions, not authority on correctness
5. **Agent preference** — last resort; never invent when a source above answers the question

Existing code is not authoritative if it conflicts with any doc above. Document the conflict and surface it to the user rather than silently following the code.

---

## Required task response structure

For any task that spans multiple files, requires an architectural decision, or implements a new feature layer — begin your response with this structure before writing any code:

1. **Affected module(s)** — which module(s) and layer(s) this touches
2. **Docs to read** — which feature file, playbook, or pattern is authoritative
3. **Key decisions** — the 2–3 architectural choices that will shape the implementation
4. **Plan** — ordered implementation steps
5. **Risks / ambiguities** — anything that could block progress or needs user confirmation first

Skip this structure for single-file edits, simple bug fixes, and doc-only changes.

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

**Step 4 — Map every AC to a concrete implementation step** before writing any code. For each AC in the US being implemented: identify which layer it belongs to, which file/method will implement it, and what the expected behavior is. Any AC that cannot be mapped to a specific implementation → stop and clarify first. This is the step most likely to surface missed requirements before they become bugs.

**Step 5 — Check implementation status** in [`docs/PROGRESS.md`](docs/PROGRESS.md)

**Step 6 — Read [`docs/playbooks/process.md`](docs/playbooks/process.md)** when starting a new module or implementing a new US — it has the step-by-step checklist (layer order, TDD gates, doc update triggers) for both backend and frontend.

**Step 7 — Read [`docs/playbooks/patterns.md`](docs/playbooks/patterns.md)** when the task involves any of: NuGet packages, EF Core mapping or JSONB, Minimal API wiring, writing tests, list/query endpoints, async patterns, response DTOs, repository methods, domain aggregate methods, multi-tenant raw SQL, Wolverine handlers or jobs, new step/field types, cross-cutting concerns, or any design decision about where logic lives.

**Step 8 — Read [`docs/TECH_STACK.md`](docs/TECH_STACK.md)** when making any architectural decision, adding a library, or referencing an ADR.

### Reading priority

When multiple sources are available, prefer in this order:
1. Current feature file — authoritative for what to build
2. Existing implementation in the **same module** — for patterns already established there
3. `docs/playbooks/patterns.md` — canonical patterns and pitfalls
4. Shared abstractions in `Axis.Shared.*` — reuse before creating
5. Other modules — reference only; never copy-paste cross-module logic

Do not read files preemptively. Read the minimum required to complete the task safely. If existing code in the codebase conflicts with CLAUDE.md or a feature spec, treat the docs as source of truth — do not imitate inconsistent legacy patterns.

Avoid re-reading a file you have already processed in the same task — reference your prior findings instead. Re-read only when an inconsistency or ambiguity requires clarification. When you have enough information to act safely, act.

---

## Non-Negotiable Gates

These two rules apply to **every task, every commit, no exceptions**. They are not part of a checklist to skim — skipping either is a hard failure regardless of how complete the implementation looks.

### Gate 1 — Build & tests must be green before committing

Run only what the change touches, but run **all** of it:

| What changed | Command(s) required | Must pass |
|---|---|---|
| `src/` or `tests/` only | `dotnet test unit-tests.slnf` | Zero errors, zero warnings |
| `frontend/` only | `npm run ci` then `npm run test` | Zero errors, zero warnings |
| Both | All of the above | Both gates, no skipping |

A commit that breaks the build or leaves a failing test is **never acceptable**, even as "temporary" or "just to save progress".

### Gate 2 — Docs must be updated in the same task

Every time code changes, the relevant docs change too — in the **same PR, not a follow-up**:

| What you did | What you must update |
|---|---|
| Added, removed, or changed a library | `docs/TECH_STACK.md` — version table + ADR if applicable |
| Established a new implementation pattern | `docs/playbooks/patterns.md` — add the pattern with an example |
| Completed a US layer | Feature file `> **Implementation status**` callout — and remove any mention of that layer from the gap text (✅ and "pending X layer" in the same callout is always a contradiction) |
| Completed a full layer for a module | Epic README status table + `docs/PROGRESS.md` |
| Changed architecture, added a cross-cutting rule | `CLAUDE.md` — the relevant section |
| Changed the implementation workflow or layer order | `docs/playbooks/process.md` — update the affected checklist |
| Changed project structure (added/removed projects) | `ARCHITECTURE.md` source tree + `docs/playbooks/process.md` new module setup table |
| Changed `Program.cs` host/middleware wiring | `docs/playbooks/patterns.md` host setup section — verify the code example still matches |
| Changed or removed a class/method/service referenced in a doc comment | Update the comment in the same file — stale comments are docs debt |
| Replaced a framework or library with another | Every doc that names the old library — search for the old name across `docs/` and `src/` comments |
| Decided a feature is out of scope or deferred | Document the decision explicitly in the affected feature file callout — never silently omit |

**Gate 2 walk-through is mandatory.** Before every commit, go through the table above row by row. For each row, answer explicitly: did any change in this PR trigger this row? If yes, confirm the target doc is already updated in this PR. "I'll update docs later" = the docs are already out of date. Later never comes.

### Docs navigation structure

Every doc file must support bidirectional navigation — a reader arriving from any link must be able to navigate back without using the browser's back button:

- **`docs/README.md`** — links to CLAUDE.md (already present). It is the navigation hub for everything under `docs/`.
- **`docs/playbooks/*.md`** — every playbook must have a `> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)` line directly after the `# Title`.
- **`docs/epics/{module}/README.md`** — must link back to `docs/epics/README.md`.
- **`docs/epics/{module}/features/*.md`** — must link back to the module's epic README.
- **Any new `docs/` subfolder** — its files must link back to `docs/README.md` at minimum.

When creating any new doc file, add the appropriate back-link as the first thing after the `# Title`.

---

## Development Rules

### Process & Workflow
- **Step-by-step workflow**: follow [`docs/playbooks/process.md`](docs/playbooks/process.md) at the start of every new US or module. Rules in this file govern HOW; PROCESS.md governs WHAT order.
- **Language**: all code, docs, commit messages, PR descriptions, and comments must be in English.
- **Git**: never push to `main` — always branch (`{type}/{short-description}` kebab-case, `type` ∈ `feat|fix|docs|refactor|test|chore`) and open a PR. When Claude Code auto-creates a worktree with a random branch name, rename before pushing.
- **Conventional Commits**: `feat: add workflow step handler` — subject ≤ 72 chars, imperative mood, no period.
- **Docs-first for new features**: before implementing any user story or new feature, read the relevant feature file. The doc defines the contract; code implements it. Never write code first and update docs after. For bug fixes, a doc update is only required if the fix reveals a spec deviation. Exception: production hotfixes under active incident — fix first, document the deviation immediately after.
- **Deviation = immediate doc update**: whenever implementation diverges from what is currently documented — different project structure, different library, different startup wiring, different API path convention, removed class or method — the doc update is the very next action, before continuing to the next implementation step. Accumulating "code reality vs doc reality" debt is the primary source of staleness. Close it immediately, never defer.
- **Every command/query maps to a US**: never invent requirements. New requirement discovered → add to docs first, then implement.
- **AC compliance is mandatory**: implement ALL acceptance criteria. Never skip an AC without documenting it as a gap in the `> **Implementation status**` callout.
- **Surface architectural decisions first**: list the 2–3 key design choices and confirm with the user before starting any new layer, module, or API surface.
- **Diagrams**: add proactively when a flow is complex enough that text alone doesn't convey it clearly.
- **CLAUDE.md maintenance**: update whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.

### Testing (shared)

- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions. Applies to both .NET and frontend.
- **Pre-commit verification is scope-based** — run only what the change touches, but run all of it:

| What changed | Command(s) required | Must pass |
|---|---|---|
| `src/` or `tests/` only | `dotnet test unit-tests.slnf` | Zero errors, zero warnings |
| `frontend/` only | `npm run ci` then `npm run test` | Zero errors, zero warnings |
| Both | All of the above | Both gates, no skipping |

**Key .NET rules:** naming `{Subject}_{Condition}_{ExpectedOutcome}`; no `UseInMemoryDatabase`; Testcontainers for all DB tests; run unit tests before every commit; update `tests/Api/Axis.Api.Tests/` when API contracts change.

**Key frontend rules:** Vitest + `@testing-library/react`; test behaviour not implementation; `userEvent` not `fireEvent`; never mock child components unless they have external dependencies.

See [`docs/playbooks/testing.md`](docs/playbooks/testing.md) for full patterns — test isolation, `IAsyncLifetime`, file naming, mocking rules, integration test maintenance.

### Code Style (shared)
- **Comments — WHY only**: default to no comments. Add one only when the WHY is non-obvious — a hidden constraint, a framework quirk, or surprising business logic. No WHAT comments.
- **Scope discipline**: only modify code directly related to the current task. Do not perform opportunistic refactors, cleanups, or "while I'm here" improvements unless explicitly requested. If you notice something worth fixing outside the current scope, flag it to the user — do not silently fix it in the same PR.
- **Violation sweep before fix**: when a task involves fixing an architectural violation (wrong access modifier, incorrect coupling, broken pattern), always grep for all occurrences of the same violation across the codebase before writing any fix. Resolve all instances in the same PR. Fixing only the reported instance while leaving identical violations elsewhere creates inconsistency and false confidence in correctness.
- **Simplest implementation first**: prefer concrete implementations first. Introduce abstractions only when variation, reuse, or lifecycle complexity is proven in the code — not anticipated. Speculative abstractions add maintenance cost without current benefit.
- **No generic abstractions without 2 existing use cases**: do not introduce `BaseRepository`, `GenericService`, `AbstractHandlerFactory`, or similar generalised scaffolding unless at least 2 concrete implementations already exist in the codebase that would benefit from it. One use case is a concrete implementation, not an abstraction.
- **Match existing local conventions**: within a module or file, prefer consistency with the patterns already present unless they directly conflict with CLAUDE.md or an official playbook. Docs override legacy code for correctness; local style overrides AI preference for consistency.
- **Read enough to reason safely**: Grep to locate, then read the minimum complete scope needed to reason about the code — a full function, a full class, or a relevant section. Expand to adjacent sections only if ambiguity remains. Blind truncation mid-logic leads to wrong conclusions; reading the entire repo when a section suffices wastes context.
- **No hardcoded secrets**: use environment-specific config files (gitignored) or environment variables. Never commit credentials or API keys.

#### Code Style — .NET only
- **Rules apply everywhere in .NET**: all rules below apply to both `src/` and `tests/` unless noted.
- **No `var`**: always write the explicit type. For constructor calls, use target-typed new to avoid repeating the type name: `WorkflowDefinition workflow = new(id, name)` not `var workflow = new WorkflowDefinition(id, name)`.
- **No inline fully-qualified type names**: always use `using` directives. Never write `System.Text.Encoding.UTF8`, `System.Security.Cryptography.SHA256`, `Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions`, etc. inline. Run the detection grep in PATTERNS.md § "Code hygiene checklist" before every commit.
- **One type per file**: every class, record, interface, or enum lives in its own file named after the type. Never group multiple types in one file, including test helpers. This applies equally to `src/` and `tests/`. Allowed exceptions:
  - Generic overloads of the same concept may share a file (e.g. `Result` + `Result<T>` in `Result.cs`, `ICommand` + `ICommand<T>` in `ICommand.cs`).
  - An xUnit `[CollectionDefinition]` class may be co-located with its fixture class (xUnit convention requires them to be in the same assembly scope).
  - A large sealed polymorphic hierarchy (5+ subtypes, all `sealed`, all deriving from the same base) may be kept in one file named after the base type (e.g. `FieldConfig.cs`).
  - A simple DTO that exists solely as the return type of a single interface method may be co-located with that interface (e.g. `UserSession` alongside `ISessionStore`).
- **No scaffold placeholder files**: delete `Class1.cs` immediately when Visual Studio creates it. A `Class1.cs` anywhere in `src/` or `tests/` must never be committed.
- **Centralized global usings**: `GlobalUsings.cs` per project or `<Using Include="..." />` in `Directory.Build.props`.
- **`dotnet format`** must pass without warnings before pushing.

#### Code Style — Frontend only
- **Never use `var`**: use `const` by default; use `let` only when reassignment is needed. Never use `var`.
- **TypeScript strict mode**: `strict: true` in tsconfig — enabled. No `any` — use `unknown` + type guards when shape is genuinely unknown. `any` only at external data boundaries and must be typed away immediately.
- **No type assertions without justification**: `as T` requires a comment explaining why the compiler cannot infer it. Never use `as any` — use `as unknown as T` if a double assertion is truly necessary.
- **Type co-location**: small prop interfaces and local type aliases may be co-located with the component that owns them. Shared types belong in a `types.ts` file within the feature folder.
- **Biome is the single tool for linting and formatting** — replaces ESLint + Prettier. Config lives in `frontend/biome.json`. Run `npm run lint:fix` to auto-fix, `npm run format` to format only.
- **`npm run ci` must pass before pushing**: runs `tsc -b --noEmit && biome ci .` — zero TypeScript errors AND zero Biome errors/warnings. This is the frontend equivalent of the .NET build gate.

### Architecture & DDD (.NET only)
- **DDD depth by module**: rich aggregates for complex modules (WorkflowEngine, DataModeling) — full invariants, factory methods, domain events. Pragmatic for simpler CRUD modules (Identity) — still apply Result pattern, repository interfaces, value objects for typed IDs and Email, no Data Annotations on domain types.
- **Aggregate boundary rule**: a class is an `Entity<TId>` within a parent aggregate — NOT a separate `AggregateRoot<TId>` — when all three hold: (1) it cannot exist without the parent, (2) it has no lifecycle independent of the parent, (3) it is only ever accessed via the parent. A FK field back to the owner is a red flag that aggregate boundaries may be wrong. When in doubt, consult patterns.md § "Mismodeled aggregate boundary". Consequences: child entities do NOT raise domain events themselves (the aggregate root raises them); child entities do NOT have `DeletedAt` (cascade covers them); EF Core maps them via `OwnsMany`, NOT a standalone `DbSet<T>`. See patterns.md § "EF Core OwnsMany pattern".
- **Result Pattern**: Command/Query handlers return `Result` or `Result<T>` for business rule violations. `ValidationBehavior` pipeline handles FluentValidation — never throw `ValidationException` manually in a handler. Exceptions are for infrastructure failures only. Aggregates guard with `throw InvalidOperationException` for internal invariants.
- **Domain invariants require spec backing**: every `throw InvalidOperationException` in a domain method must correspond to an explicit AC in the feature file. If no AC exists, do not add the throw — add the AC to the spec first. An invented guard with no spec backing is a hidden spec gap that will silently contradict integration tests. See patterns.md § "Guard Clauses".
- **CQRS & messaging**: MediatR = Commands and Queries only (intra-module). Domain events = Wolverine outbox, regardless of whether the consumer is in the same module or another. Never use MediatR to dispatch domain events.
- **Domain events**: raised inside aggregates via `AddDomainEvent`. Dispatched by `UnitOfWork` via Wolverine outbox after `SaveChangesAsync`. Never call `_messageBus.PublishAsync` for a domain event inside a handler.
- **Response DTOs**: query handlers return dedicated `*Response` / `*Dto` record types defined in the Application layer. Domain entities must never appear in HTTP responses. Mapping happens inside the query handler.
- **Commands that create resources**: return `Result<Guid>` — not the full aggregate.
- **Idempotency**: all Command handlers and Migrations must be idempotent. See PATTERNS.md.

### API Layer (.NET only)
- **Minimal API (mandatory)**: all new endpoints use Minimal API (`MapGroup` + `IEndpointRouteBuilder` extension methods), not controllers. No logic in mapping files — only `mediator.Send(...)` and `Result` → `IResult` mapping. Use `ConfigureHttpJsonOptions` for JSON config.
- **Authorization**: every endpoint must call `.RequireAuthorization()` unless explicitly public (login, register, health check). RBAC enforcement goes in the Command/Query handler via the user's claims — not in the endpoint mapping.
- **OpenAPI**: every endpoint must declare `.WithName()`, `.WithSummary()`, `.WithTags()`, `.Produces<T>()`, `.ProducesProblem()` for each applicable status (400, 401, 403, 404, 409 as relevant). Scalar UI enabled in Development and Staging. See PATTERNS.md for setup.
- **Error responses**: all failures map to `ProblemDetails` (RFC 7807) via `result.ToProblemDetails()`. No custom error JSON shapes or raw strings. See PATTERNS.md for the Result → HTTP status code mapping table.
- **Pagination**: no endpoint returns an unbounded collection. All list endpoints accept `int page = 1`, `int pageSize = 20`, hard cap `pageSize ≤ 100`. Return `PagedResult<T>` from `Axis.Shared.Application`. See PATTERNS.md.

### Infrastructure & EF Core (.NET only)
- **Fluent API only**: use `IEntityTypeConfiguration<T>` for all EF Core mappings. Data Annotations (`[Required]`, `[Table]`, etc.) are forbidden on domain entities.
- **OwnsMany for aggregate-owned entities**: child entities that are part of an aggregate (see aggregate boundary rule above) must be mapped via `OwnsMany` inside the owner's `IEntityTypeConfiguration`. They must NOT have a standalone `DbSet<T>`, a standalone `IEntityTypeConfiguration`, or a repository. Always call `stepBuilder.WithOwner().HasForeignKey(s => s.ParentId)` explicitly to prevent EF from generating a redundant shadow FK column. See PATTERNS.md § "EF Core OwnsMany pattern".
- **JSONB collections**: every `HasConversion` on a `List<T>` stored as JSONB must be paired with `HasValueComparer` in the same call. Converter without comparer = silent data loss. See PATTERNS.md.
- **Read vs write**: `AsNoTracking()` on read-only paths only. Write paths must use tracked queries.
- **Unit of Work**: `SaveChangesAsync` called only via `IUnitOfWork` in the handler, never inside a repository. Repositories only add/query `DbSet<T>`.
- **No `IQueryable` from repositories**: repository methods return materialized types (`T?`, `List<T>`, `PagedResult<T>`).
- **"No DbSet for this table" is a stop signal, not a raw SQL invitation**: if a repository method needs data from a table that has no `DbSet<T>` in the current module's `DbContext`, the correct response is always to stop and ask — never to reach for `SqlQueryRaw`/`ExecuteSqlRaw`. The absence of a `DbSet` means either (a) the data belongs to another module and must be accessed via event-driven local denormalization, or (b) the data is an owned entity accessed via the aggregate root. Raw SQL that names a table not owned by this module is a P0 violation regardless of how it is written. See PATTERNS.md § "Cross-module data pattern".
- **No N+1**: lazy loading disabled globally. Always explicit `Include`/`ThenInclude`. List queries project to DTOs via `.Select()`. See PATTERNS.md.
- **Projection-first for lists**: never materialise entities before projecting for list endpoints — call `.Select(...)` before `.ToListAsync()`, not after. Avoid `Include` chains on list queries; project directly to DTOs instead. Loading full aggregates to map them in memory is forbidden on list paths.
- **NuGet**: check `Directory.Packages.props` before adding any library. Never `dotnet add package` — it corrupts CPM. See PATTERNS.md for the correct procedure.

### Multi-tenancy & Migrations (.NET only)
- **Identity uses `public` schema** — `IdentityDbContext` has no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.
- **No direct `public` schema access** from tenant-aware services.
- **Raw SQL in tenant-aware contexts** must prefix the table with `ITenantContext.Schema` and apply the soft-delete filter manually. Prefer LINQ so global filters apply automatically. See PATTERNS.md.
- **Migration workflow**: `dotnet ef migrations add {PascalCaseName} --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/Axis.Api`. Migrations are **not** applied automatically at startup — run `dotnet ef database update` manually (or via a deploy script) against each environment. All migrations must be idempotent and tested against multiple schemas via Testcontainers before marking ✅.
- **Migration complexity escalation**: any migration that touches tenant schema resolution, cross-module contracts, Wolverine outbox or event persistence schema, or optimistic concurrency columns requires an explicit architectural review with the user before implementation. Do not proceed autonomously — the multi-tenant schema-per-tenant setup makes these changes high-blast-radius.

### Cross-cutting Concerns (.NET only)
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
- **Execution-critical path discipline**: workflow execution paths must favour predictable allocation and query behaviour. Avoid reflection-heavy runtime dispatch (`Activator.CreateInstance`, dynamic type resolution), unbounded metadata queries, or per-step DB round-trips on hot paths unless explicitly benchmarked. Prefer pre-resolved handler registrations and compiled expression trees over runtime reflection.

### Frontend

**Structure:** every feature in `frontend/src/features/{name}/{components/,hooks/,api.ts,types.ts,index.ts}`. Never import cross-feature except via `index.ts`. Shared UI: `src/components/ui/`. Utilities: `src/lib/`.

**Non-negotiable rules:**
- TanStack Query owns all server state; Zustand owns client-only state — never mix
- Three async states required on every data-fetching component: loading (skeleton), empty (message), error (message + retry)
- `react-hook-form` + Zod — Zod schema first, type inferred via `z.infer<typeof schema>`
- TypeScript strict: no `any`, no ungrounded `as T`, entity IDs typed as `string`
- All routes beyond root lazy-loaded; Error Boundaries on every top-level route
- `queryFn`/`mutationFn` in `api.ts` only — never inline in components; components call custom hooks
- Tailwind only — no inline `style` prop, no mixed Tailwind + custom CSS on the same element
- No `dangerouslySetInnerHTML` without DOMPurify; no auth tokens in `localStorage`
- If a component both fetches server data AND has complex conditional rendering, extract the fetch into a custom hook

See [`docs/playbooks/frontend.md`](docs/playbooks/frontend.md) for full rules — TanStack Query patterns, TypeScript discipline, routing, component design, styling, security, accessibility.

### Frontend Development Process

See [`docs/playbooks/process.md`](docs/playbooks/process.md) for the full step-by-step checklists — Phase 1 (Foundation, one-time) and Phase 2 (per-feature, repeatable).

#### Wireframe convention

- **Location**: `docs/epics/{E0N-module-name}/wireframes/{screen-slug}.excalidraw` (source) + `.svg` (rendered preview) — co-located with the epic's `features/` and `diagrams/` folders
- **Naming**: screen slug in kebab-case matching the primary route segment — `login`, `data-models`, `workflow-detail`
- **Shared layout references** (app shell, error pages) that don't belong to any epic go in `docs/wireframes/` root alongside `_template.excalidraw`
- **Format**: Excalidraw JSON (`roughness: 1`, sketch aesthetic) — both files committed; `.excalidraw` is diffable, `.svg` is for quick preview
- **One wireframe per screen** — multiple user stories on the same screen share one wireframe file
- **Generate SVG** after every edit: run `docs/scripts/generate-wireframes.ps1` — scans `docs/wireframes/` (shared/template) and all `docs/epics/*/wireframes/` folders
- **Link from feature file** — add a `> **Wireframe**` callout directly after the feature title, before the first user story:

  ```markdown
  > **Wireframe**: [docs/epics/E02-identity-access/wireframes/login.excalidraw](../wireframes/login.excalidraw) · [preview](../wireframes/login.svg)
  ```

#### Component kit template (`docs/wireframes/_template.excalidraw`)

The template is the single source of truth for all reusable UI patterns. Source lives in `docs/wireframes/generate-template.mjs`. Run `node docs/wireframes/generate-template.mjs` to regenerate `_template.excalidraw`, then run `docs/scripts/generate-wireframes.ps1 -Filter _template` to regenerate `_template.svg`.

For section builder anatomy, `yC` offset rules, element ID prefix conventions, section numbering, compose array structure, and the current section inventory — see [`docs/playbooks/wireframes.md`](docs/playbooks/wireframes.md).

#### Shared component library (`docs/wireframes/components.mjs`)

`components.mjs` is the single source of truth for all primitives, colors, and layout constants. **Both `generate-template.mjs` and `generate-screens.mjs` must import from it — never redefine primitives locally.**

- All primitive builders (`rect`, `ellipse`, `text`, `hline`, `vline`, `arrow`, `sectionHeader`) live here
- `C` color object and layout constants (`SB=230`, `HDR=60`, `CX`, `CY`) live here
- `component(builderFn, targetX, targetY)` — places a template section into a screen coordinate by stripping the section header (2 elements) and translating content to the target position
- `appShell(prefix, W, H, navItems, activeIdx, pageTitle)` — parameterized app shell matching S18 exactly; use this on every authenticated screen
- Convenience builders: `btn`, `inputField`, `selectField`, `badge`, `searchBar`, `pageHeader` — all with canonical dimensions from the template

**Hard rules for `generate-screens.mjs`:**
- Import primitives and helpers from `./components.mjs` — never re-declare `rect`, `text`, `C`, etc.
- Import template builders (`buildWorkflowCanvas`, `buildBuilderLayout`, `buildExecutionTimeline`, `buildModal`, `buildSideSheet`) from `./generate-template.mjs`
- Use `component(buildXxx, x, y)` to place any template section — never recreate it from scratch
- Use `appShell(...)` for the nav/header on every authenticated screen
- `generate-template.mjs` has an `isMain` guard so importing it never triggers side effects

---

## Agent Integrity Rules

These rules exist to prevent a specific failure mode: an agent hitting a blocker, silently working around it, updating docs to justify the deviation, then marking work as done. This has happened before on this project (OpenIddict replaced with custom JWT without user approval). These rules are non-negotiable.

- **Tech stack is immutable without explicit user approval**: the Tech Stack section above is the authoritative list. Never substitute, add, or remove a library — even temporarily or "just to unblock". If a specified library cannot be used, STOP, describe the exact blocker, and wait for the user to decide.
- **Spec → code. Never code → spec**: deviations from a feature file AC, Tech Stack entry, or ADR are gaps to document — never a reason to retroactively update the spec to match what you did.
- **Never work around a failing test**: fix the production code. Never weaken assertions, add `.Skip()`, introduce excessive mocks that bypass the behavior under test, or change what a test verifies to make it green.
- **Tech stack compliance before marking ✅**: verify every library used in that layer appears in the approved Tech Stack. Any deviation → mark ⚠️ and document the gap. Never mark ✅ to avoid a difficult conversation.
- **Architectural decisions require user confirmation**: any decision affecting which library is used, the structure of a cross-cutting concern, or module communication patterns must be confirmed before implementation.
- **"No exceptions, no asking" does not apply to blockers**: the Priority Order governs direction of work only. Technical blockers or architectural ambiguity → always stop and ask.
- **Legacy code is not authoritative**: if existing code conflicts with CLAUDE.md, PATTERNS.md, or a feature spec, the docs win. Do not imitate inconsistent or outdated patterns found in the codebase. If the conflict affects correctness, document it and surface it to the user.
- **Uncertainty protocol**: if you are not confident about a business rule, an architectural constraint, or an existing project pattern — stop and ask rather than guess. Never invent: API endpoint paths, domain event names, DTO field names, database table or column names, or workflow step semantics. Fabricated identifiers silently corrupt the codebase and are hard to detect in review.
- **Verify before claiming correctness**: before stating that a solution is "correct", "best practice", or "done", verify it is consistently applied everywhere it should be. Identify the rule being satisfied, grep for all places that rule applies, confirm no other violations remain. "It works for this case" is not the same as "it is correct."
- **Document what is, not what was planned**: docs must reflect the code that actually exists — not the design intent before implementation. If a planned pattern (e.g., durable outbox, MigrateAsync at startup) was deferred or replaced, update the doc to describe the current reality and note the deferral explicitly. A doc that describes a future state as if it were current is as harmful as a missing doc.

---

## Priority Order

When deciding what to work on next, always follow this order — no exceptions, no asking:
1. **Gaps / bugs / issues** — documented gaps in feature callouts, known correctness bugs, failing tests
2. **Current layer completion** — finish the layer in progress across all modules before starting the next
3. **Next planned layer** — Domain → Application → Infrastructure → API → Frontend

**Mandatory gap sweep before starting API layer** — before writing any API endpoint, run:
```
grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/
```
Every `⚠️` must be resolved (fixed or explicitly documented as deferred with reason) before any API work begins. This is not optional. See `docs/playbooks/process.md` § "Step 4.5 — Gap sweep".

**Always stop and ask when:**
- A Tech Stack library cannot be used for a concrete technical reason
- An AC is ambiguous enough that two reasonable interpretations lead to different implementations
- Completing a task requires an architectural decision not already documented
- A test is failing and fixing it would require deviating from a rule in this file

**Blocked? Follow this protocol — never self-unblock on a P0 path:**
1. State the exact blocker and what you cannot proceed without
2. List the relevant constraints from CLAUDE.md or the feature spec
3. Propose 2–3 valid options (if any exist) with concrete tradeoffs
4. Wait for the user to decide

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
   **Layer status rules:**
   - A layer is marked ✅ only when **every AC** in this US that belongs to that layer is fully implemented — no exceptions.
   - Partial implementation (even one AC unimplemented) must be marked ⚠️, not ✅.
   - Use `⏳` for a layer not yet started, `⚠️` for a layer started but incomplete, `✅` for fully complete.
   - When `Domain + Application` shorthand is used, it means both are ✅; if either is partial, split them: `Domain: ✅ | Application: ⚠️`.
3. ✅ If a gap vs spec exists, document it in the callout — never silently skip.
4. ✅ If this US introduces a new screen (Frontend layer): wireframe exists at `docs/wireframes/{screen-slug}.excalidraw`, SVG generated via `docs/scripts/generate-wireframes.ps1` alongside it, and `> **Wireframe**` callout added to the feature file before the first US. Creating the wireframe is part of the US — not a pre-task or follow-up.

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

### Keeping high-level docs current (applies to every PR)

**Diagrams** (`docs/diagrams/*.excalidraw` + `.svg` and `docs/epics/E0{N}-name/diagrams/*.excalidraw` + `.svg`) — update in the same PR when:
- A new module is introduced or a module's public boundary changes (domain events emitted, API surface added)
- A cross-module flow changes (e.g., how modules communicate via Wolverine events)
- After editing `docs/diagrams/generate-diagrams.mjs`, run `node docs/diagrams/generate-diagrams.mjs` to regenerate `.excalidraw` files, then `docs/scripts/generate-diagrams.ps1` to regenerate `.svg` files
- **Never use PlantUML** — all diagrams are Excalidraw (`.excalidraw` + `.svg`). No `.puml` files exist in this project.

**Wireframes** (`docs/wireframes/*.excalidraw` + `.svg`) — update in the same PR when:
- A new screen is introduced or an existing screen's layout changes
- After editing any `.excalidraw` file, run `docs/scripts/generate-wireframes.ps1` to regenerate the `.svg`

**`CLAUDE.md`** — update in the same PR when:
- A library is added, removed, or version-pinned differently (update Tech Stack section)
- A new architectural rule or layer-order decision is established
- A cross-cutting pattern is standardized (new multi-tenancy behavior, new error-handling convention, etc.)

**`docs/TECH_STACK.md`** — update in the same PR when:
- A library is approved for use or explicitly rejected (add an ADR entry explaining why)
- A library version changes intentionally

**`docs/playbooks/patterns.md`** — update in the same PR when:
- A new implementation pattern is established that future PRs should follow
- A new pitfall or "gotcha" is discovered and solved (so others don't repeat it)

### Pre-mark-done verification

Run through this before marking any task ✅ or raising a PR:

**Code quality**
- Tests pass — `dotnet test unit-tests.slnf` / `npm run test`
- Zero build warnings — `dotnet build` / `npm run ci`
- No TODO or FIXME introduced in this PR
- No placeholder, stub, or dead code committed
- No commented-out code left in

**Gate 2 — Doc correctness (run each check explicitly, not as a batch)**
- Walk the Gate 2 table row by row. For every row whose trigger fired in this PR, confirm the target doc is updated.
- If any library was used: verify it appears in `docs/TECH_STACK.md` AND in the actual package file (`Directory.Packages.props` for .NET, `package.json` for frontend). If the doc says a library is installed but it isn't (or vice versa), fix the discrepancy now.
- If `Program.cs` was touched: open `docs/playbooks/patterns.md` § "Host setup" and verify the code example still matches what's actually in `Program.cs`.
- If any project was added or removed: open `ARCHITECTURE.md` source tree and `docs/playbooks/process.md` new module setup table and verify both still match reality.
- If a class, method, or service was renamed or removed: `grep` for the old name across `docs/` and `src/**/*.cs` comments — update every occurrence.
- Every affected US has an updated `> **Implementation status**` callout.
- Every library used in this layer appears in the approved Tech Stack.

**Gate 3 — Retrospective (mandatory, runs in every PR before the final commit)**

Answer each question explicitly. If the answer is "yes", update the relevant doc in this PR — not later.

1. **Did any test fail for a reason not covered by an existing rule in `CLAUDE.md` or `patterns.md`?**
   If yes → add the rule or pitfall now.

2. **Did I invent a domain invariant (a `throw` in a domain method) that has no AC in the feature file?**
   If yes → either add the AC to the spec first, or remove the throw. Never leave an unbacked guard.

3. **Did I encounter a footgun in the infrastructure setup (connection strings, DI wiring, EF Core config) that surprised me?**
   If yes → add it to `patterns.md` under the relevant pitfall section.

4. **Did the test setup (fixture, containers, seed data) require a non-obvious workaround?**
   If yes → document the pattern or constraint in `testing.md`.

5. **Did I change direction mid-task — switch libraries, revert a commit, or undo an architectural choice?**
   If yes → document the decision and the reason it was wrong in `patterns.md` or an ADR in `TECH_STACK.md`.

6. **Was there a spec gap (missing AC, ambiguous lifecycle, unstated constraint) that only became visible during implementation or CI?**
   If yes → add the missing AC to the feature file now.

This gate exists because process gaps that surface during implementation are the highest-value moment to close them — the problem is fresh, the context is loaded, and the fix takes minutes. The same gap discovered six months later takes hours to reconstruct.

**When any answer above requires updating docs:** write at the principle level, not the incident level. Ask: "Will a reader who has never seen this specific situation understand and apply this rule to analogous future cases?" If not, rewrite it at a higher level of abstraction. Before adding a new section, check whether an existing section can absorb the lesson — extend rather than accumulate.

---

## Layer Order

For any **new** module: Domain → Application (no Docker needed) → Infrastructure (requires Testcontainers) → API → Frontend. Complete each layer fully before starting the next.

---

## Epics & Docs Navigation

- `docs/README.md` — master navigation hub
- `docs/TECH_STACK.md` — approved libraries, versions, and ADRs
- `docs/PROGRESS.md` — current implementation status per module and layer
- `docs/playbooks/process.md` — step-by-step implementation workflow; read at the start of every new US or module
- `docs/playbooks/patterns.md` — implementation patterns and pitfalls; read before any non-trivial implementation
- `docs/playbooks/testing.md` — test isolation, naming, file layout, mocking rules (.NET and frontend)
- `docs/playbooks/frontend.md` — TanStack Query patterns, TypeScript discipline, routing, component design
- `docs/playbooks/wireframes.md` — component kit template rules (section builder anatomy, ID prefixes, offsets)
- `docs/epics/E0{N}-*/README.md` — epic overview + implementation status table
- `docs/epics/E0{N}-*/features/F0{N}-*.md` — feature + user stories with ACs
- `docs/diagrams/` — system-level diagrams (.excalidraw source + .svg preview); regenerate with `node docs/diagrams/generate-diagrams.mjs`
- `docs/epics/E0{N}-name/diagrams/` — epic-level diagrams (.excalidraw + .svg); same generator as above
- `docs/scripts/generate-diagrams.ps1` — regenerates SVGs from all .excalidraw diagram files via Kroki.io POST API
- `docs/wireframes/` — screen wireframes (.excalidraw source + .svg preview)
- `docs/scripts/generate-wireframes.ps1` — regenerates SVGs from .excalidraw via Kroki.io POST API

---

## Solution Structure

```text
frontend/                              # React 18 + TypeScript + Vite SPA
├── src/
│   ├── features/{feature-name}/       # feature-based: components, hooks, types co-located
│   ├── components/ui/                 # shared shadcn/ui components
│   ├── lib/                           # shared utilities (api.ts, utils.ts)
│   └── routes/                        # TanStack Router file-based routes
└── tests/                             # Vitest integration-style tests
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
