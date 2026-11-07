# Documentation

This is the map of the Axis docs. Each topic has one home, and other docs link to it rather than
repeat it. Find the area you work in, then the page, then read only the sections you need. What goes in
which doc, how to write it and how the docs stay correct is in [writing the docs](writing-docs.md).

## Product and design

| Doc                                       | What it holds                                                             | Read it when                                                |
| ----------------------------------------- | ------------------------------------------------------------------------- | ----------------------------------------------------------- |
| [Product brief](product-brief.md)         | What Axis is, who uses it, the proving samples                            | You do product or architecture work                         |
| [Roadmap](roadmap.md)                     | Milestones in their phases, what each delivers and its status             | You need the current milestone or a milestone's scope       |
| [Decisions](decisions.md)                 | The index of decisions, each in its own file under `decisions/`           | A change touches a design choice; read only those decisions |
| [Concepts](domain/concepts.md)            | The vocabulary and what each resource does                                | You name anything in code, APIs, UI or issues               |
| [Knowledge](domain/knowledge.md)          | What business applications need and the pitfalls to avoid                 | You change configuration, execution, storage or security    |
| [Architecture](architecture.md)           | Context, containers, runtime and deployment, tenancy, security, telemetry | You add a module or host, or change how they fit together   |
| [Repository layout](repository-layout.md) | The folders of the repository and the rules that modules follow           | You add a project, a folder or a module                     |

## Reference

Detailed behaviour, one topic per file. Pages are grouped by the area a reader works in, and the files all stay in `reference/`. Sections marked _(planned for Mx)_ are not built yet.

### Authoring

Read by people who write an application's configuration by hand, or change how Axis checks and releases it.

| Doc                                                         | What it holds                                                                                                                | Read it when                                                        |
| ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| [Configuration pipeline](reference/configuration.md)        | Load, resolve, check, release and activate an application folder; the reference index                                        | You change how an application folder is loaded, checked or released |
| [Diagnostic codes](reference/diagnostic-codes.md)           | The shape of a diagnostic and the table of every `AXCnnnn` code                                                              | You add or change a diagnostic code                                 |
| [Resource files](reference/resource-files.md)               | The resource file shape, schema keywords, entity fields, entity logic, sequences, matched references                         | You write or change the fields of a resource file                   |
| [Form conditions](reference/form-conditions.md)             | The conditions and wizard steps of a `form` resource                                                                         | You add conditions or wizard steps to a form resource               |
| [Roles and policies](reference/roles-and-policies.md)       | The `role` and `policy` resources, the record creator, sensitive fields                                                      | You change roles, policies or sensitive fields                      |
| [Modules and templates](reference/modules-and-templates.md) | Reusable modules and application templates                                                                                   | You add or change a module or an application template               |
| [Module contracts](reference/module-contracts.md)           | The `contract` resource: lifecycles, core validations, effects, grant ceilings, relax entries and contract scenarios         | You write or change a contract resource                             |
| [Customization layers](reference/customization-layers.md)   | The `layer` resource: changes to a module by keys, list keys, extending a lifecycle, and Studio writing the layer            | You change a module through a customization layer                   |
| [Built-in modules](reference/built-in-modules.md)           | The `organization` and `approvals` modules, the `approvals` application, request types, delegation and the approval contract | You change a built-in module or the approvals application           |
| [Test scenarios](reference/test-scenarios.md)               | The `scenario` resource and how scenarios run                                                                                | You write a scenario or change how scenarios run                    |
| [Expressions](reference/expressions.md)                     | The expression language, its functions and its SQL translation                                                               | You change the expression language or its SQL translation           |
| [Data sources](reference/data-sources.md)                   | The `dataSource` resource and its endpoint                                                                                   | You add or change a data source or its endpoint                     |
| [Migrations](reference/migrations.md)                       | `migration` resources and recomputing stored computed fields                                                                 | You add a migration or recompute stored computed fields             |

### Runtime and processes

Read by people who change how records, files, processes, tasks and notifications run.

