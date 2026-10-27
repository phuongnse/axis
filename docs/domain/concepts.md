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
| **Module** | A reusable, versioned folder of resources with a `module.json` manifest. Applications declare the modules they use with version ranges. Each application gets its own copy of a module's entities, sequences and seeds. Modules that hold processes, pages and policies and depend on other modules are planned for M17, see [D89](../decisions/d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md). See [configuration](../reference/modules-and-templates.md#modules). | M8, M17 (processes, pages, policies, dependencies) |
| **Role slot** | A role that a module names without knowing the application's roles. `module.json` declares its slots, module policies and task assignees name them, and each application maps every slot to one of its own roles. See [modules](../reference/modules-and-templates.md#processes-pages-policies-and-dependencies). | M17 |
| **Application template** | An installed, versioned application folder with a `template.json`. Studio creates a new application by copying it, with new resource ids, the new name and a site path that follows the name. See [application templates](../reference/modules-and-templates.md#application-templates). | M11 |
| **Release** | The immutable compiled form of an application at one point in time. It pins every resource version it uses. New work starts on the active release. Running work, with its tasks, forms, texts and package versions, stays on its release until it ends, while roles and policies come from the active release. | M1 (basic), M6 (full) |
| **Diagnostic** | A validation or compile error or warning. It points at the resource, field and path that caused it. | M1 |
| **Studio** | The visual editor of an application's resources. It edits the JSON files of the application folders that `Studio:Folders` lists, through an authoring API. It also creates applications in `Studio:ApplicationsPath`. Its edit routes never activate a release. It asks for an activation through a publish request, see **Publish request**. It shows where each resource is used. A visual page designer, the Design tab of the page editor, is built, see [D65](../decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md). A visual form designer, the Design tab of the form editor, is built, see [D66](../decisions/d66-a-visual-form-designer-for-form-resources.md). A page or a form opens on its Design tab. A visual process designer, the Design tab of the process editor, is built, except its live region for moves, which is *(planned for M16)*, see [D74](../decisions/d74-a-visual-process-designer.md). See [authoring API](../reference/authoring.md). | M8, M13 (page and form designers), M14 (process designer) |
| **Publish request** | A request that Studio stores in the tenant database to validate or publish an application, or to roll back to an earlier release. It holds a snapshot of the folder, or a release id, and has a status: `queued`, `running`, `succeeded` or `failed`. A separate publisher runs it, so the server never activates a release itself. A request can also target the tenant's preview tenant, see **Preview tenant**. See [publishing](../reference/authoring.md#publishing) and [D47](../decisions/d47-publishing-from-studio.md). Validate, publish, rollback, preview and test requests are built. A fourth kind, `test`, runs the application's test scenarios from Studio, see [D55](../decisions/d55-running-test-scenarios-from-studio.md). | M10 |
| **Release history** | The per-tenant list of activations of each application. Each row holds the release, its content hash, the actor, the label, the note and the request. A release gets a number, 1, 2, 3…, the first time it is activated in the tenant. A rollback names a release from it. See [release history](../reference/storage.md#release-history). The history, its route and rollback are built. | M10 |
| **Reference index** | The uses and used-by edges between resources, text keys, extension packages and modules. A compile builds it in memory, and it is not stored in the release. Studio shows it as a **Uses** list and a **Used by** list. See [reference index](../reference/configuration.md#reference-index). | M8 |

## Data

| Concept | Meaning | From |
| --- | --- | --- |
| **Entity** | A business record type. Each entity becomes a typed PostgreSQL table in the tenant database. | M1 |
| **Field** | A typed attribute of an entity, with constraints. | M1 |
| **Computed field** | A field whose value comes from an expression over the record's own fields and its child rows. It is stored and recomputed on every write. A release that changes it, or adds it, recomputes stored values at activation. So does a migration `fill` or `convert` of the rows it changes. Above `Activation:InlineRecomputeLimit` rows, the worker recomputes the rows in batches. A newer release restarts that backfill, and a write by work pinned to an older release starts it again. A single read and the engine's reads of a subject record compute it on the spot meanwhile. The record API, the process endpoints and data sources refuse a sort, a search, a filter or a grouping that reads a pending field with `409`, and a schedule fan-out over one waits for the backfill. | M2 |
| **Data source** | A named, parameterized query over entities. It declares projections, filters, sorting, paging, related entities and aggregates. Pages, widgets, rules and processes read data only through data sources or entity APIs. See [data sources](../reference/data-sources.md). | M2 |
| **Sequence** | A `sequence` resource that hands out business numbers from a format such as `PR-{yyyy}-{n:5}`, giving `PR-2026-00042`. A `text` field names it and gets its number on create, in the create's transaction, so there are no gaps. When the format has a year, the counter restarts each year in the application time zone. See [configuration](../reference/resource-files.md#sequences). | M3 |
| **Seed data** | Reference or demo records shipped with an application and applied idempotently. Seed data is not created by process code. A seed can be synced so changed values update the seeded records on start. | M1 (basic), M2 |
| **File** | A file that a `file` field holds: uploaded first as `pending`, then attached by a record write, downloaded by id under the owner record's read grants, and stored where the application's `storage` binding says. M15 stores the bytes in the tenant database. See [file fields](../reference/file-fields.md) and [D76](../decisions/d76-file-money-and-enum-label-field-types.md). | M15 |
| **Import and export** | Writing the rows of a data source or an entity to a CSV or spreadsheet file, and creating or updating records from one. Each needs its own grant action, `export` or `import`. An export runs the rows query under the user's read grants. An import runs on the worker, checks every row in a dry run, then writes all rows or none through the record command. See [import and export](../reference/import-export.md) and [D79](../decisions/d79-import-and-export-under-policies.md). | M15 |
| **Migration** | A `migration` resource that changes one entity's stored data in a way that is not additive: rename or drop a field, drop the entity, fill nulls or convert values. Activation runs it once per tenant, before additive provisioning, in the same transaction. Rename field, drop field, drop entity, fill and convert are built, and so is the refusal of a destructive migration while old-release instances use the column. A retry of a `failed` instance after a destructive migration on a column its pinned release uses is refused. A fill or convert on a child entity or of a field a computed field reads recomputes the changed rows and their owners at activation. See [storage](../reference/migrations.md#migrations). | M6 |

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

M15 adds, see
[entity field types](../reference/resource-files.md#types-and-properties-added-in-m15):

- money, with a fixed currency or a currency field
- file
- references (a set of records, many-to-many)
- labels for enum values

Later types:

- JSON object

**Field constraints** in M1:

- required
- unique
- maxLength (text)
- precision and scale (decimal)

Ranges follow: `min` and `max` on integer, decimal, date and date-time, and
`minLength` on text. The server checks them and the database does not, so
changing one needs no migration.

M15 adds:

- index, declared on the entity, and added by the platform on every
  reference and owner column
- default value

**Field flags**:

- searchable

Later field flags:

- audited
- masked in logs
- encrypted at rest

## Logic

| Concept | Meaning | From |
| --- | --- | --- |
| **Expression** | A typed, side-effect-free formula in Axis's [expression language](../reference/expressions.md), checked at compile time. | M2 |
| **Rule** | A named, reusable expression with declared typed parameters and a result type. Validations, data source filters, process conditions and policy filters call rules. | M2 |
| **Operation** | A typed unit of work with declared inputs, outputs, capabilities and idempotency, for example "create record", "send email" or "call the purchasing system". Operations are built in or come from extension packages. M3 has one built-in operation, `updateRecord`, which sets fields of the subject record inside the step's transaction. M5 adds a second built-in operation, `callConnector`, which calls one operation of a connector outside the step's transaction, with a stable operation identity as its idempotency key. See [connector calls](../reference/process-execution.md#connector-calls). Package operations, named `<package>/<operation>`, run staged outside the step's transaction, like `callConnector`. They declare their capabilities, and a context limits them to those. See [package operations](../reference/process-execution.md#package-operations). | M3 (basic), M5 |
| **Extension package** | A .NET class library with a SemVer version that adds typed operations under declared capabilities. It references only `Axis.Extensions` and ships with a manifest, `axis-package.json`. Packages are installed per installation, and each version loads in its own `AssemblyLoadContext`. An application declares the packages it uses with version ranges, and its releases pin the exact versions. Process steps run their operations. An operation context hands out only the declared capabilities. Calls are retried under the step's retry policy. A package is trusted code, not sandboxed. Custom widgets in packages, a published SDK and package runners are planned for M17. See [extension packages](../reference/extension-packages.md). | M5, M17 (widgets, runners) |
| **Package runner** | A process of the `Axis.PackageHost` host that runs the operations of one installed package version, outside the worker, with an empty environment and one network route. Each runner runs one call at a time and serves every tenant. See [package runners](../reference/extension-packages.md#package-runners). | M17 |

## Processes

| Concept | Meaning | From |
| --- | --- | --- |
| **Process** | A versioned definition of steps and transitions, executed durably. It explicitly declares the entities it works on. In M3 a process names one entity, its subject, and runs for one record of it. A process without a subject, with typed parameters, is built, see [process flow](../reference/process-flow.md#processes-without-a-subject). | M3, M14 (without a subject) |
| **Process instance** | One run of a process. It is pinned to its release and records every step occurrence with its input, output, decision and error. | M3 |
| **Human task** | A step that waits for a person. It has an assignee, a form, a due date and allowed outcomes such as approve, return or reject. Exactly one final decision is recorded. In M3 the assignee is a user or a role, and the due date is only shown. A role holder can claim a task and release it, which is built. A task step may have a deadline that expires the task and moves the process on, which is built. The reassign of a task is built, and so is the escalation that reassigns a late task. The in-app `notify` of an escalation is built too. Sending its email is *(planned for M16)*, see [task deadlines and claims](../reference/task-deadlines.md). See [processes](../reference/task-api.md). | M3, M14 (deadlines and claims) |
| **Operator command** | An authorized, audited command on process work that the engine cannot finish alone. **Cancel** ends a `running` or `waiting` instance as `cancelled` and cancels its open tasks. **Retry** runs a `failed` instance again from its failed step, on its pinned release. **Resolve** settles a connector call or a package call. It settles a call whose outcome is `unknown`, as succeeded with output values or as failed. All three are built. Each needs its own `process` grant and a reason, and writes a history row and an audit record. Cancel does not undo steps that already ran. See [operator commands](../reference/process-inspection.md#operator-commands). | M7 |
| **Retry policy** | A `callConnector` step and a package operation step declare their attempts and their backoff. The platform fixes which errors are retried, and the instance fails when attempts run out. Operator [retry](../reference/process-inspection.md#retry) is built. Alerts and compensation come later. See [call outcomes](../reference/process-execution.md#call-outcomes) and [package call outcomes](../reference/process-execution.md#package-call-outcomes). | M5 |
| **Dead letter** | A delivery that kept failing after `Worker:DeliveryAttempts` tries: an event to a trigger or a waiting instance, a schedule fan-out or an email. It is kept in `axis.dead_letters` until an operator redelivers or discards it, with a reason. See [dead letters](../reference/process-events.md#dead-letters) and [D72](../decisions/d72-a-delivery-limit-and-dead-letters.md). A dead letter for a waiting instance is *(planned for M16)*. | M14 |
| **Notification** | A message a `notify` operation or a task escalation sends to users, roles or email addresses, in the site (in-app) or by email. Its texts have named placeholders filled from typed values, never from a `sensitive` field. Email goes through an outbox and is sent at least once. The email of a task escalation is *(planned for M16)*. See [notifications](../reference/notifications.md) and [D73](../decisions/d73-in-app-and-email-notifications.md). | M14 |
| **User profile** | What Axis keeps about a user who signed in: the user id, display name, email and locale, from the identity provider's claims. It gives notifications an address and a locale, and a reassign its known users. It is not a user directory. See [user profiles](../reference/notifications.md#user-profiles). | M14 |
| **Process inspector** | A widget that names one process, lists its instances and opens one. It shows the graph with the path drawn on it, the tasks, and the connector and package calls with their attempts. A call's input is shown only when the user may read every field its mapping reads or writes, and a field marked `sensitive` is never shown. It needs an `inspect` grant on the process. The purchase request sample has one, the Approvals page, for its `operator` role. The record history links to an instance. See [process inspection](../reference/process-inspection.md#process-inspection). | M7 |

**Step kinds:**

- operation (M3)
- decision (M3)
- human task (M3)
- end (M3)
- wait for event (M14)
- timer (M14)
- sub-process (M14)

See [processes](../reference/processes.md#steps) and, for the M14 steps,
loops through a waiting step, the `createRecord` operation and the email of the `notify` operation,
[process flow](../reference/process-flow.md).

## Triggers and integration

| Concept | Meaning | From |
| --- | --- | --- |
| **Trigger** | A `trigger` resource starts a process for one record when something happens. M5 has three kinds, listed below. A schedule without a data source and a delete start a process without a subject, which is built. The event, data change and schedule trigger resources and their compile checks are built. The worker runs schedule triggers over the records of their data source. Data change triggers start their processes on record API creates and updates, on an inbound endpoint's create, on task form saves, on `updateRecord` and `createRecord` steps and on connector and package operation outputs, the worker delivers raised events to `async` event triggers, and a `raiseEvent` step starts the processes of `sync` event triggers in its own transaction. See [triggers](../reference/integration-resources.md#events-and-triggers). | M5, M14 (delete triggers, scheduled jobs) |
| **Event** | An `event` resource: a named event about one entity, with typed payload fields. A `raiseEvent` step raises it for its subject record. Event triggers start processes from it, in the same transaction (sync) or through durable delivery (async). Events raised by the platform come later. | M5 |
| **Connector** | A `connector` resource for calling an external system. It has a base URL from a `url` binding, auth from a `secret` binding, a timeout and named operations. It can name the idempotency header the system accepts, and each operation can name a lookup that finds an earlier call by its key. OAuth2 client credentials and inputs in the path, query and headers, nested inputs and nested outputs are planned for M17. See [connectors](../reference/integration-resources.md#connectors-and-deployment-bindings). | M5, M17 (OAuth2, structured payloads) |
| **Webhook** | A `webhook` resource that sends an event to another system's URL, signed with the Standard Webhooks headers. The author configures it in the application. Delivery is at least once, and a delivery that keeps failing becomes a dead letter. See [outbound webhooks](../reference/integration-resources.md#outbound-webhooks). | M17 |
| **Endpoint** | An `endpoint` resource that lets another system, signed in with a bearer token, create a record and start a process on it in one call. It has typed input fields, the entity it creates, a mapping from input to fields and the process it starts. It can return output fields when the process completes within a short wait. The resource, its compile checks, the call with its output and wait, its idempotency key, and the status location are built. See [endpoints](../reference/integration-resources.md#endpoint-resource). | M5 |

**Trigger kinds:**

- **Data change:** a record was created or updated, optionally only when
  named fields changed. Delete, which starts a process without a subject, is
  built.
- **Event:** a named event was raised.
- **Schedule:** a cron schedule in an IANA time zone. Without a data source
  it starts a process without a subject once, a scheduled job. With one, it
  fans out over the records of a data source.

Inbound endpoints are their own resource, not a trigger kind. See
[D27](../decisions/d27-inbound-endpoints.md).

## Presentation

| Concept | Meaning | From |
| --- | --- | --- |
| **Site** | An entry point of one application, with its own path, title, locales (default, fallback and available) and navigation. Theme, identity provider binding and domain come later. | M1 |
| **Page** | A route in a site. It has a title, and its content is its widgets: a tree of widgets, drawn in order (M12). A page has no entity or template of its own. It can declare typed parameters, filled by navigate actions (M12). | M1, M12 (widget tree, parameters) |
| **Widget** | A UI block on a page. M1 has `table` and `form` widgets over one entity, a `table` may bind a data source instead of an entity (M2), and a table opens and creates records through navigate actions (M12). M3 adds the `taskInbox` widget, which lists the user's open tasks. The process inspector widget, which lists the instances of one process and shows one, is built in M7. M12 adds the container widgets `columns`, `tabs` and `section`, which hold other widgets and are drawn by the SPA. M12 adds `detail`, `chart` and `kpi`, see [D64](../decisions/d64-detail-chart-and-kpi-widgets.md). A chart and a KPI bind page parameters into their data source with the name map of a table (M13). A list widget comes later. Custom widgets are planned for M17. | M1, M12 (containers, detail, chart and KPI widgets), M13 (chart and KPI page parameters), M17 (custom widgets) |
| **Custom widget** | A React component in an extension package that a page names as `<package>/<name>`, with typed properties. It builds on the widget SDK, never on Ant Design. It runs in the SPA with the user's session, so it is trusted code. The page designer's palette lists the widgets of the packages an application declares. See [custom widgets](../reference/custom-widgets.md). | M17 |
| **Form** | Fields laid out in sections and steps, with visibility, enabled and required conditions, validation rules, child collections (line items), lookups and file upload. M1 has a basic form over all of an entity's fields. M3 builds the `form` resource: sections of fields, any of them read-only, used by both pages and tasks. Conditional visible, required and read-only states, validation as the user types and wizard steps are built in M12. The form schema, compile checks, metadata, the record and task form state endpoints, the SPA's form widget conditions and the task page's conditions are built, and so are the wizard steps on form pages and task pages ([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)). Refusing a value for a hidden or read-only field in the task API is built in M13. File upload in forms is built, see [file fields](../reference/file-fields.md#in-the-spa). See [frontend](../reference/frontend.md). | M1 (basic), M3 (form resource), M12 (states, steps), M13 (task API refusal), M15 (file upload) |
| **Action** | A user command on a page or widget: start a process, complete a task, run an operation or navigate. Actions are always authorized on the server. M3 builds the `startProcess` action on a form widget and the outcomes of a task. Run operation comes later. Navigate actions with page parameters are built in M12, see [D63](../decisions/d63-navigate-actions-and-page-parameters.md). Table row and toolbar actions, delete included, are built in M12, see [D59](../decisions/d59-table-row-and-toolbar-actions.md). A delete of a record that a running or waiting process instance works on is refused with `409`. Page metadata lists them, and the SPA shows their buttons on table rows, the table toolbar and saved forms. | M3, M12 (navigate, row and toolbar actions), M13 (delete refusal) |
| **Theme** | Design tokens for light and dark mode, shared by every site. M1 has the shared tokens. A site will be able to adjust tokens later, but never add per-page styling. | M1 (shared tokens), later (site adjustments) |
| **Text resource** | A localized text with a stable key. Every UI string comes from text resources. An application has one `text` resource per locale, and every locale has the same keys. A key used by a label must exist in every locale. Texts are never shared across applications. Each release carries its own texts, so removing a key in a newer release does not affect work still running on an older one. | M1 (basic), M6 (full) |

## Security and operations

| Concept | Meaning | From |
| --- | --- | --- |
| **Role** | A declared `role` resource with a name and a label. A task assigned to a role, and each role a policy names, must name a declared role. Users hold roles through the identity provider's roles claim, and a claim value that names no declared role is ignored. Policies give roles their grants. Every application endpoint matches the claim values. See [roles and policies](../reference/roles-and-policies.md), and [authorization](../reference/security.md#authorization). | M4 |
| **Policy** | A `policy` resource that gives one or more roles grants on entities, data sources and processes, and `invoke` on endpoints. An entity grant may have a record filter and a list of fields. The grants of all the user's policies combine with OR, and default-deny applies. The resource and its compile checks are built, `endpoint` grants included. The record API, the data source endpoint, the process start endpoint and the endpoint call enforce its grants, and the record API and the data source endpoint also enforce its record filters and field lists. Site navigation and page metadata follow a signed-in user's grants, and page metadata leaves out the fields the user may not read. See [roles and policies](../reference/roles-and-policies.md), and [authorization](../reference/security.md#authorization). | M4 |
| **Author** | An installation-level role, not a `role` resource. The setting `Studio:AuthorRole` names the roles claim value. A user who holds it may use Studio. With no value, nobody may. Application policies play no part. See [Studio settings](../reference/host-settings.md#studio-settings). | M8 |
| **Publisher role** | An installation-level role, not a `role` resource. The setting `Studio:PublisherRole` names the roles claim value. A user who holds it and the author role may publish from Studio and roll back. With no value, nobody may. The host that runs the requests is the command `axis-admin publisher`, see [administration](../reference/administration.md#publisher). Publishing and rollback are built. | M10 |
| **Operator console** | The `/console` page where the platform operator of a tenant sees failed work, dead letters, webhook deliveries, schedules and publishing across every application, and runs retry, cancel, resolve, redeliver and discard. The installation-level setting `Console:OperatorRole` names the roles claim value. Application policies play no part. See [operator console](../reference/operator-console.md). | M17 |
| **Tenant** | An isolated customer scope with its own database, users, applications and settings. | M1 (context), M9 (management) |
| **Tenant directory** | The list of tenants in the platform database: each tenant's status, hosts, database, sign-in settings and deployment bindings, with secrets stored encrypted. The server and the worker read it and pick up changes without a restart. Host configuration stays the default source. See [D42](../decisions/d42-tenant-directory-in-the-platform-database.md). | M9 |
| **Preview tenant** | A tenant that belongs to one main tenant, where an author tries an application before publishing it. It has its own database, hosts and deployment bindings, and its data never mixes with the main tenant's. Its bindings are empty by default, so it never calls a real system by accident. It signs people in with its main tenant's sign-in settings. See [preview tenants](../architecture.md#preview-tenants) and [D48](../decisions/d48-preview-tenants.md). Built under the `configuration` and `directory` tenant sources, with `PreviewOf`, sign-in and bindings. The publisher runs preview requests on it, and the Studio **Preview** button and its route send them. Each tenant keeps its own status. | M10 |
| **Deployment binding** | Environment-specific settings that a release resolves at run time, held as references and never as values in configuration. Examples: database, storage, identity provider, connector endpoints and secrets. M5 has two kinds, `url` and `secret`. M15 built `storage`, for the files of file fields, whose only value is `database`. Their values are set per tenant in host configuration, with `tenant binding set --storage database`, or in the tenant directory under `Tenancy:Source=directory`. See [deployment bindings](../reference/integration-resources.md#deployment-bindings). | M5, M15 (storage) |
| **Audit record** | An append-only record of a consequential action, committed in the same transaction as the action. It holds the time, the actor, the action, the application, the entity, the record, the process instance and a details object. It also holds the actor's display name at the time of the action. The database rejects every update and delete. See [storage](../reference/storage.md#audit-records-and-sequence-counters). | M3 |
| **Platform audit record** | An append-only record of an `Axis.Admin` command that changes the tenant directory, a tenant database, a key or a password, or backs up a tenant. It holds the time, the command, the tenant, the operating system user and machine, and the names of what changed, never a secret value. It lives in the platform database. See [platform audit records](../reference/tenant-setting-commands.md#platform-audit-records) and [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md). The commands that exist write them. The records of the tenant setting commands are *(planned for M16)*. | M16 |
| **Service account** | A client of the tenant's identity provider that calls the record API, data source rows, process start and process inspection, and later the operator commands, with a bearer token from the client credentials grant. It has roles from the roles claim, and policies apply to it as to a user. It never completes a task. See [service accounts](../reference/security.md#service-accounts) and [D86](../decisions/d86-service-accounts-for-the-record-and-process-apis.md). The record API, data source rows, process start and process inspection are built. The operator commands are *(planned for M16)*. | M16 |
| **Sign-in profile** | A tenant setting, `standard` or `fapi2`. Under `fapi2` sign-in uses the FAPI 2.0 Security Profile, and machine callers must send DPoP-bound tokens, which is built. See [FAPI 2.0 profile](../reference/security.md#fapi-20-profile) and [D83](../decisions/d83-the-fapi-20-profile-per-tenant.md). Sign-in under `fapi2` is built. | M16 |
| **Test scenario** | A `scenario` resource. It is a configuration-level test that runs against its own temporary tenant database. It applies the seed data, runs actions as given users with stubbed connector and package calls, and asserts record, task and process outcomes, including denied actions. It is compiled with the application, and its names and values are checked, but it is not part of its release. The runner runs record actions, process starts, task completions and the `expect` steps, and runs the process steps, the connector and package calls and the event deliveries after each action. Stubs answer the calls. Schedule triggers run through the `advanceTime` step, which moves `now` forward and runs every due work item. Studio runs scenarios through a `test` publish request and the publisher, see [D55](../decisions/d55-running-test-scenarios-from-studio.md). | M6 |
