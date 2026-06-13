# Testing Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

> Full testing rules for .NET and frontend. The non-negotiable gates (TDD mandatory, pre-commit scope table) live in CLAUDE.md. This playbook covers isolation strategies, naming conventions, file layout, and mocking rules.

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

Each integration test class implements `IAsyncLifetime` (one Testcontainer per class). Each test method calls `ResetAsync()` at its start to truncate relevant tables. See `docs/playbooks/patterns.md` for the full `IAsyncLifetime` pattern.

### Database rules

- `UseInMemoryDatabase` is strictly forbidden — it masks real DB behaviour (no FK enforcement, no JSONB, no transactions).
- All database tests use Testcontainers (PostgreSQL / Redis).
- Testcontainers generates ephemeral credentials — never reference external credentials in test code.

### Required test coverage for integration tests

Every repository integration test class must cover **all three paths**, not just the happy path:

| Path | What to test |
|---|---|
| **Happy path** | Entity is created / queried / updated successfully |
| **Not-found / isolation** | Query with wrong `organizationId` returns `null`; soft-deleted record excluded |
| **Constraint violations** | Duplicate insert (same unique fields) throws `UniqueConstraintException`; wrong FK fails as expected |

When a repository has a unique constraint (e.g. `(organization_id, name)`), a test **must** attempt to insert a second record with the same unique fields and assert the violation is caught. This prevents the class of bug where the DB index exists but `.IsUnique()` was accidentally omitted.

**Happy-path-only integration tests are not complete** — they must be expanded before the layer is marked ✅.

### Required path coverage (all implementation types)

Generic AC/path coverage requirements are owned by
[`agent-checklist.md` § AC coverage — avoid happy-path-only](./agent-checklist.md#ac-coverage--avoid-happy-path-only)
to keep one source of truth.

Testing playbook responsibility here is implementation technique:
- choose the right test level for each path (unit vs integration),
- keep path assertions deterministic (no timing-race assertions),
- keep behavior assertions explicit enough to map back to the AC row.

### Ready-PR gate

See [agent-checklist.md § Verification Gate](./agent-checklist.md#verification-gate--verify-before-pr-review) and CLAUDE.md. When `src/` or `tests/` change:

```bash
dotnet build
python scripts/axis.py check test-naming
python scripts/axis.py test unit
dotnet format --verify-no-changes
```

`python scripts/axis.py test unit` discovers committed `*.Domain.Tests` and `*.Application.Tests` projects from `git ls-files`; it does not use a static solution filter. Run `python scripts/axis.py check test-project-classification` when adding a test project; CI fails if a committed `*.Tests.csproj` is not classified. Full `dotnet test Axis.sln --nologo` runs Domain, Application, Infrastructure with Testcontainers, and API tests; Docker must be available for that full local run and CI always runs it before merge.

Use the Docker endpoint already available to the shell running .NET when
`docker info` works. Only set `DOCKER_HOST=tcp://127.0.0.1:2375` when that
shell cannot see Docker but Docker Engine is running inside WSL2 with its TCP
daemon exported; probe that fallback with
`Invoke-WebRequest http://127.0.0.1:2375/_ping`.

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
    dmConn, opts => new DataModelingDbContext(opts, tenantContext));
```

**Wolverine:** no separate `axis_wolverine_test` database. Per-module `wolverine` schemas are created by `AddResourceSetupOnStartup()` in each module's database when the test host starts ([ADR-012](../TECH_STACK.md#adr-012-per-module-wolverine-schema-in-the-modules-own-database)).

**Rule:** when adding a module to `ApiTestFixture`, add `CreateAsync` + env var + `MigrateAsync` — never `EnsureCreatedAsync`, never a shared Wolverine-only database.

### Keep deterministic tests separate from async-pipeline tests

Do not couple request-path / synchronous tests to the timing of an asynchronous, eventually-consistent pipeline (event handlers, background coordinators, retries).

- **Request-path tests** (id/context resolution, scoped reads, authz/isolation boundaries, status codes) need **deterministic fixture setup**: create the required preconditions synchronously in setup and assert they exist before exercising the API. No waiting on background work.
- **Async-workflow tests** (retry scheduling, exhaustion/alert behavior, completion transitions) belong in a **separate suite** that targets the coordinator/handler directly — not as an implicit precondition of every request-path test.
- **Shared helpers** (e.g. an auth/setup helper used by many suites) must never poll an eventually-consistent pipeline in a long loop. One slow or flaky pipeline then fails or times out every suite that touches the helper, hiding the real failure.

When a suite in this class flakes, fix setup determinism first (is the precondition actually present before the call?), then verify the pipeline behavior in its dedicated suite. See [patterns.md § Testing rules](./patterns.md#testing-rules) for the technique.

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

### Interactions

Use `@testing-library/user-event` for all interactions — not `fireEvent`. `userEvent` simulates real browser event sequences (focus, input, click) more accurately.

### Mocking

Never mock child components unless the child has external dependencies (network, native APIs) that make the test impractical. Testing with real children catches integration bugs; mocking them hides them.

### Pre-push gate

Run `npm run ci` (tsc + Biome) AND `npm run test`. Both must pass with zero errors and zero warnings.