| Doc                                                        | What it holds                                                                            | Read it when                                                           |
| ---------------------------------------------------------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Record API](reference/record-api.md)                      | Record routes, values, paging, errors, child rows                                        | You add or change a record route, value, page or error                 |
| [Record audit](reference/record-audit.md)                  | The audit records every record write leaves, and the history endpoint                    | You change what a record write audits or the history endpoint          |
| [File fields](reference/file-fields.md)                    | The `file` field type: values, upload, download, storage and clean-up                    | You change a file field's upload, download or clean-up                 |
| [Import and export](reference/import-export.md)            | Exporting records to CSV and spreadsheets, and importing them under policies             | You change CSV or spreadsheet import and export                        |
| [Processes](reference/processes.md)                        | The `process` resource, steps, compile checks, start endpoint, releases and running work | You add or change a process resource or its releases                   |
| [Process states](reference/process-states.md)              | The states of a process instance and of a task                                           | You change the states of a process instance or a task                  |
| [Task API](reference/task-api.md)                          | Listing, reading and completing tasks                                                    | You change how tasks are listed, read or completed                     |
| [Process execution](reference/process-execution.md)        | How the worker runs steps, connector calls, package operations, worker settings          | You change how the worker runs steps or connector calls                |
| [Process events and triggers](reference/process-events.md) | Events, triggers, delivery, schedules, scheduled jobs, delete triggers, dead letters     | You change events, triggers, schedules or dead letters                 |
| [Process flow](reference/process-flow.md)                  | Timer, wait for event and sub-process steps, loops, create record, no-subject processes  | You add or change a timer, wait, sub-process or loop step              |
| [Task deadlines and claims](reference/task-deadlines.md)   | Task escalations and deadlines, overdue tasks, claim, release and reassign               | You change escalations, deadlines, claims or reassignment              |
| [Notifications](reference/notifications.md)                | The `notify` operation, in-app and email notifications, user profiles, mail settings     | You change the notify operation or notification settings               |
| [Process inspection](reference/process-inspection.md)      | Inspecting instances and the operator commands retry, cancel and resolve                 | You change how instances are inspected, retried, cancelled or resolved |

### Integration and extension packages

Read by people who connect Axis to another system or write a package of custom code.

| Doc                                                                    | What it holds                                                                        | Read it when                                                   |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------ | -------------------------------------------------------------- |
| [Connectors, events and endpoints](reference/integration-resources.md) | Connectors and deployment bindings, the `event`, `trigger` and `endpoint` resources  | You add a connector, an event, a trigger or an endpoint        |
| [Outbound webhooks](reference/outbound-webhooks.md)                    | The `webhook` resource, its compile checks, and the signed delivery of an event      | You add or change a webhook or its signed delivery             |
| [Inbound endpoint calls](reference/inbound-endpoints.md)               | How another system calls an inbound endpoint: checks, responses, idempotency         | You change how an inbound endpoint call is checked or answered |
| [Extension packages](reference/extension-packages.md)                  | Custom C# code: manifest, contracts, installation, loading, capabilities             | You write or load a package of custom C# code                  |
| [Custom widgets](reference/custom-widgets.md)                          | Widgets from extension packages: manifest, page shape, loading, the SDK, the palette | You write a widget for an extension package                    |

### Studio and screens

Read by people who change Studio, the screens of the SPA or the widgets they draw.

