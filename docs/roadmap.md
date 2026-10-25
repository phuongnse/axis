# Axis roadmap

## How work is planned

- **Milestones are vertical slices.** Each one ends with a running feature
  covered by automated tests, including at least one end-to-end journey.
- **Rolling wave.** Only the current milestone is broken into issues. The next
  milestone is broken down when the current one is nearly done, using what was
  learned.
- **Issue size.** One issue is one behaviour that fits in a single NexKit
  agent session. The sizing rule and how a `too_large` issue is split are in
  [the life of an issue](delivery.md#the-life-of-an-issue).
- **Docs first.** An issue links to the doc sections it implements. When an
  issue changes agreed behaviour, it updates the docs in the same pull request.
- **Cross-cutting qualities are built in, not added later.** Every milestone
  includes:
  - tenant isolation
  - authorization from M4
  - durable history from M3
  - basic performance sanity for the features it adds

## Milestones

| | Milestone | Status | Done when |
| --- | --- | --- | --- |
| **M0** | Foundation | Done | The solution builds; CI runs build, lint, unit, PostgreSQL integration and Playwright E2E for real; NexKit is installed with those checks. |
| **M1** | Walking skeleton | Done | An entity defined in a JSON file is compiled into a release, its table is created, and a user can list, create and edit records through generic pages, in light and dark mode. Two tenant databases are isolated. |
| **M2** | Data and rules | Done | Expression language, named rules, validation and computed fields, data sources (filters, sorting, paging, relations, aggregates), seed data beyond insert-once, such as updating seeded records, and line items on the purchase request form. |
| **M3** | Processes and tasks | Done | Worker host. Purchase request submit → manager task → finance task above the threshold → approved, returned or rejected. Survives worker restart. One decision per task, one start per submission. Audit trail and sequence numbers. |
| **M4** | Identity and authorization | Done | OIDC login through the BFF (Keycloak in tests), roles, default-deny policies at resource, record, field and action level. Denied-access E2E for the purchase request scenarios. |
| **M5** | Events, triggers and integration | Done | Events, data change triggers, schedules, inbound endpoints, connectors with idempotency and reconciliation, deployment bindings and secrets, extension packages. The approved request creates exactly one purchase order, even when a response is lost. |
| **M6** | Releases and versioning | Done | Immutable releases with full pinning, waiting instances finishing on their old release, schema evolution with migrations, separate database roles for runtime access and schema changes, text resource lifecycle, configuration test scenarios in CI. |
| **M7** | Inspection and operations | Done | The process inspector, with its graph, path, attempts, input and output, and failures, and its widget. Correlated logs, metrics and traces, and health checks of the server and the worker. Operator cancel, retry and resolve for process work and calls. The purchase request sample has an Approvals inspector page for its `operator` role. Carried over from M6 and built: the recompute of stored computed fields ([D33](decisions/d33-recomputing-stored-computed-fields.md)), the start checks on the runtime database role, and event deliveries and schedule triggers in test scenarios. |
| **M8** | Studio and modules | Done | Visual authoring that uses the same resource contracts, a dependency graph and a "where is this used" view, reusable modules, and a second sample application, leave requests (see [D41](decisions/d41-leave-requests-as-the-second-sample-application.md)). Carried over from M7 and built: the tenant health metric of [D37](decisions/d37-health-checks-for-the-server-and-the-worker.md). |
| **M9** | Tenancy and deployment | Done | Tenant directory and provisioning, migrations across tenant databases, packaging for on-premises and cloud installation, backup and restore. Carried over from M8 and built: opening a diagnostic at its path, the Studio reconnect message, and module validations that call rules. |
| **M10** | Publishing from Studio | Done | An author validates, previews on a preview tenant, publishes, activates and rolls back an application from Studio, with a release history, and without the admin CLI. The server runs as more than one replica behind TLS: a shared key ring and forwarded headers. Publish and sign-in write audit records. CI runs static analysis. |
| **M11** | Authoring without JSON | Done | Studio creates an application, blank or from a template, and every resource kind. It has reference pickers, typed editors for pages, process steps and connectors, and an expression editor with type errors as you type. It has an entity designer, a policy matrix, a read-only process graph, and runs test scenarios. A third sample, a service desk (see [D56](decisions/d56-a-service-desk-as-the-third-sample-application.md)), is built only through Studio in a Playwright journey. |
| **M12** | Pages, layouts and more widgets | Done | A page holds a tree of widgets in columns, tabs and sections, and works at phone width. Navigate actions with page parameters exist. New widgets: detail, chart and KPI. Tables get row and toolbar actions, delete included. Forms get conditional visible, required and read-only states, validation as you type, and wizard steps. |
| **M13** | Visual page and form designer | Done | An author builds pages and forms by drag and drop on a canvas that uses the runtime renderers, with a palette, a property panel, data and action binding, and undo. Carried over from M11 and M12 and built: Development and Compose set `Studio:ApplicationsPath` to `artifacts/studio-apps`, a second Playwright journey from the `request-approval` template, the parameter, column, outcome and operation reference marks, the task API refusal of a value for a hidden or read-only field, the `409` for a delete of a record in a running process, navigate expressions that resolve paths, design system previews of the detail, chart and KPI widgets, and delete row actions in the page editor. |
| **M14** | Workflow, automation and notifications | Next | Processes get timer, wait for event and sub-process steps, loops that hold a task, task escalation and deadlines, claim and reassign, and a create record operation. There are scheduled jobs without a subject, delete triggers, and a limit plus dead letters for event delivery. In-app and email notifications exist. Processes are edited in a visual designer. Carried over from M13: a page and a form open on the Design tab, a section moves to another step by a drop on the step switch, and the form designer removes a section or a step. |
| **M15** | Data modeling and data management | Planned | New field types: file (with storage bindings), money and enum labels. Indexes, defaults and ranges. References from child rows, and many-to-many. CSV and spreadsheet import and export under policies. Full-text search, more aggregates and query timeouts. |
| **M16** | Hardening | Planned | Performance SLOs and load tests, the FAPI 2.0 profile, verification against an agreed ASVS level, rate limits and security headers, service accounts for the record and process APIs, tenant binding commands and key rotation. Carried over from M10: CI runs the dependency scan on its triggers and once a week, and restoring a preview tenant's archive is refused. |
| **M17** | Extensibility and operator console | Planned | Custom widgets in the designer palette, a published SDK, modules that hold processes, pages and policies and depend on other modules, packages run out of process, connector OAuth2 and structured payloads, outbound webhooks, and an operator web console. |

M3 is complete. See the closed
[M3 milestone](https://github.com/phuongnse/axis/milestone/3?closed=1) on
GitHub.

M4 is complete. See the closed
[M4 milestone](https://github.com/phuongnse/axis/milestone/4?closed=1) on
GitHub.

M5 is complete. See the closed
[M5 milestone](https://github.com/phuongnse/axis/milestone/5?closed=1) on
GitHub.

M6 is complete. See the closed
[M6 milestone](https://github.com/phuongnse/axis/milestone/6?closed=1) on
GitHub.

M7 is complete. See the closed
[M7 milestone](https://github.com/phuongnse/axis/milestone/7?closed=1) on
GitHub.

M8 is complete. See the closed
[M8 milestone](https://github.com/phuongnse/axis/milestone/8?closed=1) on
GitHub.

M9 is complete. See the closed
[M9 milestone](https://github.com/phuongnse/axis/milestone/9?closed=1) on
GitHub.

M10 is complete. See the closed
[M10 milestone](https://github.com/phuongnse/axis/milestone/10?closed=1) on
GitHub.

M11 is complete. See the closed
[M11 milestone](https://github.com/phuongnse/axis/milestone/11?closed=1) on
GitHub.

M12 is complete. See the closed
[M12 milestone](https://github.com/phuongnse/axis/milestone/12?closed=1) on
GitHub.

M13 is complete. See the closed
[M13 milestone](https://github.com/phuongnse/axis/milestone/13?closed=1) on
GitHub.

M14 is next. See the [milestones](https://github.com/phuongnse/axis/milestones) on GitHub.

Milestones after M4 may be reordered when business needs require it. M0–M4
stay in this order.

## Direction after M9

[D46](decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) records the direction.

- **A general-purpose low-code platform.** An author creates, configures, tests,
  publishes and runs an application through Studio, without editing JSON or
  code.
- **No domain gets platform code.** Samples only prove the platform.
- **Order.** M10 to M17 follow the vertical slice: create an application, then
  its data and UI, logic and workflow, permissions, try it out, validate and
  test, publish and operate. Each ends with an end-to-end journey.
- **Parallel work.** M11 and M12 can run in parallel, and so can M14 and M15.
- **Proving it is general.** From M11 on, each milestone extends a sample of a
  new shape: a service desk, then asset management. Neither needs platform
  code.
- **Planning.** Each milestone is broken into issues when the one before it is nearly done. Each milestone starts with
  docs issues that record its design as decisions, as M9 did.

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
