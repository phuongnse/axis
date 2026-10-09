# Axis roadmap

## How work is planned

- **Milestones are vertical slices.** Each one ends with a running feature
  covered by automated tests, including at least one end-to-end journey.
- **Rolling wave.** Only the current milestone is broken into issues. The next
  milestone is broken down when the current one is nearly done, using what was
  learned.
- **Issue size.** One issue is one behaviour, with 2–5 acceptance criteria that
  a test or command can verify. It must fit in a single NexKit agent session.
  If a NexKit plan reports `too_large`, split the issue into parts and add each
  as a sub-issue of the parent, in order and in the parent's milestone. See
  [delivery.md](delivery.md).
- **Docs first.** An issue links to the doc sections it implements. When an
  issue changes agreed behaviour, it updates the docs in the same pull request.
- **Cross-cutting qualities are built in, not added later.** Every milestone
  includes:
  - tenant isolation
  - authorization once M4 lands
  - durable history once M3 lands
  - basic performance sanity for the features it adds

## Milestones

| | Milestone | Status | Done when |
| --- | --- | --- | --- |
| **M0** | Foundation | Done | The solution builds; CI runs build, lint, unit, PostgreSQL integration and Playwright E2E for real; NexKit is installed with those checks. |
| **M1** | Walking skeleton | Done | An entity defined in a JSON file is compiled into a release, its table is created, and a user can list, create and edit records through generic pages, in light and dark mode. Two tenant databases are isolated. |
| **M2** | Data and rules | Done | Expression language, named rules, validation and computed fields, data sources (filters, sorting, paging, relations, aggregates), seed data beyond insert-once, such as updating seeded records, and line items on the purchase request form. |
| **M3** | Processes and tasks | Next | Worker host. Purchase request submit → manager task → finance task above the threshold → approved, returned or rejected. Survives worker restart. One decision per task, one start per submission. Audit trail and sequence numbers. |
| **M4** | Identity and authorization | Planned | OIDC login through the BFF (Keycloak in tests), roles, default-deny policies at resource, record, field and action level. Denied-access E2E for the purchase request scenarios. |
| **M5** | Events, triggers and integration | Planned | Events, data change triggers, schedules, inbound endpoints, connectors with idempotency and reconciliation, deployment bindings and secrets, extension packages. The approved request creates exactly one purchase order, even when a response is lost. |
| **M6** | Releases and versioning | Planned | Immutable releases with full pinning, waiting instances finishing on their old release, schema evolution with migrations, separate database roles for runtime access and schema changes, text resource lifecycle, configuration test scenarios in CI. |
| **M7** | Inspection and operations | Planned | Process inspector (graph, path, attempts, input and output, failures), correlated logs, metrics and traces, health checks, operator retry, cancel and resolve. |
| **M8** | Studio and modules | Planned | Visual authoring that uses the same resource contracts, a dependency graph and a "where is this used" view, reusable modules, and a second sample application. |
| **M9** | Tenancy and deployment | Planned | Tenant directory and provisioning, migrations across tenant databases, packaging for on-premises and cloud installation, backup and restore. |
| **M10** | Hardening | Planned | Performance SLOs and load tests, FAPI 2.0 profile, security verification against an agreed ASVS level, custom widgets. |

M3 is the next milestone to break down into issues.

Milestones after M4 may be reordered when business needs require it. M0–M4
stay in this order.

## M0 — Foundation

This milestone is done directly, not through NexKit, because NexKit needs
working checks before it can verify anything.

