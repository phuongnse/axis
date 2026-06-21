# Testing Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

> Full testing rules for .NET and frontend. The non-negotiable gates (TDD mandatory, pre-commit scope table) live in AGENTS.md. This playbook covers isolation strategies, naming conventions, file layout, and mocking rules.

---

## Shared

- No `// Arrange / Act / Assert` headers, no WHAT comments — WHY only, same as production code.

---

## .NET testing

### Test naming

`{Subject}_{Condition}_{ExpectedOutcome}` — e.g. `CreateWorkflow_WhenNameIsDuplicate_ReturnsConflictError`.

Every `[Fact]` and `[Theory]` name is mechanically checked by
`python scripts/axis.py check test-naming`; the fast local gate and CI both run it.

### Test isolation

Each integration test class implements `IAsyncLifetime` (one Testcontainer per class). Each test method calls `ResetAsync()` at its start to truncate relevant tables. See [Additional .NET test patterns](#additional-net-test-patterns) for the full `IAsyncLifetime` pattern.

### Database rules

- `UseInMemoryDatabase` is strictly forbidden — it masks real DB behaviour (no FK enforcement, no JSONB, no transactions).
- All database tests use Testcontainers (PostgreSQL / Redis).
- Testcontainers generates ephemeral credentials — never reference external credentials in test code.

### Required test coverage for integration tests

Every repository integration test class must cover **all three paths**, not just the happy path:

| Path | What to test |
|---|---|
| **Happy path** | Entity is created / queried / updated successfully |
| **Not-found / isolation** | Query with wrong `workspaceId` returns `null`; soft-deleted record excluded |
| **Constraint violations** | Duplicate insert (same unique fields) throws `UniqueConstraintException`; wrong FK fails as expected |

When a repository has a unique constraint (e.g. `(workspace_id, name)`), a test **must** attempt to insert a second record with the same unique fields and assert the violation is caught. This prevents the class of bug where the DB index exists but `.IsUnique()` was accidentally omitted.

**Happy-path-only integration tests are not complete** — they must be expanded before the layer is marked ✅.

### Required path coverage (all implementation types)

Generic AC/path coverage requirements are owned by
[`agent-checklist.md` § AC coverage — avoid happy-path-only](./agent-checklist.md#ac-coverage--avoid-happy-path-only)
to keep one source of truth.

Testing playbook responsibility here is implementation technique:
- choose the right test level for each path (unit vs integration),
- keep path assertions deterministic (no timing-race assertions),
- keep behavior assertions explicit enough to map back to the AC row.

### Additional .NET test patterns

- Never run `dotnet test --no-build` after editing test code — always let it recompile.
- **Never hardcode environment configurations**: connection strings, API URLs, Docker endpoints, secret keys must use environment variables, `appsettings.json`, or `.testcontainers.properties`.
- **Pre-push / CI**: local pre-push runs a quick policy/doc sanity gate (`python scripts/axis.py pre-push`) so ordinary pushes stay fast. Before requesting review, run the ready-PR gate (`python scripts/axis.py verify`), including unit test projects via `python scripts/axis.py test unit`. CI runs full `dotnet test Axis.sln`, including Testcontainers integration and API tests.

#### Pattern — keep API isolation tests deterministic, test async provisioning separately

Workspace data-isolation API tests and workspace-provisioning event-pipeline tests have different failure modes and should not share the same precondition mechanism.

- **API isolation tests** (e.g., cross-workspace 404/403 behavior) should use deterministic setup: create/verify workspace schema readiness in fixture code and verify required tables exist before requests.
- **Async provisioning tests** (Kafka/Wolverine coordinator paths) should validate retry/backoff/exhaustion/completion logic in dedicated messaging/infrastructure tests.
- Avoid helper-level long polling for eventual consistency in shared auth/setup helpers; a single timing hiccup can fan out into dozens of unrelated API test failures and CI timeout/cancel behavior.

Use this split whenever a feature combines "eventually consistent provisioning" with "synchronous API authorization/isolation checks."

**Test isolation pattern** — two levels of isolation to understand:

**Level 1 — between test classes (container-per-class):** Each test class gets its own Testcontainers instance via `IAsyncLifetime`. This guarantees no cross-class pollution.

**Level 2 — between tests within the same class:** A fresh container starts empty, so the first test is clean by default. But subsequent tests in the same class accumulate data from previous ones. Handle this with a `ResetAsync()` helper that truncates relevant tables at the start of each test:

```csharp
public class CreateWorkflowTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private WorkflowBuilderDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().Build();
        await _postgres.StartAsync();
        _context = DbContextFactory.Create(_postgres.GetConnectionString());
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // Call at the start of each test method that needs a clean slate
    private async Task ResetAsync()
    {
        await _context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE workflow_definitions CASCADE");
    }

    [Fact]
    public async Task CreateWorkflow_WhenNameIsUnique_Succeeds()
    {
        await ResetAsync();
        // ... arrange, act, assert
    }
}
```

**Rules:**
- Never assume a test runs first — always call `ResetAsync()` at the start of any test that requires a clean state.
- Never rely on data created by a sibling test — each test must arrange its own prerequisites.
- Use `AsNoTracking()` for all read queries in tests to avoid EF Core change tracker interference between assertions.

### Ready-PR gate

See [agent-checklist.md § Verification Gate](./agent-checklist.md#verification-gate--verify-before-pr-review) and AGENTS.md. When `src/` or `tests/` change:

```bash
dotnet build
python scripts/axis.py check test-naming
python scripts/axis.py test unit
dotnet format --verify-no-changes
```

`python scripts/axis.py test unit` discovers committed `*.Domain.Tests` and `*.Application.Tests` projects from `git ls-files`; it does not use a static solution filter. Run `python scripts/axis.py check test-project-classification` when adding a test project; CI fails if a committed `*.Tests.csproj` is not classified. Full `dotnet test Axis.sln --nologo` runs Domain, Application, Infrastructure with Testcontainers, and API tests; Docker must be available for that full local run and CI always runs it before merge.

Full local `dotnet test` requires Docker to be visible to the process running
.NET. Use `docker info` as the check. If Docker is not visible from that process,
use the [local-dev Docker endpoint adapter](./local-dev.md#docker-endpoint-adapter);
do not hardcode Docker endpoints in test code or fixtures.

### Integration test maintenance

Any change affecting API response shape, status codes, or request contract must include updating all relevant files under `tests/Api/Axis.Api.Tests/` in the same PR. "Cannot run locally" is not an excuse.

### ApiTestFixture — module database isolation (ADR-011 + ADR-023)

**Every module that has its own `DbContext` must get its own isolated PostgreSQL database** in `ApiTestFixture` (e.g. `axis_identity_test`, `axis_datamodeling_test`). Never point two modules at the same database.

**Why:** `EnsureCreatedAsync` skips schema creation when *any* user table already exists in the database. **Migrations do not have this heuristic** — use `MigrateAsync` everywhere ([ADR-023](../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)).

**Pattern:** use `PostgresModuleTestDatabase` (`tests/Shared/Axis.Testing`) to create a database per module, set `ConnectionStrings__{Module}` environment variables **before** `WebApplicationFactory` (Wolverine reads them at host build), then `MigrateAsync` each context before starting the host:

```csharp
string identityConn = await PostgresModuleTestDatabase.CreateAsync(adminConn, "axis_identity_test");
Environment.SetEnvironmentVariable("ConnectionStrings__Identity", identityConn);

await using (IdentityDbContext ctx = new(/* UseOpenIddict() */))
    await ctx.Database.MigrateAsync();

await PostgresModuleTestDatabase.MigrateAsync<DataModelingDbContext>(
    dmConn, opts => new DataModelingDbContext(opts, workspaceContext));
```

**Wolverine:** no separate `axis_wolverine_test` database. Per-module `wolverine` schemas are created by `AddResourceSetupOnStartup()` in each module's database when the test host starts ([ADR-012](../TECH_STACK.md#adr-012-per-module-wolverine-schema-in-the-modules-own-database)).

**Rule:** when adding a module to `ApiTestFixture`, add `CreateAsync` + env var + `MigrateAsync` — never `EnsureCreatedAsync`, never a shared Wolverine-only database.

### Keep deterministic tests separate from async-pipeline tests

Do not couple request-path / synchronous tests to the timing of an asynchronous, eventually-consistent pipeline (event handlers, background coordinators, retries).

- **Request-path tests** (id/context resolution, scoped reads, authz/isolation boundaries, status codes) need **deterministic fixture setup**: create the required preconditions synchronously in setup and assert they exist before exercising the API. No waiting on background work.
- **Async-workflow tests** (retry scheduling, exhaustion/alert behavior, completion transitions) belong in a **separate suite** that targets the coordinator/handler directly — not as an implicit precondition of every request-path test.
- **Shared helpers** (e.g. an auth/setup helper used by many suites) must never poll an eventually-consistent pipeline in a long loop. One slow or flaky pipeline then fails or times out every suite that touches the helper, hiding the real failure.

When a suite in this class flakes, fix setup determinism first (is the precondition actually present before the call?), then verify the pipeline behavior in its dedicated suite. See [Additional .NET test patterns](#additional-net-test-patterns) for the technique.

---

## Frontend testing

### Runner and structure

- Vitest + `@testing-library/react`. Run with `npm run test` (or `npx vitest`) inside `frontend/`.
- `describe('ComponentOrHookName', () => { it('should ...', ...) })` — `describe` groups by subject, `it` sentences describe expected behaviour.

### File location

| Type | Location |
|---|---|
| Integration-style | `frontend/tests/` |
| Unit (co-located) | `*.test.ts(x)` alongside the source file |

### File naming

- Always `kebab-case.test.ts(x)` mirroring the source file — e.g. `api.test.ts`, `button.test.tsx`, `vite-config.test.ts`.
- Never camelCase or PascalCase for test files.
- Use hyphens not dots in the base name to bypass vitest's built-in exclude globs — `vite-config.test.ts`, not `vite.config.test.ts`.

### What to test

- Test behaviour, not implementation — assert what users see and interact with, not internal state or component structure.
- If a refactor breaks a test without changing visible behaviour, the test was wrong.

### Browser E2E

- Playwright owns browser-level acceptance paths and local-dev smoke tests. Put these specs under `frontend/e2e/`, name them `*.pw.ts`, and run them with `npm run test:e2e`.
- Keep Vitest for focused component, hook, and UI-state coverage. Do not duplicate a full browser journey in Vitest when a Playwright AT row already owns that path.
- Run browser E2E through Docker: `docker compose --profile e2e build e2e && docker compose --profile e2e run --rm --no-deps e2e`. It runs inside the project E2E image based on the official Playwright image, bakes frontend dependencies at image build time, trusts `.dev-certs/rootCA.pem` through Node and Chromium NSS, and writes reports to the container-local E2E output directory.
- Browser E2E uses HTTPS (`https://localhost:3000` and `https://localhost:5281`) in the canonical compose path. Do not set Playwright `ignoreHTTPSErrors`; fix or trust the local CA instead.
- Host-only Playwright runs are a developer convenience, not the canonical path. Use Docker evidence when closing an E2E AT row.

### Interactions

Use `@testing-library/user-event` for all interactions — not `fireEvent`. `userEvent` simulates real browser event sequences (focus, input, click) more accurately.

### Mocking

Never mock child components unless the child has external dependencies (network, native APIs) that make the test impractical. Testing with real children catches integration bugs; mocking them hides them.

### Pre-push gate

Run `npm run ci` (tsc + Biome) AND `npm run test`. Both must pass with zero errors and zero warnings.