| Doc                                                                           | What it holds                                                                                                                 | Read it when                                                           |
| ----------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Frontend](reference/frontend.md)                                             | The SPA, sites, pages and widgets                                                                                             | You change the SPA's sites, pages or widgets                           |
| [Shared widgets](reference/shared-widgets.md)                                 | Which shared widget does which job, and what the build checks                                                                 | You add or change a shared widget, or check what the build enforces    |
| [Widget previews](reference/widget-previews.md)                               | The design system's live previews of the widgets: the bundle, the cards and the spec                                          | You add or change a widget preview, or the design system bundle        |
| [UI writing](reference/ui-writing.md)                                         | How the words of Axis's screens are written in every locale, and the glossary of terms                                        | You write or change screen text in any locale or the glossary          |
| [Actions and controls](reference/actions-and-controls.md)                     | Button levels and places, menus, and how every form draws field errors and choices                                            | You add a button, menu, form control or field error                    |
| [Shell and home page](reference/shell.md)                                     | The shell around every page, navigation icons, the page header, the platform home page and the sign-in cards                  | You change the shell, navigation, page header or home page             |
| [Platform pages](reference/platform-pages.md)                                 | The sessions screen and account page at `/account` and the users page at `/users` and a user's page                           | You change the sessions, account or users page                         |
| [Legal links and terms](reference/legal.md)                                   | Privacy and terms links per tenant and site, the legal line, `/licences` and accepting the terms                              | You change privacy and terms links or accepting the terms              |
| [Form resource and form widget](reference/form-widget.md)                     | The form resource and the SPA widget that creates and edits one record                                                        | You change the form widget that creates or edits a record              |
| [Form widget conditions and steps](reference/form-widget-conditions.md)       | How the SPA form widget follows the form state endpoint, and its wizard steps                                                 | You change how the form widget follows form state or its steps         |
| [Container widgets](reference/container-widgets.md)                           | How the SPA draws the columns, tabs and section widgets, and keeps the selected tab                                           | You change the columns, tabs or section widgets                        |
| [Detail, chart and KPI widgets](reference/detail-chart-kpi-widgets.md)        | The SPA widgets that show one record, a chart and a KPI tile                                                                  | You change the record detail, chart or KPI widget                      |
| [Process inspector widget](reference/process-inspector-widget.md)             | The SPA widget that lists and shows the instances of one process                                                              | You change the widget that lists a process's instances                 |
| [Task inbox widget](reference/task-inbox-widget.md)                           | The SPA widget that lists the user's open tasks, and the task page                                                            | You change the task inbox or the task page                             |
| [Studio](reference/studio.md)                                                 | The Studio page: editors, designers, publishing, tests                                                                        | You change the Studio page, its editors, designers or publishing       |
| [Studio workspace](reference/studio-workspace.md)                             | Studio's list of applications, an open application's sidebar, band and Overview                                               | You change Studio's application list, sidebar, band or Overview        |
| [Studio resource form](reference/studio-resource-form.md)                     | The form that edits a resource, its save bar, its references and its JSON view                                                | You change the form that edits a resource in Studio                    |
| [Entity designer](reference/entity-designer.md)                               | The Form tab of the entity editor: fields, settings, indexes, validations, migrations                                         | You change the Form tab of the entity editor                           |
| [Form designer](reference/form-designer.md)                                   | The Design tab of the form editor: canvas, field palette, layout outline, moves                                               | You change the Design tab of the form editor                           |
| [Process designer](reference/process-designer.md)                             | The Design tab of the process editor: palette, transitions, removes, keyboard                                                 | You change the Design tab of the process editor                        |
| [Authoring API](reference/authoring.md)                                       | The API that Studio uses to read, write and publish application folders                                                       | You change a route that Studio uses to read, write or publish a folder |
| [Revisions and drafts in the authoring API](reference/authoring-revisions.md) | The routes that list a folder's revisions, show one, undo it and show a draft's diff                                          | You change the routes for revisions, undo or a draft's diff            |
| [Policy matrix in the authoring API](reference/authoring-policy-matrix.md)    | The route that answers what each role may do on each target                                                                   | You change the route that answers what each role may do                |
| [Creating applications](reference/authoring-applications.md)                  | The route that creates an application folder, its order of checks and what it writes                                          | You change how an application folder is created                        |
| [Draft page metadata](reference/draft-page-metadata.md)                       | The routes that answer the page metadata of a draft page or form, of a module page and of a draft module form                 | You change the page metadata routes for draft and module pages         |
| [Module resource saves](reference/module-resource-saves.md)                   | The routes that save an edited module resource as a customization layer with only the difference, and reset one element of it | You change how a module resource is saved as a layer                   |

### AI authoring

Read by people who set up or change how an agent authors applications.

| Doc                                                           | What it holds                                                                             | Read it when                                                      |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| [MCP server](reference/mcp-server.md)                         | The MCP server for agents: route, sign-in, client registration, tools, limits and audit   | You change the MCP server route, tools, limits or audit           |
| [Authoring requests](reference/authoring-requests.md)         | The agent host and the request loop: states, plans, checks, review, budgets and accepting | You change the agent request loop, its states or checks           |
| [Revisions and drafts](reference/revisions-and-drafts.md)     | The git repository of each application folder: revisions, drafts, accepts and undo        | You change how a folder's git history records drafts and accepts  |
| [Model bindings](reference/model-bindings.md)                 | The `Ai` settings, model bindings, a tenant's own key and the authoring eval              | You change the Ai settings, model bindings or a tenant's own key  |
| [AI authoring in Studio](reference/ai-authoring-in-studio.md) | The request panel, plan approval, the diff, accept and drop, auto-accept and the history  | You change the request panel, plan approval or the diff in Studio |

### Operations and installation

Read by people who install, run, observe or operate Axis.

