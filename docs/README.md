# Documentation

This is the map of the Axis docs. Each topic has one home, and other docs link to it rather than
repeat it. Find the file for your topic here, then read only the sections you need. How the docs are
kept current and how they are split is in
[keeping the docs current](delivery.md#keeping-the-docs-current).

## Product and design

| Doc                               | What it holds                                                    | Read it when                                                |
| --------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------- |
| [Product brief](product-brief.md) | What Axis is, who uses it, the proving samples                   | You do product or architecture work                         |
| [Roadmap](roadmap.md)             | Milestones in their phases, what each delivers and its status    | You need the current milestone or a milestone's scope       |
| [Decisions](decisions.md)         | The index of decisions, each in its own file under `decisions/`  | A change touches a design choice; read only those decisions |
| [Concepts](domain/concepts.md)    | The vocabulary and what each resource does                       | You name anything in code, APIs, UI or issues               |
| [Knowledge](domain/knowledge.md)  | What business applications need and the pitfalls to avoid        | You change configuration, execution, storage or security    |
| [Architecture](architecture.md)   | Modules, hosts, tenancy, security, execution, telemetry, testing | You add a module or host, or change how they fit together   |
| [Repository layout](repository-layout.md) | The folders of the repository and the rules that modules follow | You add a project, a folder or a module |

## Reference

Detailed behaviour, one topic per file. Sections marked _(planned for Mx)_ are not built yet.

| Doc                                                                    | What it holds                                                                         |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| [Configuration pipeline](reference/configuration.md)                   | Load, resolve, check, release and activate an application folder; the reference index |
| [Diagnostic codes](reference/diagnostic-codes.md)                      | The shape of a diagnostic and the table of every `AXCnnnn` code                       |
| [Resource files](reference/resource-files.md)                          | The resource file shape, schema keywords, entity fields, entity logic, sequences      |
| [Roles and policies](reference/roles-and-policies.md)                  | The `role` and `policy` resources, the record creator, sensitive fields               |
| [Connectors, events and endpoints](reference/integration-resources.md) | Connectors and deployment bindings, the `event`, `trigger` and `endpoint` resources |
| [Outbound webhooks](reference/outbound-webhooks.md)                    | The `webhook` resource, its compile checks, and the signed delivery of an event       |
| [Modules and templates](reference/modules-and-templates.md)            | Reusable modules and application templates                                            |
| [Module contracts](reference/module-contracts.md)                      | The `contract` resource: lifecycles, core validations, effects, grant ceilings, relax entries and contract scenarios |
| [Customization layers](reference/customization-layers.md)              | The `layer` resource: changes to a module by keys, list keys, extending a lifecycle, and Studio writing the layer |
| [Extension packages](reference/extension-packages.md)                  | Custom C# code: manifest, contracts, installation, loading, capabilities              |
| [Test scenarios](reference/test-scenarios.md)                          | The `scenario` resource and how scenarios run                                         |
| [Expressions](reference/expressions.md)                                | The expression language, its functions and its SQL translation                        |
| [Data sources](reference/data-sources.md)                              | The `dataSource` resource and its endpoint                                            |
| [Record API](reference/record-api.md)                                  | Record routes, values, paging, errors, child rows, audit history                      |
| [File fields](reference/file-fields.md)                                | The `file` field type: values, upload, download, storage and clean-up                |
| [Import and export](reference/import-export.md)                        | Exporting records to CSV and spreadsheets, and importing them under policies          |
| [Inbound endpoint calls](reference/inbound-endpoints.md)               | How another system calls an inbound endpoint: checks, responses, idempotency          |
| [Processes](reference/processes.md)                                    | The `process` resource, steps, compile checks, start endpoint, states                 |
| [Process tables](reference/process-tables.md)                          | The tables that `Axis.Processes` owns                                                 |
| [Process states](reference/process-states.md)                          | The states of a process instance and of a task                                        |
| [Task API](reference/task-api.md)                                      | Listing, reading and completing tasks                                                 |
| [Process execution](reference/process-execution.md)                    | How the worker runs steps, connector calls, package operations, worker settings       |
| [Process events and triggers](reference/process-events.md)             | Events, triggers, delivery, schedules, scheduled jobs, delete triggers, dead letters  |
| [Process flow](reference/process-flow.md)                              | Timer, wait for event and sub-process steps, loops, create record, no-subject processes |
| [Task deadlines and claims](reference/task-deadlines.md)               | Task escalations and deadlines, overdue tasks, claim, release and reassign            |
| [Notifications](reference/notifications.md)                            | The `notify` operation, in-app and email notifications, user profiles, mail settings  |
| [Process inspection](reference/process-inspection.md)                  | Inspecting instances and the operator commands retry, cancel and resolve              |
| [Operator console](reference/operator-console.md)                      | The web page where the platform operator sees and fixes failed work in a tenant       |
| [Process inspector widget](reference/process-inspector-widget.md)      | The SPA widget that lists and shows the instances of one process                      |
| [Task inbox widget](reference/task-inbox-widget.md)                    | The SPA widget that lists the user's open tasks, and the task page                    |
| [Form resource and form widget](reference/form-widget.md)              | The form resource and the SPA widget that creates and edits one record                |
| [Form widget conditions and steps](reference/form-widget-conditions.md) | How the SPA form widget follows the form state endpoint, and its wizard steps          |
| [Container widgets](reference/container-widgets.md)                    | How the SPA draws the columns, tabs and section widgets, and keeps the selected tab   |
| [Detail, chart and KPI widgets](reference/detail-chart-kpi-widgets.md) | The SPA widgets that show one record, a chart and a KPI tile                          |
| [Custom widgets](reference/custom-widgets.md)                          | Widgets from extension packages: manifest, page shape, loading, the SDK, the palette  |
| [Storage](reference/storage.md)                                        | Physical names, column types, database roles, schema planning, audit, release history |
| [Child collections in storage](reference/child-tables-and-computed-columns.md) | How child tables and computed columns are stored                              |
| [Migrations](reference/migrations.md)                                  | `migration` resources and recomputing stored computed fields                          |
| [Security](reference/security.md)                                      | Sign-in and the session, machine callers, service accounts, the FAPI 2.0 profile, authorization, development users |
| [Axis's identity provider](reference/identity-provider.md)           | The identity provider Axis ships: realms, the `identityProvider` resource, sign-in per site, platform sign-in, the Axis theme, installation |
| [User administration](reference/user-administration.md)              | The administrator role, the users page, accounts and their MFA, disabled users, and the roles an administrator gives |
| [Security headers and rate limits](reference/headers-and-rate-limits.md) | The headers the server sets on every response, and the rate limits per partition      |
| [Keys and data protection](reference/keys-and-data-protection.md) | The key policy, the cryptographic inventory and crypto agility, and the data protection levels |
| [Data encryption](reference/data-encryption.md) | Encrypted `sensitive` field values, exact lookup, tenant data keys and their rotation, encrypted backups |
| [ASVS](reference/asvs.md)                                              | The ASVS 5.0 Level 2 tracking: each requirement, its status and its evidence          |
| [Fuzz tests](reference/fuzz-tests.md)                                  | The parsers that get fuzz tests, how they are fuzzed and how to replay a failure      |
| [Threat model](reference/threat-model.md)                              | What can go wrong in Axis's own design, and each threat's status                      |
| [Threat model method](reference/threat-model-method.md)                | How the threat model is filled: the methods, the rows, the ratings and the statuses    |
| [Log inventory](reference/log-inventory.md)                            | Every log Axis writes: events, format, destination, readers, retention and the security events |
| [Performance](reference/performance.md)                                | The service level objectives, the reference environment and the load test             |
| [Frontend](reference/frontend.md)                                      | The SPA, sites, pages and widgets                                                     |
| [Shell and home page](reference/shell.md)                             | The shell around every page, navigation icons, the page header, the platform home page and the sign-in cards      |
| [Platform pages](reference/platform-pages.md)                        | The sessions screen at `/account` and the users page at `/users`                      |
| [Studio](reference/studio.md)                                          | The Studio page: editors, designers, publishing, tests                                |
| [Entity designer](reference/entity-designer.md)                        | The Form tab of the entity editor: fields, settings, indexes, validations                 |
| [Form designer](reference/form-designer.md)                            | The Design tab of the form editor: canvas, field palette, layout outline, moves       |
| [Process designer](reference/process-designer.md)                      | The Design tab of the process editor: palette, transitions, removes, keyboard         |
| [Authoring API](reference/authoring.md)                                | The API that Studio uses to read, write and publish application folders               |
| [Draft page metadata](reference/draft-page-metadata.md)               | The routes that answer the page metadata of a draft page or form and of a module page  |
| [Host settings](reference/host-settings.md)                            | Startup activation and the server, Studio, tenancy, data and file, admin, rate limit and telemetry settings |
| [Administration](reference/administration.md)                          | The `Axis.Admin` commands: create a tenant, migrate, activate, publisher |
| [Tenant backup and restore](reference/tenant-backup-and-restore.md)    | `tenant backup` and `tenant restore`: the encrypted archive, the backup key, the manifest, the checks and the steps, and daily backups in a cell |
| [Tenant setting commands](reference/tenant-setting-commands.md)        | The `Axis.Admin` commands that change bindings, sign-in settings, mail settings and hosts, rotate keys and passwords, end sessions, and the platform audit records |
| [Tenant status commands](reference/tenant-status-commands.md)           | The `Axis.Admin` commands that suspend and resume a tenant and list the tenants       |
| [Cells](reference/cells.md)                                            | Axis SaaS cells and regions, tenant hosts and certificates, PgBouncer connections, cell size |
| [Tenant quotas and usage](reference/tenant-quotas.md)                  | The quotas of a tenant in a cell, their refusals, work at once and the measured usage  |
| [Worker scheduling](reference/worker-scheduling.md)                    | How the worker shares its time between tenants, and wakes an idle tenant               |
| [Tenant lifecycle in a cell](reference/tenant-lifecycle.md)            | The cell's provisioning API: create a tenant, hosts, the export ZIP, deletion          |
| [Cell console](reference/cell-console.md)                              | The page where the platform operator sees and provisions every tenant of a cell, and its operator realm |
| [Installation](reference/installation.md)                              | Production images and installs on premises and in the cloud                           |
| [Health checks](reference/health-checks.md)                            | The server's and the worker's `/health/live` and `/health/ready` answers and when each status holds |

## Working on Axis

| Doc                           | What it holds                                                                |
| ----------------------------- | ---------------------------------------------------------------------------- |
| [AGENTS.md](../AGENTS.md)     | The working rules and the commands, for people and agents                    |
| [Development](development.md) | Running Axis locally, the scripts, dependency updates and scans, test reports, database roles, realm users |
| [Delivery](delivery.md)       | How work flows through NexKit, and how the docs are kept current             |
