# Axis concepts

This is the vocabulary used in code, APIs, configuration files and UI. The
**From** column gives the milestone that introduces the concept (see the
[roadmap](../roadmap.md)). The docs sweep at milestone close checks this
column and moves a concept to a later milestone when it was not delivered.
See [the life of a milestone](../delivery.md#the-life-of-a-milestone).

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
| **Computed field** | A field whose value comes from an expression over the record's own fields and its child rows. It is stored and recomputed on every write. | M2 |
| **Data source** | A named, parameterized query over entities. It declares projections, filters, sorting, paging, related entities and aggregates. Pages, widgets, rules and processes read data only through data sources or entity APIs. See [data sources](../reference/data-sources.md). | M2 |
| **Sequence** | A `sequence` resource that hands out business numbers from a format such as `PR-{yyyy}-{n:5}`, giving `PR-2026-00042`. A `text` field names it and gets its number on create, in the create's transaction, so there are no gaps. When the format has a year, the counter restarts each UTC year. See [configuration](../reference/configuration.md#sequences). | M3 |
| **Seed data** | Reference or demo records shipped with an application and applied idempotently. Seed data is not created by process code. A seed can be synced so changed values update the seeded records on start. | M1 (basic), M2 |

**Field types** start with:

- text
- integer
- decimal
- boolean
- date
- date-time
- enum
- reference (to another entity)
- child collection (owned rows such as line items). The record API reads and
  writes its rows through the owner record.

Later types:

- money
- file
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
| **Expression** | A typed, side-effect-free formula in Axis's [expression language](../reference/expressions.md), checked at compile time. | M2 |
| **Rule** | A named, reusable expression with declared typed parameters and a result type. Validations, data source filters, process conditions and policy filters call rules. | M2 |
| **Operation** | A typed unit of work with declared inputs, outputs, capabilities and idempotency, for example "create record", "send email" or "call the purchasing system". Operations are built in or come from extension packages. M3 has one built-in operation, `updateRecord`, which sets fields of the subject record inside the step's transaction. M5 adds a second built-in operation, `callConnector`, which calls one operation of a connector outside the step's transaction, with a stable operation identity as its idempotency key. See [connector calls](../reference/processes.md#connector-calls). *(planned for M5)* Package operations, named `<package>/<operation>`, declare their capabilities and run staged outside the step's transaction, like `callConnector`. See [package operations](../reference/processes.md#package-operations). | M3 (basic), M5 |
| **Extension package** | *(planned for M5)* A .NET class library with a SemVer version that adds typed operations under declared capabilities. It references only `Axis.Extensions` and ships with a manifest, `axis-package.json`. Packages are installed per installation and pinned by releases. Each version loads in its own `AssemblyLoadContext`. A package is trusted code, not sandboxed. React components come later. See [extension packages](../reference/extension-packages.md). | M5 |

## Processes

| Concept | Meaning | From |
| --- | --- | --- |
| **Process** | A versioned definition of steps and transitions, executed durably. It explicitly declares the entities it works on. In M3 a process names one entity, its subject, and runs for one record of it. | M3 |
| **Process instance** | One run of a process. It is pinned to its release and records every step occurrence with its input, output, decision and error. | M3 |
| **Human task** | A step that waits for a person. It has an assignee, a form, a due date and allowed outcomes such as approve, return or reject. Exactly one final decision is recorded. In M3 the assignee is a user or a role, and the due date is only shown. Queues and escalation come later. See [processes](../reference/processes.md#task-api). | M3 |
| **Retry policy** | In M5, a `callConnector` step and a package operation step declare their attempts and their backoff. The platform fixes which errors are retried, and the instance fails when attempts run out. Operator retry comes in M7, and alerts and compensation later. See [call outcomes](../reference/processes.md#call-outcomes) and [package call outcomes](../reference/processes.md#package-call-outcomes). | M5 |

**Step kinds:**

- operation (M3)
- decision (M3)
- human task (M3)
- end (M3)
- wait for event (later)
- timer (later)
- sub-process (later)

See [processes](../reference/processes.md#steps).

## Triggers and integration

| Concept | Meaning | From |
| --- | --- | --- |
| **Trigger** | A `trigger` resource starts a process for one record when something happens. M5 has three kinds, listed below. See [triggers](../reference/configuration.md#events-and-triggers). | M5 |
| **Event** | An `event` resource: a named event about one entity, with typed payload fields. A `raiseEvent` step raises it for its subject record. Event triggers start processes from it, in the same transaction (sync) or through durable delivery (async). Events raised by the platform come later. | M5 |
| **Connector** | A `connector` resource for calling an external system. It has a base URL from a `url` binding, auth from a `secret` binding, a timeout and named operations. It can name the idempotency header the system accepts, and each operation can name a lookup that finds an earlier call by its key. See [connectors](../reference/configuration.md#connectors-and-deployment-bindings). | M5 |
| **Endpoint** | An `endpoint` resource that lets another system, signed in with a bearer token, create a record and start a process on it in one call. It has typed input fields, the entity it creates, a mapping from input to fields and the process it starts. It can return output fields when the process completes within a short wait. The resource, its compile checks, the call with its output and wait, and the status location are built. Idempotency is *(planned for M5)*. See [endpoints](../reference/configuration.md#endpoint-resource). | M5 |

**Trigger kinds:**

- **Data change:** a record was created or updated, optionally only when
  named fields changed. Delete comes later.
- **Event:** a named event was raised.
- **Schedule:** a cron schedule in an IANA time zone. It fans out over the
  records of a data source.

Inbound endpoints are their own resource, not a trigger kind. See
[D27](../decisions.md#d27-inbound-endpoints--agreed).

## Presentation

| Concept | Meaning | From |
| --- | --- | --- |
| **Site** | An entry point of one application, with its own path, title, locales (default, fallback and available) and navigation. Theme, identity provider binding and domain come later. | M1 |
| **Page** | A route in a site. It has a title, and its content is its widgets: exactly one. A page has no entity or template of its own. | M1 |
| **Widget** | A UI block on a page. M1 has `table` and `form` widgets over one entity, a `table` may bind a data source instead of an entity (M2), and a table may name a `formPage` that opens its records. M3 adds the `taskInbox` widget, which lists the user's open tasks. More types (list, detail, process inspector), container widgets for layout (tabs, sections, columns) and custom widgets come later. | M1 |
| **Form** | Fields laid out in sections and steps, with visibility, enabled and required conditions, validation rules, child collections (line items), lookups and file upload. M1 has a basic form over all of an entity's fields. M3 builds the `form` resource: sections of fields, any of them read-only, used by both pages and tasks. Wizard steps, conditional states, validation as the user types and file upload come later. See [frontend](../reference/frontend.md). | M1 (basic), M3 (form resource), later |
| **Action** | A user command on a page or widget: start a process, complete a task, run an operation or navigate. Actions are always authorized on the server. M1 only has the table's `formPage` link. M3 builds the `startProcess` action on a form widget and the outcomes of a task. Run operation comes later, and navigate actions are still **Proposed** in [D15](../decisions.md#d15-presentation-model--agreed). | M3 |
| **Theme** | Design tokens for light and dark mode, shared by every site. M1 has the shared tokens. A site will be able to adjust tokens later, but never add per-page styling. | M1 (shared tokens), later (site adjustments) |
| **Text resource** | A localized text with a stable key. Every UI string comes from text resources. An application has one `text` resource per locale, and every locale has the same keys. A key used by a label must exist in every locale. Texts are never shared across applications. Releases pin text versions, and a key that is still in use cannot be deleted. | M1 (basic), M6 (full) |

## Security and operations

| Concept | Meaning | From |
| --- | --- | --- |
| **Role** | A declared `role` resource with a name and a label. A task assigned to a role, and each role a policy names, must name a declared role. Users hold roles through the identity provider's roles claim, and a claim value that names no declared role is ignored. Policies give roles their grants. Every application endpoint matches the claim values. See [roles and policies](../reference/configuration.md#roles-and-policies), and [authorization](../architecture.md#authorization). | M4 |
| **Policy** | A `policy` resource that gives one or more roles grants on entities, data sources and processes, and `invoke` on endpoints. An entity grant may have a record filter and a list of fields. The grants of all the user's policies combine with OR, and default-deny applies. The resource and its compile checks are built, `endpoint` grants included. The record API, the data source endpoint, the process start endpoint and the endpoint call enforce its grants, and the record API and the data source endpoint also enforce its record filters and field lists. Site navigation and page metadata follow a signed-in user's grants, and page metadata leaves out the fields the user may not read. See [roles and policies](../reference/configuration.md#roles-and-policies), and [authorization](../architecture.md#authorization). | M4 |
| **Tenant** | An isolated customer scope with its own database, users, applications and settings. | M1 (context), M9 (management) |
| **Deployment binding** | Environment-specific settings that a release resolves at run time, held as references and never as values in configuration. Examples: database, storage, identity provider, connector endpoints and secrets. M5 has two kinds, `url` and `secret`. Their values are set per tenant in host configuration until the tenant directory in M9. See [deployment bindings](../reference/configuration.md#deployment-bindings). | M5 |
| **Audit record** | An append-only record of a consequential action, committed in the same transaction as the action. It holds the time, the actor, the action, the application, the entity, the record, the process instance and a details object. It also holds the actor's display name at the time of the action. The database rejects every update and delete. See [storage](../reference/storage.md#audit-records-and-sequence-counters). | M3 |
| **Test scenario** | A configuration-level test that runs against a temporary tenant: it seeds data, runs actions or processes as given users and asserts the outcomes. | M6 |
