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

### Pre-commit gate

See CLAUDE.md Gate 1 for the canonical pre-commit command table. Short form: run `dotnet test unit-tests.slnf` before every commit. When adding a new unit test project, also add it to `unit-tests.slnf`. Integration tests (requiring Docker) can be skipped locally unless explicitly instructed.

### Integration test maintenance

Any change affecting API response shape, status codes, or request contract must include updating all relevant files under `tests/Api/Axis.Api.Tests/` in the same PR. "Cannot run locally" is not an excuse.

### ApiTestFixture — module database isolation

**Every module that has its own `DbContext` must get its own isolated database** in `ApiTestFixture`. Never point two modules at the same PostgreSQL database.

**Why:** `EnsureCreatedAsync` is a no-op when the target database already has tables — regardless of which module created them. When two modules share a database and one runs `EnsureCreatedAsync` first, the second module's tables are never created and every test fails with `relation "..." does not exist`.

**Pattern:** use the `CreateModuleDatabaseAsync` helper to provision a dedicated database per module before building the `WebApplicationFactory`, then call `EnsureCreatedAsync` on each module's `DbContext` separately:

```csharp
// ✅ correct — each module gets its own isolated DB
string moduleAConnStr = await CreateModuleDatabaseAsync("axis_modulea_test");
string moduleBConnStr = await CreateModuleDatabaseAsync("axis_moduleb_test");

// Wire each connection string into the host config:
["ConnectionStrings:ModuleA"] = moduleAConnStr,
["ConnectionStrings:ModuleB"] = moduleBConnStr,

// EnsureCreatedAsync for each module independently:
DbContextOptions<ModuleADbContext> aOpts = new DbContextOptionsBuilder<ModuleADbContext>()
    .UseNpgsql(moduleAConnStr).Options;
await using ModuleADbContext aCtx = new(aOpts, new PublicSchemaTenantContext());
await aCtx.Database.EnsureCreatedAsync();
```

Identity is the exception: it uses the host `postgres` database because `IdentityDbContext` targets the global `public` schema — it must never be isolated to a module-specific database.

**Rule:** when adding a new module to `ApiTestFixture`, always call `CreateModuleDatabaseAsync(...)` and wire the returned connection string into the host config — never reuse the root container connection string for a non-Identity module.

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
