# Axis product brief

This page says what Axis is, who it serves, how it runs and which goals it holds itself to. It ends
with the four sample applications that prove the platform. Each sample runs under `scripts/dev.sh`,
Docker Compose and the E2E run, and CI runs its scenarios. Milestones and their status are in the
[roadmap](roadmap.md), and the vocabulary is in [concepts](domain/concepts.md).

## What Axis is

Axis is a general-purpose low-code platform for building and running business applications of any
domain through configuration. An application is a set of typed resources:

- entities and fields
- data sources
- rules
- processes and human tasks
- triggers
- sites, pages and widgets
- policies
- text resources

Axis validates these resources, compiles them into an immutable **release** and runs the release on
shared engines. Publishing an application, or changing one, never needs a platform source change or
an application-specific build, and no domain gets platform code, as
[D46](decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) explains.

When a requirement cannot be expressed in configuration, it is written as an **extension package**:
versioned, tested C# code that adds typed operations under explicit capability limits. The package is
built and deployed on its own, apart from the configuration, see
[D7](decisions/d7-custom-code-lives-in-extension-packages.md).

An author creates, configures, tests, publishes and runs an application through Studio, without
editing JSON or code. The files stay the source of truth, see
[D5](decisions/d5-configuration-is-authored-as-files-first.md).

Axis ships built-in modules: standard processes, such as approvals, that a business builds its
application from instead of starting from scratch. A business changes any part of a module in a layer
of its own application, while the module's contract, bound to its data, holds on every write. The
reasons are in
[D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md), and the modules in
[built-in modules](reference/built-in-modules.md).

- **A service desk and an asset register** _(planned for M30)_. They join the built-in modules, each
  with a built-in application that uses it.

## Who Axis serves first

Axis serves individuals and small and medium businesses first, and large organizations, banks and
government later. The roadmap's phases follow this order, and the reasons are in
[D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md).

The first customers are an individual, or a small or medium business, that runs its internal work on
Axis, such as approvals, requests and registers. They have no IT team and no operations team: a
business analyst, an office manager or the owner of the business sets the application up in Studio.
They need:

- **A built-in application to start from,** changed to fit in Studio, that keeps its upgrades.
- **Vietnamese and English.**
- **Axis as a service** _(planned for M31)_, with nothing to install.
- **Sign-up and a trial** _(planned for M33)_, so they try Axis before they pay.
- **Paying online** _(planned for M34)_.
- **Their data kept safe** as Vietnam's personal data rules ask, see
  [D109](decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).

What sets Axis apart for them is that they change a standard process anywhere, keep its upgrades,
and its core rules still hold on every write.

- **Changes described in words** _(planned for M35)_. An author describes a change in words, and an
  agent makes it under the same rules, see [D119](decisions/d119-ai-authoring-in-studio.md).
- **What large organizations, banks and government ask for** _(planned for M32)_. Conformance runs,
  signed images and alert rules for their own operations teams.

## Ways to run Axis

The same software runs in three ways, as
[D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) explains.

- **SaaS, shared** _(planned for M31)_. Many customers share a cell, a full installation in a region
  that Axis runs. Each tenant keeps its own database, realm, files, Studio applications and quotas.
  This is how small businesses and individuals use Axis.
- **SaaS, dedicated** _(planned for M31)_. One customer has a cell of its own that Axis runs.
- **Self-hosted.** The customer installs Axis on premises or in its own cloud, see
  [installation](reference/installation.md). This serves large organizations, banks and government.

## Who uses it

Each role below uses Axis for a different job.

| Role                           | Needs                                                                                                                                       |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Application author             | Define data, queries, rules, processes, UI and policies; validate, test and publish them; see clear errors and where each resource is used. |
| Process participant (end user) | Use the application's site: fill in forms, find records, work through assigned tasks, follow status.                                       |
| Application administrator      | Manage users, roles and application settings; track running work; retry or resolve failures.                                               |
| Extension developer            | Write C# operations and custom UI components against stable contracts, and test them in isolation.                                         |
| Platform operator              | Install and upgrade Axis; manage tenants, databases, identity providers and secrets; watch health and diagnose incidents.                   |

## Product goals

These are the qualities that Axis aims for in everything it builds.

1. **Complete applications through configuration.** Data, behaviour, UI and security are configured
   and enforced by the platform.
2. **Reliable execution.** Accepted work is never silently lost. Running processes survive restarts
   and keep the version they started on. Protected business effects, such as one purchase order per
   approved request, happen once.
