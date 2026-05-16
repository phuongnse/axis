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

**Step 5 — Read [`docs/PROCESS.md`](docs/PROCESS.md)** when starting a new module or implementing a new US — it has the step-by-step checklist (layer order, TDD gates, doc update triggers) for both backend and frontend.

**Step 7 — Read [`docs/PATTERNS.md`](docs/PATTERNS.md)** when the task involves any of: NuGet packages, EF Core mapping or JSONB, Minimal API wiring, writing tests, list/query endpoints, async patterns, response DTOs, repository methods, domain aggregate methods, multi-tenant raw SQL, Wolverine handlers or jobs, new step/field types, cross-cutting concerns, or any design decision about where logic lives.

**Step 8 — Read [`docs/TECH_STACK.md`](docs/TECH_STACK.md)** when making any architectural decision, adding a library, or referencing an ADR.

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
| Established a new implementation pattern | `docs/PATTERNS.md` — add the pattern with an example |
| Completed a US layer | Feature file `> **Implementation status**` callout |
| Completed a full layer for a module | Epic README status table + `docs/PROGRESS.md` |
| Changed architecture, added a cross-cutting rule | `CLAUDE.md` — the relevant section |
| Changed the implementation workflow or layer order | `docs/PROCESS.md` — update the affected checklist |

"I'll update docs later" = the docs are already out of date. Later never comes.

---

## Development Rules

### Process & Workflow
- **Step-by-step workflow**: follow [`docs/PROCESS.md`](docs/PROCESS.md) at the start of every new US or module. Rules in this file govern HOW; PROCESS.md governs WHAT order.
- **Language**: discuss in Vietnamese, write all code and docs in English.
- **Git**: never push to `main` — always branch (`{type}/{short-description}` kebab-case, `type` ∈ `feat|fix|docs|refactor|test|chore`) and open a PR. When Claude Code auto-creates a worktree with a random branch name, rename before pushing.
- **Conventional Commits**: `feat: add workflow step handler` — subject ≤ 72 chars, imperative mood, no period.
- **Docs-first for new features**: before implementing any user story or new feature, read the relevant feature file. The doc defines the contract; code implements it. Never write code first and update docs after. For bug fixes, a doc update is only required if the fix reveals a spec deviation.
- **Every command/query maps to a US**: never invent requirements. New requirement discovered → add to docs first, then implement.
- **AC compliance is mandatory**: implement ALL acceptance criteria. Never skip an AC without documenting it as a gap in the `> **Implementation status**` callout.
- **Surface architectural decisions first**: list the 2–3 key design choices and confirm with the user before starting any new layer, module, or API surface.
- **Diagrams**: add proactively when a flow is complex enough that text alone doesn't convey it clearly.
- **CLAUDE.md maintenance**: update whenever architecture decisions change, new patterns are established, or layer-order rules are clarified.

### Testing (shared)
- **TDD is mandatory**: write tests first, must pass before moving to next step, no exceptions. Applies to both .NET and frontend.
- **Comments — WHY only in tests too**: same rule applies — no `// Arrange / Act / Assert` headers, no WHAT comments.
- **Pre-commit verification is scope-based** — run only what the change touches, but run all of it:
  - Changes only in `src/` or `tests/` → run `dotnet test unit-tests.slnf`. Build must succeed with zero errors and zero warnings.
  - Changes only in `frontend/` → run `npm run ci` (tsc + Biome) AND `npm run test`. Both must pass with zero errors and zero warnings.
  - Changes in both → run both of the above. Neither gate may be skipped.

#### Testing — .NET only
- **Test naming**: `{Subject}_{Condition}_{ExpectedOutcome}` — e.g. `CreateWorkflow_WhenNameIsDuplicate_ReturnsConflictError`.
- **Test isolation**: each integration test class implements `IAsyncLifetime` (container per class). Each test method calls `ResetAsync()` at its start to truncate relevant tables. See PATTERNS.md for the full pattern.
- **No InMemoryDatabase**: `UseInMemoryDatabase` is strictly forbidden for all new tests. All database tests use Testcontainers (PostgreSQL/Redis).
- **Unit tests before every commit**: run `dotnet test unit-tests.slnf`. When adding a new unit test project, add it to this file too. Note: The agent is only required to verify that unit tests pass. Integration tests and other tests requiring third-party dependencies can be skipped unless explicitly instructed otherwise.
- **Integration test maintenance**: any change affecting API response shape, status codes, or request contract must include updating all relevant files under `tests/Api/Axis.Api.Tests/` in the same PR. "Cannot run locally" is not an excuse.

