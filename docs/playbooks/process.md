# Development Process

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

> Step-by-step checklists for backend and frontend. Read this at the start of any new module or user story — before writing any code.

## Contents

- [Backend Process](#backend-process)
  - [New module setup](#new-module-setup-one-time-per-module)
  - [Per use case workflow](#per-use-case-workflow)
- [Frontend Process](#frontend-process)
  - [Phase 1 — Foundation](#phase-1--foundation-one-time)
  - [Per-feature workflow](#per-feature-workflow)

---

## Backend Process

### New module setup (one-time per module)

Complete in order when scaffolding a brand-new module:

| Step | Action |
|---|---|
| 1 | Create three projects: `{Module}.Domain`, `{Module}.Application`, `{Module}.Infrastructure` under `src/Modules/{Module}/` (no per-module `.Api` project — all endpoints live in `src/Axis.Api/Endpoints/`) |
| 2 | Add all three to `Axis.sln` |
| 3 | Wire project references: `Domain` ← `Application` ← `Infrastructure`; `Axis.Api` references `Infrastructure` (and transitively `Application`) |
| 4 | Add `GlobalUsings.cs` to each project; add common usings to `Directory.Build.props` if not already present |
| 5 | Create `AxisDbContext` subclass in Infrastructure with `WorkspaceSchemaInterceptor`; register in DI |
| 6 | Create `IEndpointRouteBuilder` extension class in `src/Axis.Api/Endpoints/{Module}Endpoints.cs`; wire it in `Axis.Api/Program.cs` |
| 7 | Create test projects: `{Module}.Domain.Tests` (unit), `{Module}.Application.Tests` (unit), `{Module}.Infrastructure.Tests` (integration with Testcontainers) |
| 8 | Add test projects to `Axis.sln` |
| 9 | Run `dotnet build` — zero errors before writing any domain code |

### Per use case workflow

Repeat for every user story, in layer order: Domain → Application → Infrastructure → API.
**Never start the next layer until the current layer's tests are green.**

#### Step 1 — Read and align

- Read the use-case file ACs in full
- Check `docs/PROGRESS.md` for current layer status
- Identify 2–3 key design decisions (aggregate boundaries, value objects, event names, query shape)
- **Surface decisions to the user and confirm before writing any code** — see "Surface architectural decisions first" rule in `AGENTS.md`

#### Step 2 — Domain layer (TDD)

1. Write failing unit tests for aggregate behaviour, value object invariants, and domain events
2. Implement: aggregate factory methods, domain methods, value objects, domain events, repository interface
3. Run `dotnet test` (full `Axis.sln`) — must be **zero errors, zero warnings** before proceeding
4. No EF Core, no MediatR, no external dependencies — pure C# only

#### Step 3 — Application layer (TDD)

1. Write failing unit tests for the command/query handler (NSubstitute for repository)
2. Implement: `ICommand` / `IQuery`, handler, `AbstractValidator<T>`, `*Response` / `*Dto` record
3. Run `dotnet test` (full `Axis.sln`) — must be **zero errors, zero warnings** before proceeding
4. Handlers return `Result` / `Result<T>` for business rule violations — never throw

#### Step 4 — Infrastructure layer (Testcontainers)

1. Implement `IEntityTypeConfiguration<T>` — Fluent API only, no Data Annotations
2. Implement repository — `AsNoTracking()` on reads, tracked queries on writes
3. Run `dotnet ef migrations add {PascalCaseName}` — **never hand-write** migration files; use the module Infrastructure project as both `--project` and `--startup-project` when a `*DbContextFactory` exists ([local-dev.md § EF Core migrations](./local-dev.md#ef-core-migrations-dotnet-ef)). `dotnet ef` must resolve from `PATH`; if it is missing, fix the SDK/tool installation before generating migrations.
4. Verify the migration has a paired `.Designer.cs`; run integration tests against Testcontainers PostgreSQL
5. Run `dotnet test` (full `Axis.sln`) — still green

#### Step 4.5 — Gap sweep (mandatory before API layer)

**Run this before starting Step 5 for any module.** Skipping it means carrying hidden debt into the API layer.

```
grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/use-cases/
```

For every `⚠️` found, decide explicitly:

| Verdict | Action |
|---|---|
| Actually done, docs stale | Update callout to ✅ |
| Deferred — depends on a later module (e.g. workflow-engine) | `Gaps vs spec` with `pending E0X` and/or `**Deferred follow-ups:**` per agent-checklist deferred-callout rules (`**Deferred follow-ups:**`) |
| Genuine miss | Fix it before proceeding |

Also check cross-module Application dependencies: list every query or command the upcoming API layer will call from *other* modules' Application layers. If any are missing, add them now.

**Cross-module data access sweep (mandatory before Step 5):**

Run the following and inspect every result:

```bash
grep -rn "SqlQueryRaw\|ExecuteSqlRaw\|FromSqlRaw\|ExecuteSqlInterpolated" src/Modules/ --include="*.cs"
```

For every match: confirm the SQL only references tables owned by that match's own module. Any reference to another module's table is a P0 violation — fix it using the event-driven local denormalization pattern in [cross-module patterns](./cross-module-patterns.md) before continuing.

**Do not start Step 5 until every ⚠️ is resolved or explicitly documented as deferred with a reason.**

#### Step 5 — API layer

1. Add Minimal API endpoint in `src/Axis.Api/Endpoints/{Module}Endpoints.cs` with full OpenAPI annotations (`.WithName`, `.WithSummary`, `.WithTags`, `.Produces<T>`, `.ProducesProblem`)
2. Every endpoint calls `.RequireAuthorization()` unless explicitly public
3. Mapping: `mediator.Send(...)` → `Result` → `result.ToProblemDetails()` — no logic in endpoint
4. Add / update integration tests under `tests/Api/Axis.Api.Tests/`
5. Run `dotnet test` (full `Axis.sln`) — must be **zero errors, zero warnings**

#### Step 6 — Update docs (same PR)

- Large features: if the use case spans multiple PRs, follow [pr-slicing.md](./pr-slicing.md) — each slice updates callouts for what **that** slice ships, defers remaining bullets explicitly, and owns its shared seams.
- Update use-case file `> **Implementation status**` callout for this US
- If all USes in the feature are complete for a layer: update Domain README status table
- If the full layer is done for the module: update `docs/PROGRESS.md`
- If a new pattern was established: add it to the focused owner doc from `docs/playbooks/patterns-index.md`
- If a library was added or changed: update `docs/TECH_STACK.md`

---

## Frontend Process

### Phase 0 — Design system foundation

Before broad UI polish or new visual patterns, follow [design-system.md](./design-system.md). Design-system PRs establish tokens, reusable components, visual QA, and migration rules; they do not migrate unrelated legacy screens.

### Phase 1 — Foundation (one-time)

Complete in order before building any feature screen. Do not skip or reorder.

| Step | What | Done when |
|---|---|---|
| 1 | **Auth flow** — `/login` POSTs credentials to `/connect/login`, then Authorization Code + PKCE via `/connect/authorize` → `/callback` → `/connect/token`; access token in memory, refresh token in httpOnly cookie | Login + callback complete; dashboard reachable |
| 2 | **Route guard** — `_authenticated` layout route with `beforeLoad`; redirects to `/login` when session is absent | Unauthenticated navigation to any protected route → redirected |
| 3 | **Global 401 handling** — `fetchApi` 401 branch navigates to `/login` and calls `queryClient.clear()` | Any expired-session API call redirects without per-feature handling |
| 4 | **App shell** — root authenticated layout with sidebar + header; all protected routes render as `<Outlet />` inside it | Every protected page inherits sidebar + header automatically |

### Per-feature workflow

Repeat for every screen / feature area. **Never skip the wireframe step** — it is part of the US, not a pre-task.

| Step | Action | Output |
|---|---|---|
| 1 | Read use-case file ACs in full and define the user goal, required decision, and minimum useful information | UX contract before visual design |
| 2 | Create/update Penpot design source for the screen | Penpot frame linked from `docs/use-cases/{domain}/{slug}/README.md`; committed preview optional when review needs a stable snapshot |
| 3 | Add row to the use-case `## Wireframes` table | Source/preview linked from spec |
| 4 | Define types from backend contract | `features/{name}/types.ts` |
| 5 | Define API functions + query key factory | `features/{name}/api.ts` |
| 6 | Write tests first (TDD) — Vitest + Testing Library | Failing tests that define expected behaviour |
| 7 | Implement components + hooks | Feature folder anatomy (see `AGENTS.md`) |
| 8 | Wire route — lazy-loaded, nested inside `_authenticated` | New file under `routes/` |
| 9 | Run gates: `npm run ci` + `npm run test` | Both green |
| 10 | Update docs (same PR — see breakdown below) | No stale docs |

**Step 10 — Update docs breakdown:**

- Update use-case file `> **Implementation status**` callout for this US
- If all USes in the feature are complete for Frontend: update Domain README status table
- If the full Frontend layer is done for the module: update `docs/PROGRESS.md`
- If a new frontend pattern was established: add it to `docs/playbooks/frontend.md` and update `patterns-index.md` only if routing changes
- If a library was added or changed: update `docs/TECH_STACK.md`

**UX review before implementation and before PR:**

- Does each visible element help the user understand the screen or complete the next action?
- Is the copy user-facing, specific, and free of internal architecture terms?
- Does the screen lead to a clear next action instead of explaining how the system works?
- If content was removed, did the layout adapt instead of filling space with decorative or redundant text?
- Are public/auth screens free of fake workspace data, fake metrics, workspace names, and operational status?
- Can the primary action be found without reading decorative or explanatory content?
