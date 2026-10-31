# Axis roadmap

## How work is planned

- **Milestones are vertical slices.** Each one ends with a running feature
  covered by automated tests, including at least one end-to-end journey.
- **Rolling wave.** A milestone is broken into issues only when it is meant to
  run and passes the readiness check, using what was learned. The issues of
  several milestones run at once, as
  [D68](decisions/d68-issues-of-several-milestones-run-at-once.md) describes.
  How is in [the life of a milestone](delivery.md#the-life-of-a-milestone).
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

Axis serves individuals and small and medium businesses first, and large organizations, banks and
government later, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) agrees. The milestones after M18 are in three
phases, and each phase lists its milestones in the order they run. The
[direction](#direction) says why.

### Done

The closed milestones are on [GitHub](https://github.com/phuongnse/axis/milestones?state=closed).

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
| **M14** | Workflow, automation and notifications | Done | Processes get timer, wait for event and sub-process steps, loops that hold a task, task escalation and deadlines, claim and reassign, and a create record operation. There are scheduled jobs without a subject, delete triggers, and a limit plus dead letters for event delivery. In-app and email notifications exist. Processes are edited in a visual designer. Carried over from M13 and built: a page and a form open on the Design tab, a section moves to another step by a drop on the step switch, and the form designer removes a section or a step. |
| **M15** | Data modeling and data management | Done | New field types: file (with storage bindings), money and enum labels. Indexes, defaults and ranges. References from child rows, and many-to-many. CSV and spreadsheet import and export under policies. Full-text search, more aggregates and query timeouts. |
| **M16** | Hardening | Done | The FAPI 2.0 profile, ASVS 5.0 Level 2 tracking with a server-side session, rate limits and security headers, service accounts for the record API, data source rows, process start and inspection, tenant setting commands and key rotation with platform audit records, and the shell and platform home page of D98. Carried over from M10 and built: restoring a preview tenant's archive is refused. |
| **M17** | Axis's identity provider | Done | Every installation runs Keycloak with the Axis theme as Axis's identity provider, with a realm for each tenant. An application declares its identity providers, accounts with a required second step, OIDC or SAML, with their claim and role mapping, and each site lists the ones it allows. Activation keeps the realm in step with the release, and a site may have a host of its own. The tenant's platform sign-in serves the home page, Studio and the console. An administrator invites accounts and gives roles on a users page. The end-to-end journey signs one person in to a site through a brokered identity provider and another in to a second site with an account and a code from an authenticator app, and refuses a provider the site does not allow. Carried over to M28: the users page's disable, enable and reset authenticator buttons, and keeping the realm's redirect addresses in step with `tenant host add`, `remove` and `move`. See [D99](decisions/d99-axis-ships-its-own-identity-provider.md) and [D100](decisions/d100-identity-providers-per-application-and-sign-in-per-site.md). |
| **M18** | Verification and threat model | Done | Carried over from M16, with no new feature, as [D101](decisions/d101-the-extensibility-milestone-is-split-into-six.md) agrees, and built: performance objectives and the load test, `scripts/load.sh`, the email channel of a task escalation's `notify`, Mailpit in Docker Compose and `scripts/dev.sh`, dead letters for waking a waiting instance, the process designer's live region for moves, the threat model and its check, added by [D106](decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md), authentication by default with a test that calls every route without a session, added by [D111](decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md), and fuzz tests for the expression language, the record API's request bodies and import. The dependency scan in CI is no longer in M18: a hand-made change runs it now, as [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) agrees. Carried over to M28: the weekly CI run of the load test and the final query limits. Carried over to M32, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) moves them: the FAPI 2.0 conformance run, the final ASVS sign-off and the first SAMM rating. Carried over to M30: the asset management scenarios for import and attachments, with their denied cases. |
| **M19** | Connector OAuth2 and outbound webhooks | Done | A connector signs in with OAuth2 client credentials and sends inputs in the path, query and headers, nested inputs and nested outputs. An application tells another system about an event through a signed outbound webhook, delivered at least once, with dead letters. An inbound endpoint call raises an event, as [D115](decisions/d115-an-inbound-endpoint-call-raises-an-event.md) agrees. The journey: an approved purchase request reaches the purchasing system with its line items as a list, and a webhook whose receiver fails leaves a dead letter. See [D91](decisions/d91-connector-oauth2-and-structured-payloads.md) and [D92](decisions/d92-outbound-webhooks.md). |
| **M20** | Modules that ship working features | Done | A module holds processes, pages, policies and triggers, names role slots that each application maps to its own roles, names connector slots that each application maps to its own connectors, and depends on other modules. An application's endpoint starts a module process. The journey: purchase requests declare the `department-budgets` module, which depends on the `departments` module, and map its approver slot to the manager role. See [D89](decisions/d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md) and [D103](decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md). Carried over to M24: a module policy's grants on the processes of its own module. |

### Phase 1: the first small businesses

Done when a small business signs up for Axis SaaS, pays for a plan and runs its approvals, changed
to fit in Studio by hand or by a request in words, while the module's contract holds: the journeys
of M26, M34 and M35 pass.

| | Milestone | Status | Done when |
| --- | --- | --- | --- |
| **M27** | Data protection | In progress | A field marked `sensitive` is stored encrypted under a data key of its tenant, with an exact lookup when the field declares one, and compilation refuses a filter, sort, aggregate, index or SQL expression on it. A command rotates a tenant's data key. Tenant backups are encrypted with a backup key the operator keeps outside the installation. New history rows keep the user id and no display name, and session audit records keep a shortened client address. The journey: the leave request sample marks the reason `sensitive`, an employee submits a sick leave, the manager reads the reason, a query over the tenant database as its runtime role sees no clear text, and a restore without the backup key is refused. It follows M18 and runs beside M19 to M21, and M26 waits for it. See [D109](decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md). |
| **M24** | Module contracts | Planned | A module declares a contract on its entities: a lifecycle of states and transitions with guards, core validations, the effects of a transition and a grant ceiling. The engine checks it on every write, whatever makes the write. A module holds test scenarios whose users hold role slots ([D103](decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md)). Contract scenarios run on every publish of an application that uses the module, and a failing one stops the publish. An application relaxes a named point of a module's contract with a reason, and its release is marked off-standard. The journey: a process change that moves a module record to a state without meeting its guard is refused, and a relax entry lets it through with the off-standard mark. Carried over from M20: a module policy grants on the processes of its own module. It follows M20. See [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md). |
| **M25** | Customization layers | Planned | An application changes a module through a layer under `customizations/<module id>/` that names elements by name and holds only its changes. Compilation applies the layer and checks the contract. Studio shows each module resource with the layer applied, writes the layer and marks what it changed. The journey: an author adds a step to a module's process in Studio and publishes, the layer file holds only that step, and a change that breaks the module's contract is refused. Module upgrades and eject are in M29, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) agrees. It follows M24. See [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md). |
| **M26** | The first built-in modules | Planned | The built-in modules `organization` and `approvals` under `modules/`, and a built-in application that uses them under `applications/`. The `departments` sample module and the `request-approval` template become them. The journey: two businesses customize `approvals` differently, one adds a director's approval above a threshold and the other adds a request type of its own, and the contract holds for both: nobody decides their own request, and each step is decided once. `service-desk` and `assets` are in M30, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) agrees. It follows M25 and waits for M27. See [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md). |
| **M23** | Operator console | Planned | The platform operator sees a tenant's failed work, dead letters, webhook deliveries, schedules and publishing at `/console`, and retries, cancels, resolves, redelivers and discards with a reason. The journey: the operator redelivers the dead letter of the failed webhook and sees it delivered. It needed M19, which is done. See [D93](decisions/d93-an-operator-web-console.md). |
| **M28** | Security operations | Planned | Axis's images are scanned with Trivy in CI. The hourly driver turns each high or critical Dependabot alert into a NexKit issue. An administrator disables, enables and resets an authenticator from the users page, and `tenant host add`, `remove` and `move` keep the realm's redirect addresses in step. CI runs the load test once a week, and the query limits get their final values. The journey: an administrator disables an account on the users page and its next sign-in is refused, and after `tenant host add` a person signs in on the new host. Carried over from M18: the weekly CI run of the load test and the final query limits. Carried over from M17: the users page's buttons and the realm's redirect addresses. Image signing, the alert rules and the hardening checklist are in M32, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) agrees. It can run beside any other milestone. See [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md). |
| **M31** | Axis SaaS | Planned | Axis runs as a SaaS in cells: each cell is a full installation in a region, the first region is `vn`, and up to 1,000 tenants share a cell. Each tenant's Studio application folders are kept apart. Each tenant has quotas for storage, users, applications, emails a day and work at once, its usage is measured, and the worker shares its time fairly between tenants. A new tenant gets a subdomain of the region's domain, and may add a host of its own. Every tenant is backed up each day, encrypted, kept 30 days, with a restore tried every week. The platform operator creates a tenant from a built-in application, exports a tenant's data and deletes a tenant, and sees every tenant of the cell with its status, usage and failed work. A load test sets how many tenants a cell holds. The journey: the operator creates two tenants from the built-in approvals application, one reaches its storage quota and is refused while the other keeps working, and the first is exported and deleted. It runs beside M24 to M26. See [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). |
| **M33** | Sign-up and trials | Planned | A person signs up on Axis's public page, chooses a region and a built-in application, and verifies their email. Axis picks a cell of the region with room, creates the tenant, its realm and its subdomain, activates the application and invites the person as the first administrator. A new tenant is a 30-day trial, with emails before its end, a suspension at the end and a deletion 30 days later. There is no free plan that never ends. Sign-up has rate limits and a bot check. A directory of regions and cells routes each sign-up and each host to its cell. The journey: a person signs up, uses a working approvals site, and the trial ends with a suspension after the reminder emails. It follows M31 and M26. See [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). |
| **M34** | Plans and billing | Planned | Each tenant has a plan that sets its quotas, with a price for the whole tenant, not per user, and prices set from the measured cost of a cell. A tenant administrator chooses a plan and pays: in Vietnam by bank transfer through VietQR or a payment gateway, abroad through a merchant of record. Each payment gets an electronic invoice. A trial becomes paid without losing anything, and a failed or late payment sends reminders, then a grace period, then a suspension. The journey: a trial tenant chooses a plan, pays, gets its invoice and its new quotas, and a later failed payment ends in a suspension after the grace period. It follows M33. See [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). |
| **M35** | AI changes an application | Planned | Axis serves an MCP server over the authoring API, the only way any agent acts on Axis: its tools read resources, change drafts, write customization layers, validate and run scenarios, a client signs in through OAuth with its user's grants, and publishing is not a tool. The agent host is one of its clients. An author describes a change in words in Studio, the agent answers with a plan that the author approves, prefers declaring a built-in module and writing its layer to new resources, and writes scenarios for what it adds. The compiler, the scenarios and the contract scenarios run as its checks, a second agent session reviews the draft, and failures start a bounded fix round. Studio shows the draft's diff. The author accepts or drops it, or the agent accepts it when the application allows it. An accept is all or nothing, runs the checks again on the latest working folder, and waits in a queue of one accept at a time per application. Each accept and each Studio save is a revision that can be undone. The model is a binding of the kind `anthropic` or `openai-compatible` behind an adapter, and an authoring eval decides which models are supported. No record of the tenant reaches the model, each request is audited, the threat model covers the MCP server, and a SaaS plan includes a number of requests. The journey: an author asks for a director's approval above a threshold on an approvals application, approves the plan, sees the layer change and its new scenario pass, and publishes; a request that would let a requester decide their own request is refused by the contract; a developer makes the same kind of change from an outside MCP client; and an accept whose resource another author changed meanwhile writes nothing and answers a conflict. It follows M25 and M26 and runs beside M31 to M34. See [D119](decisions/d119-ai-authoring-in-studio.md). |