3. **Secure by default.** Authorization is server-side and default-deny, at resource, record, field
   and action level. Configuration can never bypass it.
4. **Visible execution.** Authorized users can see where a process is, what it did, with which
   inputs and outputs, and why it failed or is waiting.
5. **Fast enough at realistic sizes.** Large applications, see
   [typical application size](domain/knowledge.md#typical-application-size), stay responsive to
   author and to use. Every query and expression has a bounded cost.
6. **Author-friendly.** Resources are readable files with stable identities, so people and AI agents
   can review, diff and edit them. Studio edits the same files through the same contracts.

## Out of scope

These are expected later. They must not shape the milestones before them beyond keeping the agreed
extension points.

- **Formal security certification.** Axis offers the FAPI 2.0 profile and tracks ASVS 5.0 Level 2.
  The conformance run and the ASVS sign-off, for large and regulated customers, are in a later phase
  of the [roadmap](roadmap.md#milestones).
- **Custom UI widgets.** They come in phase 2, after the visual designers, see
  [D8](decisions/d8-custom-ui-widgets-are-packaged-react-components.md).
- **Migrating live business data between installations.**

## First application: purchase requests

Purchase requests are the first proving sample, and they exercise the whole platform. The
application lives as configuration in `samples/apps/purchase-requests/` and is not platform code.

### Data

- **Purchase request**: number (generated sequence), title, requester, department, supplier,
  currency, total amount (computed from line items), status, submitted at, decision comments.
- **Line item**: description, quantity, unit price, amount (computed).
- **Department** and **supplier**: reference data. A department has a manager, who decides on its
  requests. Departments come from the built-in `organization` module, with the same employees and
  departments as the leave request sample.
- **Attachments**: supporting documents on a request.

### Flow

1. An employee creates a draft with line items and attachments and saves it.
2. Submitting validates the request and starts the approval process. A given submission starts the
   process only once.
3. The department manager gets a task. They approve, return for correction or reject it.
4. If the total is at or above the configured threshold, a finance reviewer gets a second task. The
   test fixture threshold is 10,000.
5. An approved request triggers a background call to a purchasing system. The call is idempotent per
   request, retries safely, and is reconciled when the outcome is unknown.
6. A returned request can be edited and submitted again, which starts a new submission.
7. Users see their requests, their tasks, status and history. Authorized users can inspect each
   process run.

### Sign-in

The Purchasing site allows two sign-ins: accounts, and the organization's own identity provider,
`company`. A person in that provider's `employees` group signs in through "Continue with Example
Company" and gets the `employee` role. In development the `company` realm plays the organization's
provider, see [the development realm](development.md#development-realm-and-users).

### Scenarios the application must pass

- The happy path with and without finance review, plus return and reject.
- Denied access: users cannot see another department's requests or act on another user's task.
- Sign-in per site: a Playwright journey signs a person of the organization's provider in to
  Purchasing, signs an account in to the Service desk with a password and a code, and shows that the
  Service desk refuses the organization's provider.
- A worker restarts while a request waits for approval.
- Duplicate or concurrent submissions and decisions.
- The purchasing system accepts the order but the response is lost.
- A new release is published while an older request is still waiting, and that request finishes on
  its original version.

### Extensibility

Purchase requests prove the extensibility milestones, one step each, as
[D94](decisions/d94-purchase-requests-prove-the-extensibility-milestone.md) explains.

1. **Modules.** The application declares the `department-budgets` module, which depends on the
   `organization` module, and maps its approver slot to the manager role.
2. **A packaged widget** _(planned for M22)_. The author drags a packaged "budget usage" widget onto a
   purchase request page in Studio.
3. **A connector.** An approved request reaches the purchasing system through a connector that signs
   in with client credentials and sends the line items as a list.
4. **A package runner** _(planned for M21)_. The payment reference package runs out of process.
5. **A webhook.** A signed webhook announces the approval, and a receiver that fails leaves a dead
   letter. The platform operator redelivers the dead letter from the console and sees it delivered.

## Second application: leave requests

Leave requests are the second proving sample, smaller than the first and of the same shape. They
show that the platform is reused with no platform code change, as
[D41](decisions/d41-leave-requests-as-the-second-sample-application.md) explains. The application
lives as configuration in `samples/apps/leave-requests/` and is not platform code.

### Data

- **Leave request**: number (sequence `LR-{yyyy}-{n:5}`), title, employee, department, leave type
  (`annual`, `sick` or `unpaid`), first day, last day, days (computed), reason, status and decision
  comment. The title is required and is the display field, which labels the request in task lists.
  The number cannot be the display field, because a sequence number cannot be required, and nor can
  the reason, because it is `sensitive` and so stored encrypted.
- Days are calendar days, counting both ends: `daysBetween(firstDay, lastDay) + 1`. Weekends and
  holidays are not left out.
- **Department**: comes from the built-in `organization` module, see
  [built-in modules](reference/built-in-modules.md). The sample seeds an employee for each of its
  users and the departments, each with a head.

### Flow

1. An employee creates a draft and submits it. Submitting checks that the last day is not before the
   first day, and starts the approval process only once.
2. The department manager gets a task. They approve, return or reject the request.
3. A returned request can be edited and submitted again.
4. There is no finance step, connector or extension package.

### Security

- The roles are `employee` and `department-manager`.
- An employee sees only their own requests. A manager sees the requests of their departments.
  Everything else is denied by default.

### Pages

My leave requests, the request form and My tasks, in a site at path `leave`.

### Scenarios the application must pass

- Approve, return then resubmit, and reject.
- Denied access: an employee cannot see another employee's request, and a user cannot act on a task
  that is not theirs.
- One Playwright journey: an employee submits, a department manager approves, and the employee sees
  the request approved.

## Third application: service desk

The service desk is a sample of a different shape from the first two, and a Playwright journey builds
it only through Studio. It lives as configuration in `samples/apps/service-desk/` and is not
platform code. It is thin on purpose: it has no SLA calendars or business hours. The reasons are in
[D56](decisions/d56-a-service-desk-as-the-third-sample-application.md).

### Data

- **Ticket**: number (sequence `SD-{yyyy}-{n:5}`), title, description, category, priority (`low`,
  `normal`, `high` or `urgent`), requester, assigned agent, status and resolution. The title is the
  display field.
- **Category**: name and default agent. A few categories are seeded.

### Flow

1. A requester submits a ticket.
2. The service desk lead triages it. They set the category and priority and assign an agent, or they
   reject the ticket.
3. The agent resolves the ticket with a resolution.
4. The requester confirms the resolution, which closes the ticket, or reopens it.
5. A reopened ticket goes back to the status `assigned` and loops to the agent's resolve task in the
   same instance, see [loops](reference/process-flow.md).

The service desk runs in the `Asia/Ho_Chi_Minh` time zone, and these automations run beside the flow:

- **Overdue reminder.** Every weekday at 08:00 local time a scheduled job notifies the lead in-app
  that tickets are overdue, and the notification opens All tickets.
- **Deleted draft.** When a requester deletes a draft ticket, a delete trigger notifies the lead
  in-app with the ticket's number and title.
- **Escalation.** The agent's resolve task is due three days after it starts. When it is due, the
  lead is notified in-app and the assigned agent in-app and by email, with the ticket's number. One
  day later the task is reassigned to the `service-desk-lead` role, see
  [task deadlines and claims](reference/task-deadlines.md).
- **Resolution notice.** When the agent resolves the ticket, the requester is notified in-app and by
  email with the ticket's number and the resolution, and the notification opens the ticket detail
  page, see [notifications](reference/notifications.md).

### Security

- The roles are `requester`, `agent` and `service-desk-lead`.
- A requester reads and edits only their own tickets. An agent reads the tickets assigned to them.
  The lead reads every ticket.
- The lead may reassign the tasks of a ticket's handling. The lead and the agent claim and release
  through their roles.
- The requester cannot edit the resolution.
- A requester may delete their own ticket while its status is `draft`. An agent and the lead may not
  delete.
- The Service desk site allows only accounts. Its sign-in page has no "Continue with" button, and the
  callback refuses a person who arrives through the organization's provider.
- Everything else is denied by default.

### Pages

The site at path `service-desk` has My tickets, the ticket form, the task inbox, the ticket detail
page, All tickets and the lead's dashboard. Its texts are in `en` and `vi`.

- The dashboard has KPIs (key figures) for open and urgent tickets and a chart of open tickets by
  category, above All tickets, in columns.
- The ticket detail page has one detail widget that shows the ticket. My tickets and All tickets open
  it through a navigate action with a `ticket` parameter.
- The ticket form has two steps. Step 1 is the title and description. Step 2 is the category, the
  priority and the ticket's other fields. The resolution shows, and is required, only when the status
  is `resolved`.

### Scenarios the application must pass

- The happy path, a rejection, and a reopen that returns to the agent.
- Denied access for each role. A requester cannot see another requester's ticket, an agent cannot see
  a ticket assigned to someone else, and a user cannot act on a task that is not theirs.
- A requester deletes a draft ticket. An agent's delete is refused.
- The next weekday morning reminds the lead of overdue tickets, and a deleted draft ticket is
  reported to the lead.
- A late ticket notifies the lead in-app and the agent in-app and by email, and is then reassigned to
  the lead's role. A resolved ticket notifies the requester in-app and by email.

### Built through Studio

The main Playwright journey proves that Studio alone builds a whole application. Its steps are in
[testing](architecture.md#testing).

- It starts from a blank application in Studio and builds the whole application, the two-step form,
  the dashboard, the ticket detail page, the time zone, the automations and their texts included.
- It may not use a file edit, the JSON view or an `axis-admin` command.
- At the end it compares the folder it built with the committed sample, `samples/apps/service-desk`.
  The comparison ignores resource ids and key order, and any other difference fails the journey.
- It then runs the scenarios from Studio and publishes. The requester, the service desk lead and the
  agent take one ticket through the process on the site, and the requester opens the ticket from the
  bell after the agent resolves it.

Another Playwright journey uses the sample at phone width.

## Fourth application: asset management

Asset management is a register of the company's assets with a dashboard, of a shape none of the
first three has. It proves the data modeling: files, money, labelled enum values, many-to-many,
references from child rows, import and export. It lives as configuration in
`samples/apps/asset-management/` and is not platform code. The reasons are in
[D81](decisions/d81-asset-management-as-the-fourth-sample-application.md).

### Data

- **Asset**: number (sequence `AS-{n:6}`), name, category, status (`in-stock`, `assigned`,
  `in-repair` or `retired`, each with a label), location, holder, purchase cost (money, with a
  currency field), purchase date and warranty end date. The name is the display field and is
  searchable.
- **Attachment**: child rows of an asset, each with a file and a note.
- **Maintenance entry**: child rows of an asset, each with a date, a vendor (a reference from the
  child row), a cost in money and a note.
- **Software**: the software installed on an asset, a `references` field to software records.
- **Category**, **location**, **vendor** and **software**: reference data. Categories and locations
  are seeded. A new asset's status defaults to `in-stock`, and a cost cannot be negative.

### Flow

1. An asset manager registers assets, one by one or by importing a CSV file, and attaches invoices
   and photos.
2. The asset manager assigns an asset to a holder, and records maintenance.
3. An auditor exports the asset register.
4. A scheduled job reminds the asset manager, in-app on weekdays, of warranties that end within 30
   days, see [notifications](reference/notifications.md).

### Security

- The roles are `asset-manager`, `employee` and `auditor`.
- An asset manager reads, writes, imports and exports everything.
- An employee reads only the assets they hold, and cannot export.
- An auditor reads and exports everything and writes nothing.
- Everything else is denied by default.

### Pages

The asset register, the asset form, the asset detail page and the dashboard, in a site at path
`asset-management`. The path `assets` is reserved, because the SPA serves its bundle there. Texts are
in `en` and `vi`.

- The dashboard has asset value by location in each currency, assets by status, and a KPI of
  warranties that end within 30 days.
- The asset register searches by name and exports to CSV or Excel, and has an Import button for the
  asset manager.

### Scenarios the application must pass

- An asset is registered.
- An employee and an auditor cannot write.
- A negative cost is refused.
- An asset with a maintenance entry with a vendor, and software, is recorded.
- A user without a role is denied.
- **Import** _(planned for M30)_. An import of valid assets, and an import whose dry run reports a bad
  row and writes nothing.
- **Attachment** _(planned for M30)_. An asset with an attachment.
- **Denied import, export and download** _(planned for M30)_. An employee is denied another holder's
  asset, its attachment's download and an export, and an auditor is denied an import.

### Built through Studio

As for the service desk, the main Playwright journey builds the application with Studio only,
compares it with the committed folder, runs its scenarios from Studio and publishes it. It uses the
JSON view for one scenario only, because the scenario editor cannot add a child-collection value. On
the site, the asset manager registers an asset with an attachment and imports assets, and the
auditor exports the register.
