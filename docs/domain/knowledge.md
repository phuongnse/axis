# Design knowledge

This document explains what configured business applications need in practice
and which design mistakes Axis must avoid. The [decisions](../decisions.md)
and [concepts](concepts.md) build on it. Each rule gives its reason, so that it
can be weighed against new requirements instead of being followed blindly.

## Typical application size

A mid-sized production application contains roughly:

| Resource | Typical count |
| --- | --- |
| Entities | 30–90 |
| Fields across all entities | 800–2,000 |
| Data sources | 150–250 |
| Process definitions | 400–700 |
| Reusable rules | 250–300 |
| Text keys (per locale) | 1,500–3,000 |
| Pages | 60–130 |
| Indexes | 100–400 |
| Locales | 2–3 |

Consequences:

- Authoring needs search, a dependency graph, "where is this used" and fast
  incremental validation. Scrolling through lists does not work at this size.
- Compiling a full release must stay fast (target: seconds) and give
  diagnostics for all problems at once.
- Many process definitions are small and composed from each other.
  Sub-processes and reusable rules are core features, not extras.
- Applications typically have 2–3 locales. Translations drift unless missing
  keys are detected automatically.

## What applications use most

Listed from most to least used:

1. **Data reads and writes from logic.** Load, search, create, update, count.
   This is the most frequent operation in application logic by far.
2. **Named reusable rules.** Applications call shared rules thousands of
   times. Without them, logic gets copied.
3. **Forms with conditional behaviour.** Fields that are visible, required or
   read-only under a condition; validation on submit and as the user types;
   file upload; lookups; child collections.
4. **Composition.** Processes call sub-processes a lot, often hundreds of
   calls per application.
5. **Custom events.** Processes raise business events and other processes
   react to them, both synchronously and asynchronously.
6. **Inbound endpoints.** Typically 25–50 per application. They are used by
   external systems and as the backend for custom UI.
7. **Data change triggers.** React to a record or field change.
8. **Schedules.** Daily, hourly or every few minutes, in the application's
   time zone, often fanning out over the records matching a query.
9. **Platform utilities.** Sequence numbers, distributed locks, caching,
   spreadsheet export, documents generated from templates, email and other
   notifications, file storage and full-text search.
10. **Integrations.** Several external systems per application: payment,
    national registries, document archives, file transfer and messaging.

These capabilities belong in the platform as built-in operations or as
reusable modules (see [concepts](concepts.md)). Otherwise every application
rebuilds them.

## Logic in configuration

**Failure mode.** A platform lets authors embed general-purpose
code in configuration: scripts in rules, process steps and computed fields.
Within a few years an application contains 50,000–100,000+ lines of such code.
The result is:

- **Duplication.** Dozens of identical blocks of 15+ lines appear across
  process definitions.
- **Oversized scripts.** Single scripts reach 500–1,700 lines, for example
  file parsers, seed data or validation.
- **No unit tests and no real code review.** Errors appear only at run time.
- **Hidden side effects.** The engine cannot know what a script does, so it
  cannot guarantee retries, idempotency or authorization.
- **Security exposure.** Scripts run with full trust, and blocking dangerous
  APIs with a denylist does not work.

**Axis rules:**

- Configuration uses a typed expression language with no side effects (D6).
  Expressions cover validation, computed values, conditions, routing and
  policy filters.
- Reusable logic is a named rule with typed parameters, not copied code.
- Anything with side effects or real algorithmic content is an operation.
  Built-in operations cover common needs. Everything else is written in an
  extension package as tested C# with declared capabilities (D7).
- Large seed data is a seed data resource, not code.

## Execution

**Failure modes:**

- **Unpinned versions.** Running instances load the *current* definition, so
  publishing a change alters work that is already in flight.
- **Pseudo-transactions.** Changes are buffered in memory and flushed as
  several independent writes at the end. A crash during the flush leaves
  partial data.
- **Engine on the request thread.** The process engine runs inside the HTTP
  request. Long work blocks requests, and background work depends on polling
  loops inside the web process that call back into the same process over
  HTTP.
- **No branching.** There is no real parallel branching, and conditions are
  scattered across individual steps.
- **Hand-rolled retries.** Retry windows and locks are written by hand in
  scripts. Long comments explain race conditions with webhooks.

**Axis rules:**

- **Release pinning.** An instance is pinned to its release, including
  process, operation, form and text versions. A new release affects only new
  starts.
- **One transaction per step.** Business writes, process progress, the
  idempotency receipt, audit records and the next work items commit together
  in one PostgreSQL transaction.
- **External calls outside transactions.** A call is staged as durable work
  with a stable operation identity. Retries reuse that identity. An unknown
  outcome stays visible until it is reconciled, and the engine never retries
  it blindly.
- **Worker execution.** Background work runs in a worker host that claims work
  with leases and fencing. Nothing calls back into its own host over HTTP.