### Phase 2: growth

More built-in modules, module upgrades, and extension work for developers and partners. It starts
when every milestone of phase 1 is broken down.

| | Milestone | Status | Done when |
| --- | --- | --- | --- |
| **M29** | Module upgrades | Planned | A module ships migrations, and a module build fails when an element is gone without one. An upgrade merges an application's layer three ways, property by property, and Studio shows the conflicts. An application ejects a module. The journey: a module is upgraded and an author's added step stays, and a property both changed is resolved in Studio. It follows M25, and must be done before Axis ships a new version of a built-in module to a business that has a layer over it. See [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) and [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md). |
| **M30** | More built-in modules | Planned | The built-in modules `service-desk` and `assets`, which the service desk and the asset management samples become, with built-in applications that use them. Carried over from M18: the asset management scenarios for import and attachments, with their denied cases. The journey: a business starts from the built-in service desk, adds a category of its own in Studio, and a request in it reaches the right team. It follows M26. See [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md), [D81](decisions/d81-asset-management-as-the-fourth-sample-application.md) and [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md). |
| **M21** | Package runners | Planned | Outside Development, package operations run in the package runners of `Axis.PackageHost`, with an empty environment, a read-only file system, CPU and memory limits, one network route through an egress proxy, and a kill at the operation's timeout. The journey: the payment reference package of purchase requests runs in the package host. See [D90](decisions/d90-extension-packages-run-out-of-process.md). |
| **M22** | Custom widgets and the published SDK | Planned | An extension package holds custom widgets that the SPA loads through an import map, and the page designer offers them in its palette with a property panel. `Axis.Extensions` and `@axis/widgets-sdk` are published, with a package template and a test harness. The journey: the author drags the "budget usage" widget onto a purchase request page in Studio. It needed M20, which is done. See [D88](decisions/d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md). |
| **M36** | AI builds applications and modules | Planned | An agent builds a whole application, or a reusable module, from a description, with its entities, forms, pages, processes, roles, policies and scenarios, through the MCP server and the loop of M35. The authoring eval gains requests for whole applications and modules. The journey: an author describes a small register, accepts the draft and publishes it. It follows M35. See [D119](decisions/d119-ai-authoring-in-studio.md). |

