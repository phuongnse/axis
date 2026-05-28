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

This rule is generic, not repository-only: for every implementation touched in a PR (endpoint, command/query handler, gRPC service, consumer, scheduler/job), tests must cover all applicable paths instead of only the primary flow.

| Path class | Minimum expectation |
|---|---|
| **Happy path** | Expected success result/output |
| **Validation / constraints** | Invalid input or business constraint returns the expected failure |
| **Auth / permissions** | Unauthenticated/forbidden paths for protected surfaces |
| **Not found / isolation** | Unknown resource and wrong-tenant/no-data-leak behavior |
| **Dependency failure** | Controlled failure when required downstream dependency is unavailable/fails |

If a path class is not applicable to a specific surface, record it as explicit `N/A` in the AC map / status callout — never omit it implicitly.

### Pre-commit gate

See [agent-checklist.md § Gate 1](./agent-checklist.md) and CLAUDE.md. When `src/` or `tests/` change:

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

`dotnet test` runs the **full** solution (Domain, Application, Infrastructure with Testcontainers, API tests). Docker must be available — same expectation as CI. When adding a test project, add it to `Axis.sln` only (no solution filter).

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

### Tenant isolation tests vs async provisioning tests (platform-foundation tenant isolation/tenant-registration lesson)

Do not couple tenant-isolation API tests to asynchronous provisioning-event timing.

- **Tenant isolation API tests (tenant-isolation)** should verify request-path behavior (`org_id` resolution, schema-scoped reads, 403/404 boundaries) with **deterministic fixture setup**. Provision tenant schemas synchronously in test setup and assert required tenant tables exist before making API calls.
- **Async provisioning workflow tests ([tenant provisioning](../use-cases/platform-foundation/provision-tenant/))** should be a **separate test suite** that targets coordinator/handler behavior (retry scheduling, exhaustion alerts, completion transitions), not a precondition for every API test.
- Shared helpers (e.g., auth helpers used by many endpoint suites) must not wait on eventually-consistent background pipelines with long polling loops. That pattern can create CI-wide flakes/timeouts and hide the real failing concern.

When diagnosing CI failures in this area:

1. Download TRX artifacts and identify the first failing test names.
2. Check for `42P01 relation tenant_*.{table} does not exist` (schema/table readiness issue).
3. Check for timeout exceptions from helper-level provisioning waits (coupling issue).
4. Fix setup determinism first, then verify event-pipeline behavior in its dedicated suite.

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