| Doc                                                                 | What it holds                                                                                                                                                      | Read it when                                                   |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------- |
| [Installation](reference/installation.md)                           | Production images and installs on premises and in the cloud                                                                                                        | You prepare or change an install on premises or in the cloud   |
| [Host settings](reference/host-settings.md)                         | Startup activation and the server, Studio, tenancy, data and file, admin, rate limit and telemetry settings                                                        | You add or change a host setting                               |
| [Several server replicas](reference/several-server-replicas.md)     | Running the server as several replicas behind a TLS proxy: key ring, forwarded headers, Studio volume                                                              | You run the server as several replicas behind a proxy          |
| [Outbound connections](reference/outbound-connections.md)           | Every connection the server and the worker open, the setting that names its address and the allowlist                                                              | You add or change an outbound connection or the allowlist      |
| [Health checks](reference/health-checks.md)                         | The `/health/live` and `/health/ready` answers of the server, the worker and the agent host, and when each status holds                                            | You change a health check or what its status means             |
| [Telemetry](reference/telemetry.md)                                 | Correlated logs, metrics and traces, the OTLP export, the Axis spans and what telemetry never holds                                                                | You change logs, metrics, traces or the OTLP export            |
| [Log inventory](reference/log-inventory.md)                         | Every log Axis writes: events, format, destination, readers, retention and the security events                                                                     | You add or change a log, its destination or its retention      |
| [Performance](reference/performance.md)                             | The service level objectives, the reference environment and the load test                                                                                          | You change a service level objective or the load test          |
| [Administration](reference/administration.md)                       | The `Axis.Admin` commands: create a tenant, migrate, activate, publisher                                                                                           | You change an Axis.Admin command                               |
| [Tenant setting commands](reference/tenant-setting-commands.md)     | The `Axis.Admin` commands that change bindings, sign-in settings, mail settings and hosts, rotate keys and passwords, end sessions, and the platform audit records | You change a tenant setting command or rotate a key            |
| [Tenant status commands](reference/tenant-status-commands.md)       | The `Axis.Admin` commands that suspend and resume a tenant and list the tenants                                                                                    | You suspend, resume or list tenants                            |
| [Tenant backup and restore](reference/tenant-backup-and-restore.md) | `tenant backup` and `tenant restore`: the encrypted archive, the backup key, the manifest, the checks and the steps, and daily backups in a cell                   | You change backups or restore a tenant                         |
| [Operator console](reference/operator-console.md)                   | The web page where the platform operator sees and fixes failed work in a tenant                                                                                    | You change the operator console or fix failed work in a tenant |

### Axis SaaS

Read by people who run or change the hosted service: cells, tenants, sign-up and billing.

| Doc                                                         | What it holds                                                                                               | Read it when                                                             |
| ----------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| [Cells](reference/cells.md)                                 | Axis SaaS cells and regions, tenant hosts and certificates, PgBouncer connections, cell size                | You add a cell or change its regions, hosts, certificates or connections |
| [Tenant quotas and usage](reference/tenant-quotas.md)       | The quotas of a tenant in a cell, their refusals, work at once and the measured usage                       | You change a tenant's quotas, refusals or measured usage                 |
| [Worker scheduling](reference/worker-scheduling.md)         | How the worker shares its time between tenants, and wakes an idle tenant                                    | You change how the worker shares time between tenants                    |
| [Tenant lifecycle in a cell](reference/tenant-lifecycle.md) | The cell's provisioning API: create a tenant, hosts, the export ZIP, deletion                               | You change the cell's provisioning API                                   |
| [Cell console](reference/cell-console.md)                   | The page where the platform operator sees and provisions every tenant of a cell, and its operator realm     | You change the cell console or its operator realm                        |
| [Control plane](reference/control-plane.md)                 | The service apart from the cells that runs sign-up, trials, plans and billing, and how it reaches the cells | You change the service that runs sign-up, plans and billing              |
| [Region directory](reference/region-directory.md)           | Which cell holds each tenant and host, picking a cell, DNS routing and reserved names                       | You change how a tenant or host is routed to a cell                      |
| [Sign-up](reference/sign-up.md)                             | The public sign-up page, the subdomain, bot checks, email verification, provisioning and terms acceptance   | You change the public sign-up page or its checks                         |
| [Subscriptions](reference/subscriptions.md)                 | A tenant's trial and paid states, the read-only tenant, the dunning timeline and the control plane's emails | You change trial, paid or read-only states or dunning emails             |
| [Plans](reference/plans.md)                                 | The plan catalogue, versions, quotas from a plan, user packs and changing a plan                            | You change the plan catalogue or a plan's quotas                         |
| [Payments](reference/payments.md)                           | Billing invoices, PayOS and VietQR, Paddle, webhooks, the daily reconciliation and the ledger               | You change invoices, payment providers, webhooks or the ledger           |
| [E-invoices](reference/e-invoices.md)                       | Vietnamese e-invoices issued on payment through a provider adapter                                          | You change how e-invoices are issued on payment                          |
| [Billing portal](reference/billing-portal.md)               | The portal where a tenant's owner and billing contacts choose a plan, pay and get invoices                  | You change what the billing portal lets a tenant do                      |

### Security

Read by people who change sign-in, authorization, keys, headers or the threat model.

