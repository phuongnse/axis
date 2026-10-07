# Axis concepts

This is the vocabulary used in code, APIs, configuration files and UI. The
**From** column gives the milestone that introduces the concept (see the
[roadmap](../roadmap.md)).

## Packaging and lifecycle

| Concept | Meaning | From |
| --- | --- | --- |
| **Application** | A deployable business application. It is a folder of resources with a manifest, and owns a set of sites. | M1 |
| **Resource** | One typed configuration item with a stable `id`, a `kind`, a `name`, a format version and typed references to other resources. | M1 |
| **Module** | A reusable bundle of resources and extension operations, such as notifications, document generation or audit export. Applications declare the modules they depend on, with version ranges. | M8 |
| **Release** | The immutable compiled form of an application at one point in time. It pins every resource version it uses. New work starts on the active release; running work stays on its own. | M1 (basic), M6 (full) |
| **Diagnostic** | A validation or compile error or warning. It points at the resource, field and path that caused it. | M1 |

## Data

| Concept | Meaning | From |
| --- | --- | --- |
| **Entity** | A business record type. Each entity becomes a typed PostgreSQL table in the tenant database. | M1 |
| **Field** | A typed attribute of an entity, with constraints. | M1 |
| **Computed field** | A field whose value comes from an expression over the record and its related records. | M2 |
| **Data source** | A named, parameterized query over entities. It declares projections, filters, sorting, paging, related entities and aggregates. Pages, widgets, rules and processes read data only through data sources or entity APIs. | M2 |
| **Sequence** | A counter that hands out business numbers, such as `PR-2026-00042`, inside the caller's transaction. | M3 |
| **Seed data** | Reference or demo records shipped with an application and applied idempotently. Seed data is not created by process code. | M1 (basic), M2 |

**Field types** start with:

- text
- integer
- decimal
- boolean
- date
- date-time
- enum
- reference (to another entity)

Later types:

- money
- file
- child collection (owned rows such as line items)
- JSON object

**Field constraints** in M1:

- required
- unique
- maxLength (text)
- precision and scale (decimal)

Later constraints:

- range
- index
- default value

**Field flags** (later):

- searchable
- audited
- masked in logs
- encrypted at rest

## Logic

| Concept | Meaning | From |
| --- | --- | --- |
| **Expression** | A typed, side-effect-free formula in Axis's expression language, checked at compile time. | M2 |
| **Rule** | A named, reusable expression with declared typed parameters. Rules are used for validation, routing, conditions and policy filters. | M2 |
| **Operation** | A typed unit of work with declared inputs, outputs, capabilities and idempotency, for example "create record", "send email" or "call the purchasing system". Operations are built in or come from extension packages. | M3 |
| **Extension package** | Versioned C# code, and later React components, that adds operations and widgets under declared capabilities. | M5 |

## Processes

| Concept | Meaning | From |
| --- | --- | --- |
| **Process** | A versioned definition of steps and transitions, executed durably. It explicitly declares the entities it works on. | M3 |
| **Process instance** | One run of a process. It is pinned to its release and records every step, attempt, input, output and decision. | M3 |
| **Human task** | A step that waits for a person. It has an assignee (user, role or queue), a form, a due date and allowed outcomes such as approve, return or reject. Exactly one final decision is recorded. | M3 |
| **Retry policy** | Declared per operation step: attempts, backoff, which errors are retryable, and what happens when attempts run out (alert, manual retry, compensation). | M5 |

**Step kinds:**

- operation
- decision
- human task
- wait for event
- timer
- sub-process
- end

## Triggers and integration

| Concept | Meaning | From |
| --- | --- | --- |
| **Trigger** | Starts a process or operation from something that happened. There are four kinds, listed below. | M5 |
| **Event** | A named, typed business event raised by a process or the platform. Subscribers are either synchronous (same transaction) or asynchronous (durable delivery). | M5 |
| **Connector** | Configuration for calling an external system: endpoint binding, secret reference, timeouts, idempotency and reconciliation contract. | M5 |

**Trigger kinds:**

- **Data change:** a record was created, updated or deleted. The trigger can
  read which fields changed.
- **Event:** a named business event was raised.
- **Schedule:** a time-zone-aware recurrence. It can fan out over the records
  a data source returns.
- **Endpoint:** an authenticated inbound HTTP call with a typed input schema. It
  can return a process output.

## Presentation

| Concept | Meaning | From |
| --- | --- | --- |
| **Site** | An entry point of one application, with its own path, title, locales (default, fallback and available) and navigation. Theme, identity provider binding and domain come later. | M1 |
| **Page** | A route in a site. It has a title, and its content is its widgets: exactly one in M1. A page has no entity or template of its own. | M1 |
| **Widget** | A UI block on a page. M1 has `table` and `form` widgets over one entity, and a table may name a `formPage` that opens its records. More types (list, detail, task inbox, process inspector), container widgets for layout (tabs, sections, columns) and custom widgets come later. | M1 |
| **Form** | Fields laid out in sections and steps, with visibility, enabled and required conditions, validation rules, child collections (line items), lookups and file upload. | M1 (basic), M3 (full) |
| **Action** | A user command on a page or widget: start a process, complete a task, run an operation or navigate. Actions are always authorized on the server. M1 only has the table's `formPage` link; navigate actions are still **Proposed** in [D15](../decisions.md#d15-presentation-model--agreed). | M3 |
| **Theme** | Design tokens for light and dark mode, shared by every site. M1 has the shared tokens. A site will be able to adjust tokens later, but never add per-page styling. | M1 (shared tokens), later (site adjustments) |
| **Text resource** | A localized text with a stable key. Every UI string comes from text resources. An application has one `text` resource per locale, and every locale has the same keys. A key used by a label must exist in every locale. Texts are never shared across applications. Releases pin text versions, and a key that is still in use cannot be deleted. | M1 (basic), M6 (full) |

## Security and operations

| Concept | Meaning | From |
| --- | --- | --- |
| **Role** | A named set of permissions. Users get roles through membership or identity-provider claims. | M4 |
| **Policy** | A grant on a resource, record, field or action, optionally with a record filter rule. Default-deny applies. | M4 |
| **Tenant** | An isolated customer scope with its own database, users, applications and settings. | M1 (context), M9 (management) |
| **Deployment binding** | Environment-specific settings that a release resolves at run time, held as references and never as values in configuration. Examples: database, storage, identity provider, connector endpoints and secrets. | M5 |
| **Audit record** | An append-only record of a consequential action, committed in the same transaction as the action. | M3 |
| **Test scenario** | A configuration-level test that runs against a temporary tenant: it seeds data, runs actions or processes as given users and asserts the outcomes. | M6 |