1. Create the .NET 10 solution following the [repository layout](architecture.md#repository-layout).
   Include `Directory.Build.props` (nullable enabled, warnings as errors,
   analyzers on), central package management and an `.editorconfig`.
2. `Axis.Server` serves `/health` and a placeholder SPA page.
3. Create the `web/` Vite + React + TypeScript app with Ant Design and
   ProComponents, oxlint, Prettier and Vitest.
4. Add one integration test project that starts PostgreSQL through
   Testcontainers and runs one real query.
5. Add a Playwright project that starts the server against a PostgreSQL
   container and checks that the placeholder page loads.
6. Write one script per check and document the commands in `AGENTS.md`:
   `build`, `lint`, `test`, `integration`, `e2e`.
7. Add a GitHub Actions CI workflow that runs the same scripts on pull
   requests and on `main`.
8. Install NexKit with those checks, add the agent credential secret, and run
   `nexkit doctor`.

Done when a trivial NexKit issue goes from `/nexkit plan` to a green pull
request.

## M1 — Walking skeleton (done)

This milestone is complete. See the closed
[M1 milestone](https://github.com/phuongnse/axis/milestone/1?closed=1) on
GitHub. The items below are the original plan, kept as a record. Item numbers
gave the order; items in the same row of the dependency list could run in
parallel.

**Dependencies:**

- 1 → 2 → 3 → 4
- 1 → 5
- 3 + 5 → 6
- 6 + 7 → 8 → 9

### 1. Resource file loader with diagnostics

Load an application folder of JSON resources (`application` manifest plus
`entity` resources) and validate each one against its JSON Schema. As built,
the loader handles the `application`, `entity`, `site`, `page`, `text` and
`seed` kinds.

- Valid sample folder loads into typed resource objects.
- Invalid JSON, unknown `kind`, schema violations and duplicate `id` or `name`
  each produce a diagnostic with file, JSON path and code.
- All diagnostics are reported in one pass.

### 2. Entity model and reference validation

Build the entity and field model:

- types: text, integer, decimal, boolean, date, date-time, enum, reference
- constraints: required, max length, unique

Resolve references between entities.

- A reference to an unknown entity, an invalid constraint for a type and
  duplicate field names each fail with a diagnostic.
- The purchase request sample entities compile.

### 3. Release compilation and storage

Compile a loaded application into a release record with a content hash and
store it in the tenant's system tables (EF Core). The release is immutable.

- Compiling the same folder twice gives the same hash and does not create a
  new release.
- Changing a resource gives a new release with a different hash.
- A release cannot be modified after it is stored.

### 4. Entity table provisioning

Plan and apply PostgreSQL tables for a release's entities: columns, NOT NULL,
unique constraints and foreign keys. Only additive changes are allowed.

- A new release with an added field adds the column; existing rows are kept.
- Changing a label does not change storage.
- Removing a field or changing its type is rejected with a diagnostic before
  anything is applied.
- Tested against real PostgreSQL.

### 5. Tenant context and connection isolation

Resolve `TenantContext` from the request host using tenants listed in server
configuration. A connection factory returns connections only for the current
tenant's database.

- Requests to two tenant hosts read and write separate databases.
- A request with an unknown host returns 404.
- Data access without a tenant context throws.
- Integration test with two databases.

### 6. Generic record API

`/api/apps/{app}/entities/{entity}/records` supports list (with paging),
get, create, update and delete, driven by the active release metadata.

- Validation failures return RFC 9457 problem details with field paths.
- Updates use optimistic concurrency, and a stale version returns 409.
- An unknown entity or field returns 404 or 400, never a SQL error.
- Identifiers cannot be injected.

### 7. SPA shell, theme and text resources

The app layout has navigation built from site metadata and a light/dark
toggle based on shared tokens. Text resources are loaded from the server,
with a fallback locale and a visible missing-key marker.

- Both themes render the shell.
- Switching locale changes the text.
- A missing key shows its key name in development builds.

### 8. Table and form widgets

Render pages from page and widget metadata, with `table` and `form` widgets
over entities, using ProComponents.

- The list page supports paging and sorting.
- The form page supports create and edit, with server validation errors shown
  on the matching fields.
- Reference fields use a lookup.

### 9. Purchase request skeleton E2E

Add `samples/apps/purchase-requests` with the department, supplier and
purchase request entities (no line items, no process yet), plus seed
departments and suppliers. The sample is compiled and activated on startup in
development.

A Playwright test creates a request, sees it in the list, edits it and checks
that it persists after reload, in both themes.