#### Testing — Frontend only
- **Test runner**: Vitest + `@testing-library/react`. Run with `npm run test` (or `npx vitest`) inside `frontend/`.
- **Test structure**: `describe('ComponentOrHookName', () => { it('should ...', ...) })`. Use `describe` to group by subject, `it` sentences describe the expected behavior.
- **Test location**: test files live in `frontend/tests/` for integration-style tests and alongside source files (`*.test.ts(x)`) for unit tests.
- **Test file naming**: use `kebab-case.test.ts(x)` mirroring the source file name (e.g. `api.test.ts`, `button.test.tsx`, `vite-config.test.ts`). Never use camelCase or PascalCase for test files. Avoid names that match vitest's built-in exclude glob (`**/vite.config.*`, `**/vitest.config.*`, etc.) — use hyphens instead of dots to bypass (e.g. `vite-config.test.ts`, not `vite.config.test.ts`).
- **Before every push**: run `npm run ci` (type check + Biome) AND `npm run test`. Both must pass.
- **Test behavior, not implementation**: assert what users see and can interact with — not internal state, not component structure. If a refactor breaks a test without changing visible behavior, the test was wrong.
- **Use `@testing-library/user-event` for interactions** — not `fireEvent`. `userEvent` simulates real browser event sequences (focus, input, click) more accurately.
- **Never mock child components** unless the child has external dependencies that make the test impractical. Testing with real children catches integration bugs; mocking them hides them.

### Code Style (shared)
- **Comments — WHY only**: default to no comments. Add one only when the WHY is non-obvious — a hidden constraint, a framework quirk, or surprising business logic. No WHAT comments.
- **Scope discipline**: only modify code directly related to the current task. Never restructure code outside task scope unless explicitly requested.
- **Read in full before asserting**: Grep to locate, then Read without a `limit` to assert content. Partial reads lead to wrong conclusions. Grep-first and read-in-full are complementary, not conflicting.
- **No hardcoded secrets**: use environment-specific config files (gitignored) or environment variables. Never commit credentials or API keys.

#### Code Style — .NET only
- **Rules apply everywhere in .NET**: all rules below apply to both `src/` and `tests/` unless noted.
- **No `var`**: always write the explicit type, even when the assignment makes it obvious.
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
- **Result Pattern**: Command/Query handlers return `Result` or `Result<T>` for business rule violations. `ValidationBehavior` pipeline handles FluentValidation — never throw `ValidationException` manually in a handler. Exceptions are for infrastructure failures only. Aggregates guard with `throw InvalidOperationException` for internal invariants.
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
- **JSONB collections**: every `HasConversion` on a `List<T>` stored as JSONB must be paired with `HasValueComparer` in the same call. Converter without comparer = silent data loss. See PATTERNS.md.
- **Read vs write**: `AsNoTracking()` on read-only paths only. Write paths must use tracked queries.
- **Unit of Work**: `SaveChangesAsync` called only via `IUnitOfWork` in the handler, never inside a repository. Repositories only add/query `DbSet<T>`.
- **No `IQueryable` from repositories**: repository methods return materialized types (`T?`, `List<T>`, `PagedResult<T>`).
- **No N+1**: lazy loading disabled globally. Always explicit `Include`/`ThenInclude`. List queries project to DTOs via `.Select()`. See PATTERNS.md.
- **NuGet**: check `Directory.Packages.props` before adding any library. Never `dotnet add package` — it corrupts CPM. See PATTERNS.md for the correct procedure.

### Multi-tenancy & Migrations (.NET only)
- **Identity uses `public` schema** — `IdentityDbContext` has no `TenantSchemaInterceptor`. All other modules use `AxisDbContext` with `TenantSchemaInterceptor`.
- **No direct `public` schema access** from tenant-aware services.
- **Raw SQL in tenant-aware contexts** must prefix the table with `ITenantContext.Schema` and apply the soft-delete filter manually. Prefer LINQ so global filters apply automatically. See PATTERNS.md.
- **Migration workflow**: `dotnet ef migrations add {PascalCaseName} --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/Axis.Api`. Applied automatically at startup via `MigrateAsync()`. Never apply manually in production. All migrations must be idempotent and tested against multiple schemas via Testcontainers before marking ✅.

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

### Frontend

#### Feature folder anatomy
Every feature lives under `frontend/src/features/{feature-name}/` with a fixed internal structure:
```text
features/{feature-name}/
├── components/     # React components belonging to this feature
├── hooks/          # custom hooks (useXxx.ts)
├── api.ts          # all query/mutation functions for this feature
├── types.ts        # shared types for this feature
└── index.ts        # barrel export — public API of the feature
```
- Component files: `PascalCase.tsx`. Hook files: `camelCase.ts` with mandatory `use` prefix (`useWorkflows.ts`).
- Never import directly from another feature's `components/` or `hooks/` — only through its `index.ts`.
- Shared UI primitives (shadcn/ui, base-ui): `src/components/ui/`. Shared utilities: `src/lib/`.