- **Platform retries.** Retry, backoff, alerting and manual retry are declared
  per step and implemented once by the engine.
- **Early events.** Events that arrive before a process waits for them are
  stored and matched later, not dropped.

## Human work

**Failure mode.** When the platform has no task concept, every
application models assignment, queues and "my tasks" with ordinary records,
grids and forms. The result is inconsistent and hard to secure.

**Axis rule:** the human task is a first-class step. It has:

- an assignee: a user, a role or a queue
- a pinned form
- a due date
- allowed outcomes
- a guarantee of exactly one final decision
- authorization checked at the moment of action

The platform supplies a task inbox widget.

## Authorization

**Failure modes:**

- **Combined filters fail open.** Record filters are combined with OR. A
  filter whose condition evaluates to false contributes "match everything",
  so one mistaken rule exposes all records.
- **Client-side checks treated as security.** Rules can run on the client and
  on the server, and authors sometimes rely on the client-side check.
- **Field rules exist but are unused.** Field-level read rules are available
  but almost never configured, so sensitive fields are protected only by
  hiding them in the UI.

**Axis rules:**

- **Default-deny.** No matching grant means no access. A failing or
  undecidable policy also means no access (D12).
- **Explicit combination.** Combining several policies is specified exactly,
  and a test proves that a condition evaluating to false cannot widen access.
- **Server enforcement.** Client-side conditions only shape the UI. The server
  re-checks every read, write, action, export and inspection.
- **Practical field security.** Field-level policies are simple to declare.
  Sensitive field flags (masked, encrypted) are part of the entity definition.

## Authoring and packaging

**Failure modes:**

- **Replace-all edits.** Authoring APIs replace the whole resource on every
  edit, so a field the caller omitted is silently cleared.
- **Implicit relationships.** Processes and data are linked indirectly, by
  matching which entities they mention, instead of through explicit
  references.
- **Hidden dependencies.** Applications depend on shared modules that are not
  part of their export. Missing rules are found only at run time.
- **Inconsistent serialization.** The same kind of data is exported in
  different formats, such as single escaped strings in one place and arrays of
  lines in another. Diffs are noisy.
- **Secrets in exports.** Integration settings carry plaintext credentials
  inside exported configuration.
- **Manual workarounds.** Denormalized copies of related data, plus scheduled
  jobs to repair them when they drift, are needed to make queries fast.

**Axis rules:**

- **Partial updates.** Authoring APIs patch resources, and concurrent edits
  are detected through resource versions.
- **Explicit references.** Every relationship between resources is a typed
  reference, validated at compile time.
- **Declared dependencies.** Modules declare versioned dependencies. A release
  does not compile if any reference cannot be resolved.
- **Canonical files.** One file per resource, stable ordering, LF line endings,
  and multi-line expressions kept readable.
- **No secrets in configuration.** Configuration holds deployment binding
  references only.
- **Query instead of denormalize.** Relational storage with joins,
  aggregates, indexes and computed fields comes first. Projections maintained
  by the platform are added only when a measured need appears.

## User interface

**Failure mode.** When the form designer lacks a capability, authors
switch to raw HTML and JavaScript components. In practice the custom HTML
component becomes the most-used form component, and custom widgets reach
400–700 lines of hand-written front-end code. Theme, localization, security
and upgrades all break at those points.

**Axis rules:**

- **Shared templates and tokens.** Pages are built only from shared templates
  and widgets, styled through shared tokens in light and dark mode.
- **Strong standard forms.** Standard forms cover these needs from the start:
  - sections and wizard steps
  - conditional visibility, required and read-only states
  - validation on submit and as the user types
  - line items (child collections)
  - lookups
  - file upload
  - read-only summaries
- **Custom widgets later.** Custom widgets come later as packaged React
  components (D8). Each one is reviewed as a sign of a missing standard
  capability.
- **Display is formatting only.** The UI formats a value for the locale and
  never changes it. What the API returned is what is shown and what is sent
  back.
- **The URL holds the page state.** Paging, sorting and similar view state
  live in the address, so reload, sharing and the back button keep them.
  Components change only their own parameters.

## Testing applications

**Common practice.** Authors write many configuration-level test processes,
often over a hundred per application, to seed data and assert behaviour.
Running them usually depends on a manually prepared environment, so they do
not run in CI.

**Axis rule:** test scenarios are a resource kind. They run against a
temporary tenant database in CI, as specific users, and assert data, task and
process outcomes, including denied actions.

## Operations

**Axis rules:**

- **Correlation.** Every request, process instance, step attempt and connector
  call carries correlation identifiers. Operators can go from a user-visible
  error to the exact attempt and its logs.
- **Durable history.** Execution history is stored with the process. Sampled
  telemetry is not the source of truth.
- **Log protection.** Sensitive fields are masked in logs and inspection
  according to field flags.
- **Recovery visibility.** Failed and stuck work is visible. Retry,
  cancellation and resolution are authorized, audited commands.