### Phase 3: large organizations and government

What only a large or regulated customer's procurement asks for. It starts when the owner names a
first such customer.

| | Milestone | Status | Done when |
| --- | --- | --- | --- |
| **M32** | Assurance for large and regulated customers | Planned | A release workflow publishes Axis's images signed with cosign, each with a CycloneDX SBOM. The Helm chart ships alert rules for security events, the installation page holds a hardening checklist of every control delegated to the operator, and the server and the worker warn at start about a missing one. The FAPI 2.0 conformance run, the final ASVS sign-off and the first SAMM rating are done. The journey: an operator checks an image's signature, installs with the alert rules on, sees an alert fire on a burst of refused sign-ins, and sees the startup warning for a missing control. Carried over from M18 and M28, as [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) moves them. It starts when the owner names a first large or regulated customer. See [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md), [D110](decisions/d110-security-alerts-and-a-hardening-checklist-for-the-operator.md), [D83](decisions/d83-the-fapi-20-profile-per-tenant.md) and [D84](decisions/d84-asvs-level-2-and-a-server-side-session.md). |

Milestones after M4 may be reordered when business needs require it. M0–M4
stay in this order.

## Direction

[D46](decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) records the
direction after M9, and [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) the customers it serves first.

- **A general-purpose low-code platform.** An author creates, configures, tests,
  publishes and runs an application through Studio, without editing JSON or
  code.
