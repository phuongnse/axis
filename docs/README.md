# Documentation

This is the map of the Axis docs. Each topic has one home, and other docs link to it rather than
repeat it. Find the file for your topic here, then read only the sections you need. How the docs are
kept current and how they are split is in
[keeping the docs current](delivery.md#keeping-the-docs-current).

## Product and design

| Doc                               | What it holds                                                    | Read it when                                                |
| --------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------- |
| [Product brief](product-brief.md) | What Axis is, who uses it, the proving samples                   | You do product or architecture work                         |
| [Roadmap](roadmap.md)             | Milestones, what each delivers and its status                    | You need the current milestone or a milestone's scope       |
| [Decisions](decisions.md)         | The index of decisions, each in its own file under `decisions/`  | A change touches a design choice; read only those decisions |
| [Concepts](domain/concepts.md)    | The vocabulary and what each resource does                       | You name anything in code, APIs, UI or issues               |
| [Knowledge](domain/knowledge.md)  | What business applications need and the pitfalls to avoid        | You change configuration, execution, storage or security    |
| [Architecture](architecture.md)   | Modules, hosts, tenancy, security, execution, telemetry, testing | You add a module or host, or change how they fit together   |

## Reference

Detailed behaviour, one topic per file. Sections marked _(planned for Mx)_ are not built yet.

| Doc                                                                    | What it holds                                                                         |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| [Configuration pipeline](reference/configuration.md)                   | Load, resolve, check, release and activate an application folder; the reference index |
| [Diagnostic codes](reference/diagnostic-codes.md)                      | The shape of a diagnostic and the table of every `AXCnnnn` code                       |
| [Resource files](reference/resource-files.md)                          | The resource file shape, schema keywords, entity fields, entity logic, sequences      |
| [Roles and policies](reference/roles-and-policies.md)                  | The `role` and `policy` resources, the record creator, sensitive fields               |
| [Connectors, events and endpoints](reference/integration-resources.md) | Connectors and deployment bindings, the `event`, `trigger` and `endpoint` resources   |
| [Modules and templates](reference/modules-and-templates.md)            | Reusable modules and application templates                                            |
| [Extension packages](reference/extension-packages.md)                  | Custom C# code: manifest, contracts, installation, loading, capabilities              |
| [Test scenarios](reference/test-scenarios.md)                          | The `scenario` resource and how scenarios run                                         |
| [Expressions](reference/expressions.md)                                | The expression language, its functions and its SQL translation                        |
| [Data sources](reference/data-sources.md)                              | The `dataSource` resource and its endpoint                                            |
| [Record API](reference/record-api.md)                                  | Record routes, values, paging, errors, child rows, audit history                      |
| [Inbound endpoint calls](reference/inbound-endpoints.md)               | How another system calls an inbound endpoint: checks, responses, idempotency          |
| [Processes](reference/processes.md)                                    | The `process` resource, steps, compile checks, start endpoint, states, process tables |
| [Task API](reference/task-api.md)                                      | Listing, reading and completing tasks                                                 |
| [Process execution](reference/process-execution.md)                    | How the worker runs steps, connector calls, package operations, worker settings       |
| [Process events and triggers](reference/process-events.md)             | Raised events, starts from triggers, event delivery, schedules                        |
| [Process inspection](reference/process-inspection.md)                  | Inspecting instances and the operator commands retry, cancel and resolve              |
| [Process inspector widget](reference/process-inspector-widget.md)      | The SPA widget that lists and shows the instances of one process                      |
| [Task inbox widget](reference/task-inbox-widget.md)                    | The SPA widget that lists the user's open tasks, and the task page                    |
| [Form widget conditions and steps](reference/form-widget-conditions.md) | How the SPA form widget follows the form state endpoint, and its wizard steps          |
| [Container widgets](reference/container-widgets.md)                    | How the SPA draws the columns, tabs and section widgets, and keeps the selected tab   |
| [Detail, chart and KPI widgets](reference/detail-chart-kpi-widgets.md) | The SPA widgets that show one record, a chart and a KPI tile                          |
| [Storage](reference/storage.md)                                        | Physical names, column types, database roles, schema planning, audit, release history |
| [Migrations](reference/migrations.md)                                  | `migration` resources and recomputing stored computed fields                          |
| [Security](reference/security.md)                                      | Sign-in and the session, machine callers, authorization, development users            |
| [Frontend](reference/frontend.md)                                      | The SPA, sites, pages and widgets                                                     |
| [Studio](reference/studio.md)                                          | The Studio page: editors, designers, publishing, tests                                |
| [Authoring API](reference/authoring.md)                                | The API that Studio uses to read, write and publish application folders               |
| [Host settings](reference/host-settings.md)                            | Startup activation and the server, Studio, tenancy, admin and telemetry settings      |
| [Administration](reference/administration.md)                          | The `Axis.Admin` commands: tenants, backup, restore, migrate, activate, publisher     |
| [Installation](reference/installation.md)                              | Production images and installs on premises and in the cloud                           |

## Working on Axis

| Doc                           | What it holds                                                                |
| ----------------------------- | ---------------------------------------------------------------------------- |
| [AGENTS.md](../AGENTS.md)     | The working rules and the commands, for people and agents                    |
| [Development](development.md) | Running Axis locally, the scripts, test reports, database roles, realm users |
| [Delivery](delivery.md)       | How work flows through NexKit, and how the docs are kept current             |