#### State management
- **TanStack Query owns all server state**. Zustand owns global client-only state (UI flags, user preferences). Never store server data in Zustand; never cache client UI state in TanStack Query.
- **Forms**: `react-hook-form` + Zod. Define the Zod schema first (source of truth), infer TypeScript type via `z.infer<typeof schema>`.
- **Three async states**: every data-fetching component handles loading (skeleton/spinner), empty (descriptive message), and error (message + retry). Silent empty render is a bug.

#### TanStack Query patterns
- All `queryFn` and `mutationFn` definitions live in `features/{feature}/api.ts`. Never write them inline inside a component.
- Components call custom hooks — never call `useQuery`/`useMutation` directly with a `queryFn` in a component file.
- Each feature defines a **query key factory** to avoid magic strings:
  ```ts
  export const workflowKeys = {
    all: ['workflows'] as const,
    list: (filters: WorkflowFilters) => [...workflowKeys.all, 'list', filters] as const,
    detail: (id: string) => [...workflowKeys.all, 'detail', id] as const,
  }
  ```
- All TanStack Query mutations handle errors explicitly — surface via toast or inline message using the shared `ApiError` type.

#### TypeScript discipline
- **No `any`**: TypeScript strict mode on. Use `unknown` + type guards when shape is genuinely unknown. `any` is only acceptable at external/unknown data boundaries (raw API responses before parsing), and must be typed away immediately.
- **No type assertions without justification**: `as T` requires an inline comment explaining why the compiler cannot infer it — not just `as unknown as T` to silence an error.
- **Entity IDs are `string`**: backend uses Guid serialized as string. Never type an entity ID as `number`.
- **API response types are not transformed in components**: if a different shape is needed, derive it in the hook or a selector — not inline in JSX.

#### Routing
- All routes beyond the root are **lazy-loaded** by default — use TanStack Router's `lazy()` for code splitting.
- **Route protection**: auth guard logic lives in a root layout route (loader or `beforeLoad`), not inside individual page components.
- Global 401 handling (redirect to login) is wired once in `api.ts` / a root query error handler — never duplicated per feature.
- **Error Boundaries**: wrap every top-level route. Render a user-actionable fallback, never a blank screen.

#### Component design
- **Small and single-purpose**: extract hooks for non-trivial logic. If a component does data fetching AND complex rendering, split it.
- **Composition over prop drilling**: use compound components or context for UI that shares state across more than two levels. Avoid prop chains longer than 2 hops.
- No more than ~5 props before reconsidering the component's responsibility.

#### Styling
- **No inline `style` prop** — use Tailwind classes exclusively.
- **`cn()` for conditional classes** — never string concatenation.
- Do not mix Tailwind utility classes and custom CSS on the same element.

#### Security
- **No `dangerouslySetInnerHTML`** unless content is sanitized first (DOMPurify). Requires an explicit comment explaining the source.
- **Environment variables**: public vars use `VITE_` prefix. Never expose secrets via `VITE_` — anything in `VITE_*` is bundled into the client.
- **Do not store auth tokens in `localStorage`** — the backend uses `httpOnly` cookies via `credentials: 'include'` (already set in `fetchApi`).

#### Accessibility baseline
- Every form input must have a `<label>` or `aria-label`.
- Every icon-only button must have `aria-label`.
- Never use color as the sole indicator — error/warning states require text or icon alongside color.

### Frontend Development Process

See [`docs/PROCESS.md`](docs/PROCESS.md) for the full step-by-step checklists — Phase 1 (Foundation, one-time) and Phase 2 (per-feature, repeatable).

#### Wireframe convention

- **Location**: `docs/wireframes/{screen-slug}.excalidraw` (source) + `{screen-slug}.svg` (rendered preview)
- **Naming**: kebab-case matching the primary route segment — `login`, `data-models`, `workflow-detail`
- **Format**: Excalidraw JSON (`roughness: 1`, sketch aesthetic) — both files committed; `.excalidraw` is diffable, `.svg` is for quick preview
- **One wireframe per screen** — multiple user stories on the same screen share one wireframe file
- **Generate SVG** after every edit: run `docs/scripts/generate-wireframes.ps1` — regenerates all `.svg` files from `.excalidraw` source via Kroki.io
- **Link from feature file** — add a `> **Wireframe**` callout directly after the feature title, before the first user story:

  ```markdown
  > **Wireframe**: [docs/wireframes/login.excalidraw](../../wireframes/login.excalidraw) · [preview](../../wireframes/login.svg)
  ```

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

**Diagrams** (`docs/diagrams/*.puml` + `.png`) — update in the same PR when:
- A new module is introduced or a module's public boundary changes (domain events emitted, API surface added)
- A cross-module flow changes (e.g., how modules communicate via Wolverine events)
- After editing any `.puml` file, run `docs/scripts/generate-diagrams.ps1` to regenerate the `.png`

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

**`docs/PATTERNS.md`** — update in the same PR when:
- A new implementation pattern is established that future PRs should follow
- A new pitfall or "gotcha" is discovered and solved (so others don't repeat it)

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
