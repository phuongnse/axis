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
| **Module** | A reusable, versioned folder of resources with a `module.json` manifest. Applications declare the modules they use with version ranges. Each application gets its own copy of a module's entities, sequences and seeds. See [configuration](../reference/configuration.md#modules). | M8 |
| **Release** | The immutable compiled form of an application at one point in time. It pins every resource version it uses. New work starts on the active release. Running work, with its tasks, forms, texts and package versions, stays on its release until it ends, while roles and policies come from the active release. | M1 (basic), M6 (full) |
| **Diagnostic** | A validation or compile error or warning. It points at the resource, field and path that caused it. | M1 |
| **Studio** | The visual editor of an application's resources. It edits the JSON files of the application folders that `Studio:Folders` lists, through an authoring API. Its edit routes never activate a release. *(Planned for M10:)* it asks for an activation through a publish request, see **Publish request**. It shows where each resource is used. Visual page, form and process designers come later, see [D46](../decisions.md#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed). See [authoring API](../reference/authoring.md). | M8 |
| **Publish request** | A request that Studio stores in the tenant database to validate or publish an application, or to roll back to an earlier release. It holds a snapshot of the folder, or a release id, and has a status: `queued`, `running`, `succeeded` or `failed`. A separate publisher runs it, so the server never activates a release itself. A request can also target the tenant's preview tenant, see **Preview tenant**. See [publishing](../reference/authoring.md#publishing) and [D47](../decisions.md#d47-publishing-from-studio--agreed). *(Planned for M10.)* | M10 |
| **Release history** | The per-tenant list of activations of each application. Each row holds the release, its content hash, the actor, the label, the note and the request. A release gets a number, 1, 2, 3…, the first time it is activated in the tenant. A rollback names a release from it. See [release history](../reference/storage.md#publish-requests-and-release-history). *(Planned for M10.)* | M10 |
| **Reference index** | The uses and used-by edges between resources, text keys, extension packages and modules. A compile builds it in memory, and it is not stored in the release. Studio shows it as a **Uses** list and a **Used by** list. See [reference index](../reference/configuration.md#reference-index). | M8 |

## Data

| Concept | Meaning | From |
| --- | --- | --- |
| **Entity** | A business record type. Each entity becomes a typed PostgreSQL table in the tenant database. | M1 |
| **Field** | A typed attribute of an entity, with constraints. | M1 |
| **Computed field** | A field whose value comes from an expression over the record's own fields and its child rows. It is stored and recomputed on every write. A release that changes it, or adds it, recomputes stored values at activation. So does a migration `fill` or `convert` of the rows it changes. Above `Activation:InlineRecomputeLimit` rows, the worker recomputes the rows in batches. A newer release restarts that backfill, and a write by work pinned to an older release starts it again. A single read and the engine's reads of a subject record compute it on the spot meanwhile. The record API, the process endpoints and data sources refuse a sort, a search, a filter or a grouping that reads a pending field with `409`, and a schedule fan-out over one waits for the backfill. | M2 |
| **Data source** | A named, parameterized query over entities. It declares projections, filters, sorting, paging, related entities and aggregates. Pages, widgets, rules and processes read data only through data sources or entity APIs. See [data sources](../reference/data-sources.md). | M2 |
| **Sequence** | A `sequence` resource that hands out business numbers from a format such as `PR-{yyyy}-{n:5}`, giving `PR-2026-00042`. A `text` field names it and gets its number on create, in the create's transaction, so there are no gaps. When the format has a year, the counter restarts each UTC year. See [configuration](../reference/configuration.md#sequences). | M3 |
| **Seed data** | Reference or demo records shipped with an application and applied idempotently. Seed data is not created by process code. A seed can be synced so changed values update the seeded records on start. | M1 (basic), M2 |
| **Migration** | A `migration` resource that changes one entity's stored data in a way that is not additive: rename or drop a field, drop the entity, fill nulls or convert values. Activation runs it once per tenant, before additive provisioning, in the same transaction. Rename field, drop field, drop entity, fill and convert are built, and so is the refusal of a destructive migration while old-release instances use the column. A retry of a `failed` instance after a destructive migration on a column its pinned release uses is refused. A fill or convert on a child entity or of a field a computed field reads recomputes the changed rows and their owners at activation. See [storage](../reference/storage.md#migrations). | M6 |

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
| **Operation** | A typed unit of work with declared inputs, outputs, capabilities and idempotency, for example "create record", "send email" or "call the purchasing system". Operations are built in or come from extension packages. M3 has one built-in operation, `updateRecord`, which sets fields of the subject record inside the step's transaction. M5 adds a second built-in operation, `callConnector`, which calls one operation of a connector outside the step's transaction, with a stable operation identity as its idempotency key. See [connector calls](../reference/processes.md#connector-calls). Package operations, named `<package>/<operation>`, run staged outside the step's transaction, like `callConnector`. They declare their capabilities, and a context limits them to those. See [package operations](../reference/processes.md#package-operations). | M3 (basic), M5 |
| **Extension package** | A .NET class library with a SemVer version that adds typed operations under declared capabilities. It references only `Axis.Extensions` and ships with a manifest, `axis-package.json`. Packages are installed per installation, and each version loads in its own `AssemblyLoadContext`. An application declares the packages it uses with version ranges, and its releases pin the exact versions. Process steps run their operations. An operation context hands out only the declared capabilities. Calls are retried under the step's retry policy. A package is trusted code, not sandboxed. React components come later. See [extension packages](../reference/extension-packages.md). | M5 |

## Processes

| Concept | Meaning | From |
| --- | --- | --- |
| **Process** | A versioned definition of steps and transitions, executed durably. It explicitly declares the entities it works on. In M3 a process names one entity, its subject, and runs for one record of it. | M3 |
| **Process instance** | One run of a process. It is pinned to its release and records every step occurrence with its input, output, decision and error. | M3 |
| **Human task** | A step that waits for a person. It has an assignee, a form, a due date and allowed outcomes such as approve, return or reject. Exactly one final decision is recorded. In M3 the assignee is a user or a role, and the due date is only shown. Queues and escalation come later. See [processes](../reference/processes.md#task-api). | M3 |
| **Operator command** | An authorized, audited command on process work that the engine cannot finish alone. **Cancel** ends a `running` or `waiting` instance as `cancelled` and cancels its open tasks. **Retry** runs a `failed` instance again from its failed step, on its pinned release. **Resolve** settles a connector call or a package call. It settles a call whose outcome is `unknown`, as succeeded with output values or as failed. All three are built. Each needs its own `process` grant and a reason, and writes a history row and an audit record. Cancel does not undo steps that already ran. See [operator commands](../reference/processes.md#operator-commands). | M7 |
| **Retry policy** | A `callConnector` step and a package operation step declare their attempts and their backoff. The platform fixes which errors are retried, and the instance fails when attempts run out. Operator [retry](../reference/processes.md#retry) is built. Alerts and compensation come later. See [call outcomes](../reference/processes.md#call-outcomes) and [package call outcomes](../reference/processes.md#package-call-outcomes). | M5 |
| **Process inspector** | A widget that names one process, lists its instances and opens one. It shows the graph with the path drawn on it, the tasks, and the connector and package calls with their attempts. A call's input is shown only when the user may read every field its mapping reads or writes, and a field marked `sensitive` is never shown. It needs an `inspect` grant on the process. The purchase request sample has one, the Approvals page, for its `operator` role. The record history links to an instance. See [process inspection](../reference/processes.md#process-inspection). | M7 |

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
| **Trigger** | A `trigger` resource starts a process for one record when something happens. M5 has three kinds, listed below. The event, data change and schedule trigger resources and their compile checks are built. The worker runs schedule triggers over the records of their data source. Data change triggers start their processes on record API creates and updates, on an inbound endpoint's create, on task form saves, on `updateRecord` steps and on connector and package operation outputs, the worker delivers raised events to `async` event triggers, and a `raiseEvent` step starts the processes of `sync` event triggers in its own transaction. See [triggers](../reference/configuration.md#events-and-triggers). | M5 |
| **Event** | An `event` resource: a named event about one entity, with typed payload fields. A `raiseEvent` step raises it for its subject record. Event triggers start processes from it, in the same transaction (sync) or through durable delivery (async). Events raised by the platform come later. | M5 |
| **Connector** | A `connector` resource for calling an external system. It has a base URL from a `url` binding, auth from a `secret` binding, a timeout and named operations. It can name the idempotency header the system accepts, and each operation can name a lookup that finds an earlier call by its key. See [connectors](../reference/configuration.md#connectors-and-deployment-bindings). | M5 |
| **Endpoint** | An `endpoint` resource that lets another system, signed in with a bearer token, create a record and start a process on it in one call. It has typed input fields, the entity it creates, a mapping from input to fields and the process it starts. It can return output fields when the process completes within a short wait. The resource, its compile checks, the call with its output and wait, its idempotency key, and the status location are built. See [endpoints](../reference/configuration.md#endpoint-resource). | M5 |

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
| **Widget** | A UI block on a page. M1 has `table` and `form` widgets over one entity, a `table` may bind a data source instead of an entity (M2), and a table may name a `formPage` that opens its records. M3 adds the `taskInbox` widget, which lists the user's open tasks. The process inspector widget, which lists the instances of one process and shows one, is built in M7. More types (list, detail), container widgets for layout (tabs, sections, columns) and custom widgets come later. | M1 |
| **Form** | Fields laid out in sections and steps, with visibility, enabled and required conditions, validation rules, child collections (line items), lookups and file upload. M1 has a basic form over all of an entity's fields. M3 builds the `form` resource: sections of fields, any of them read-only, used by both pages and tasks. Wizard steps, conditional states, validation as the user types and file upload come later. See [frontend](../reference/frontend.md). | M1 (basic), M3 (form resource), later |
| **Action** | A user command on a page or widget: start a process, complete a task, run an operation or navigate. Actions are always authorized on the server. M1 only has the table's `formPage` link. M3 builds the `startProcess` action on a form widget and the outcomes of a task. Run operation comes later, and navigate actions are still **Proposed** in [D15](../decisions.md#d15-presentation-model--agreed). | M3 |
| **Theme** | Design tokens for light and dark mode, shared by every site. M1 has the shared tokens. A site will be able to adjust tokens later, but never add per-page styling. | M1 (shared tokens), later (site adjustments) |
| **Text resource** | A localized text with a stable key. Every UI string comes from text resources. An application has one `text` resource per locale, and every locale has the same keys. A key used by a label must exist in every locale. Texts are never shared across applications. Each release carries its own texts, so removing a key in a newer release does not affect work still running on an older one. | M1 (basic), M6 (full) |

## Security and operations

| Concept | Meaning | From |
| --- | --- | --- |
| **Role** | A declared `role` resource with a name and a label. A task assigned to a role, and each role a policy names, must name a declared role. Users hold roles through the identity provider's roles claim, and a claim value that names no declared role is ignored. Policies give roles their grants. Every application endpoint matches the claim values. See [roles and policies](../reference/configuration.md#roles-and-policies), and [authorization](../architecture.md#authorization). | M4 |
| **Policy** | A `policy` resource that gives one or more roles grants on entities, data sources and processes, and `invoke` on endpoints. An entity grant may have a record filter and a list of fields. The grants of all the user's policies combine with OR, and default-deny applies. The resource and its compile checks are built, `endpoint` grants included. The record API, the data source endpoint, the process start endpoint and the endpoint call enforce its grants, and the record API and the data source endpoint also enforce its record filters and field lists. Site navigation and page metadata follow a signed-in user's grants, and page metadata leaves out the fields the user may not read. See [roles and policies](../reference/configuration.md#roles-and-policies), and [authorization](../architecture.md#authorization). | M4 |
| **Author** | An installation-level role, not a `role` resource. The setting `Studio:AuthorRole` names the roles claim value. A user who holds it may use Studio. With no value, nobody may. Application policies play no part. See [Studio settings](../reference/configuration.md#studio). | M8 |
| **Publisher role** | An installation-level role, not a `role` resource. The setting `Studio:PublisherRole` names the roles claim value. A user who holds it and the author role may publish and roll back from Studio. With no value, nobody may. The host that runs the requests is the command `axis-admin publisher`, see [administration](../reference/administration.md#publisher). *(Planned for M10.)* | M10 |
| **Tenant** | An isolated customer scope with its own database, users, applications and settings. | M1 (context), M9 (management) |
| **Tenant directory** | The list of tenants in the platform database: each tenant's status, hosts, database, sign-in settings and deployment bindings, with secrets stored encrypted. The server and the worker read it and pick up changes without a restart. Host configuration stays the default source. See [D42](../decisions.md#d42-tenant-directory-in-the-platform-database--agreed). | M9 |
| **Preview tenant** | A tenant that belongs to one main tenant, where an author tries an application before publishing it. It has its own database, hosts and deployment bindings, and its data never mixes with the main tenant's. Its bindings are empty by default, so it never calls a real system by accident. It signs people in with its main tenant's sign-in settings. See [preview tenants](../architecture.md#preview-tenants) and [D48](../decisions.md#d48-preview-tenants--agreed). Built under the `configuration` tenant source, with `PreviewOf`, sign-in and bindings. *(Planned for M10:)* the `directory` source, status and publishing to it. | M10 |
| **Deployment binding** | Environment-specific settings that a release resolves at run time, held as references and never as values in configuration. Examples: database, storage, identity provider, connector endpoints and secrets. M5 has two kinds, `url` and `secret`. Their values are set per tenant in host configuration or in the tenant directory under `Tenancy:Source=directory`. See [deployment bindings](../reference/configuration.md#deployment-bindings). | M5 |
| **Audit record** | An append-only record of a consequential action, committed in the same transaction as the action. It holds the time, the actor, the action, the application, the entity, the record, the process instance and a details object. It also holds the actor's display name at the time of the action. The database rejects every update and delete. See [storage](../reference/storage.md#audit-records-and-sequence-counters). | M3 |
| **Test scenario** | A `scenario` resource. It is a configuration-level test that runs against its own temporary tenant database. It applies the seed data, runs actions as given users with stubbed connector and package calls, and asserts record, task and process outcomes, including denied actions. It is compiled with the application, and its names and values are checked, but it is not part of its release. The runner runs record actions, process starts, task completions and the `expect` steps, and runs the process steps, the connector and package calls and the event deliveries after each action. Stubs answer the calls. Schedule triggers run through the `advanceTime` step, which moves `now` forward and runs every due work item. | M6 |