| Doc                                                                        | What it holds                                                                                                                               | Read it when                                                      |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| [Security](reference/security.md)                                          | Sign-in and the session, machine callers, service accounts, authorization, development users                                                | You change sign-in, the session, machine callers or authorization |
| [Axis's identity provider](reference/identity-provider.md)                 | The identity provider Axis ships: realms, the `identityProvider` resource, sign-in per site, platform sign-in, the Axis theme, installation | You change realms, sign-in per site or the Axis theme             |
| [User administration](reference/user-administration.md)                    | The administrator role, the users page, accounts and their MFA, disabled users, and the roles an administrator gives                        | You change the administrator role or the users page               |
| [FAPI 2.0 profile](reference/fapi2-profile.md)                             | The FAPI 2.0 Security Profile per tenant: PAR, `private_key_jwt`, DPoP and the client key                                                   | You change a tenant's FAPI 2.0 security profile                   |
| [Security headers and rate limits](reference/headers-and-rate-limits.md)   | The headers the server sets on every response, and the rate limits per partition                                                            | You change a response header or a rate limit                      |
| [Keys and data protection](reference/keys-and-data-protection.md)          | The key policy, the cryptographic inventory and crypto agility, and the data protection levels                                              | You change the key policy or a data protection level              |
| [Data encryption](reference/data-encryption.md)                            | Encrypted `sensitive` field values, exact lookup, tenant data keys and their rotation, encrypted backups                                    | You change encrypted fields, tenant data keys or backups          |
| [ASVS](reference/asvs.md)                                                  | The ASVS 5.0 Level 2 tracking: each requirement, its status and its evidence                                                                | You update an ASVS requirement, its status or its evidence        |
| [Fuzz tests](reference/fuzz-tests.md)                                      | The parsers that get fuzz tests, how they are fuzzed and how to replay a failure                                                            | You add a parser fuzz test or replay a fuzz failure               |
| [Threat model](reference/threat-model.md)                                  | What can go wrong in Axis's own design, and each threat's status                                                                            | You add a threat or change a threat's status                      |
| [Threat model: the product](reference/threat-model-product.md)             | The server, worker, SPA, Studio, packages, connectors, identity provider and personal data threats                                          | You assess a threat to the product                                |
| [Threat model: the installation](reference/threat-model-installation.md)   | The images, hosts, databases, backups, secrets and the cell console threats of an install                                                   | You assess a threat to an installation                            |
| [Threat model: the way Axis is built](reference/threat-model-build.md)     | The NexKit, CI and AI agent threats of how Axis is built                                                                                    | You assess a threat to how Axis is built                          |
| [Threat model: the control plane](reference/threat-model-control-plane.md) | The sign-up, cell call, DNS, payment webhook, e-invoice and billing portal threats of Axis SaaS                                             | You assess a threat to the control plane of Axis SaaS             |
| [Threat model method](reference/threat-model-method.md)                    | How the threat model is filled: the methods, the rows, the ratings and the statuses                                                         | You fill in or rate a threat model row                            |

### Platform internals

Read by people who change how Axis stores data.

| Doc                                                                            | What it holds                                                                               | Read it when                                                     |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| [Storage](reference/storage.md)                                                | Physical names, column types, database roles, schema planning, audit                        | You change physical names, column types or database roles        |
| [Child collections in storage](reference/child-tables-and-computed-columns.md) | How child tables and computed columns are stored                                            | You change how child tables or computed columns are stored       |
| [Process tables](reference/process-tables.md)                                  | The tables that `Axis.Processes` owns                                                       | You change the tables that Axis.Processes owns                   |
| [Authoring request storage](reference/authoring-request-storage.md)            | The tables of authoring requests and their steps                                            | You change the tables of authoring requests and their steps      |
| [Publish request storage](reference/publish-request-storage.md)                | The tables of publish requests and the release history, and the audit records of publishing | You change the tables of publish requests or the release history |

## Working on Axis

| Doc                                 | What it holds                                                                                                      | Read it when                                   |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------- |
| [AGENTS.md](../AGENTS.md)           | The working rules and the commands, for people and agents                                                          | You start any change                           |
| [Development](development.md)       | Running Axis locally, the scripts, dependency updates and scans, test reports, database roles, realm users         | You run Axis or its tests locally              |
| [Testing](testing.md)               | The test levels, rules for tests, shared test services, the E2E stack and its journeys, the purchasing system stub | You write or change a test                     |
| [Delivery](delivery.md)             | How work flows through NexKit                                                                                      | You file an issue, plan or open a pull request |
| [Writing the docs](writing-docs.md) | What goes in which doc, how to write it, and how the docs stay correct                                             | You add or change a doc                        |