- **Small businesses first.** Individuals and small and medium businesses have no IT team and
  do not install software. They start from a built-in application, change it in Studio and use
  Axis SaaS _(planned for M31, M33 and M34)_, see [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). Large organizations,
  banks and government come in phase 3.
- **What sets Axis apart.** Built-in modules _(planned for M26)_ that a business changes anywhere,
  that keep their upgrades, and whose contract holds on every write, see
  [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md). An author
  can also describe a change in words, and an agent makes it under the same contract, wherever
  Axis runs _(planned for M35)_, see [D119](decisions/d119-ai-authoring-in-studio.md).
- **No domain gets platform code.** Samples prove the platform. Built-in
  modules serve real businesses as configuration, not platform code.
- **Order.** M10 to M16 and M18 to M26 follow the vertical slice: create an
  application, then its data and UI, logic and workflow, permissions, try it
  out, validate and test, publish and operate. Each ends with an end-to-end
  journey. M17 gives every tenant Axis's own identity provider before
  extensibility, so every sign-in looks and works the same, see
  [D99](decisions/d99-axis-ships-its-own-identity-provider.md). From M19 the
  phases order the work, see [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md).
- **Parallel work.** M19 and M20 are done. M27 runs and finishes first, and M24 is next.
  M24, M25 and M26 follow one after the other, M24 first, see [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md).
  M26 waits for M27, see
  [D109](decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).
  M23 needed M19, which is done, see
  [D101](decisions/d101-the-extensibility-milestone-is-split-into-six.md).
  M28 can run beside any of them, and M31 runs beside M24 to M26. M33
  follows M31 and M26, and M34 follows M33, see [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md).
  M35 follows M25 and M26 and runs beside M31 to M34, see [D119](decisions/d119-ai-authoring-in-studio.md).
  In phase 2, M29 follows M25, M30 follows M26, M36 follows M35, and M22
  needed M20, which is done.
- **Proving it is general.** From M11 on, each milestone extends a sample of a
  new shape: a service desk, then asset management. Neither needs platform
  code.
  M19 to M23 extend the purchase request sample instead, as
  [D94](decisions/d94-purchase-requests-prove-the-extensibility-milestone.md)
  agrees. From M26 the samples become built-in modules that real businesses
  customize, and phase 1 proves its work on them wherever it can.
- **Planning.** A milestone is broken into issues when it passes the readiness
  check of [D68](decisions/d68-issues-of-several-milestones-run-at-once.md).
  Only the milestones of the current phase are broken down, unless the owner
  asks for another, see [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md).
  The decisions and doc sections a milestone lacks are written first, in the owner's
  local session, as [the life of a milestone](delivery.md#the-life-of-a-milestone)
  describes.

## M0 — Foundation

This milestone is done directly, not through NexKit, because NexKit needs
working checks before it can verify anything.

1. Create the .NET 10 solution following the [repository layout](repository-layout.md#repository-layout).
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
