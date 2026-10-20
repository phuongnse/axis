# Axis product brief

## What Axis is

Axis is a general-purpose low-code platform for building and running business
applications of any domain through configuration. An application is a set of typed resources:

- entities and fields
- data sources
- rules
- processes and human tasks
- triggers
- sites, pages and widgets
- policies
- text resources

Axis validates these resources, compiles them into an immutable **release** and
runs the release on shared engines. Publishing an application, or changing one,
never needs a platform source change or an application-specific build.

When a requirement cannot be expressed in configuration, it is written as an
**extension package**: versioned, tested C# code that adds typed operations
under explicit capability limits. The package is built and deployed on its
own, separate from the configuration (see [decisions](decisions.md) D7).

An author creates, configures, tests, publishes and runs an application
through Studio, without editing JSON or code. Files stay the source of truth
(D5). No domain gets platform code. See [D46](decisions.md#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed).

## Who uses it

| Role | Needs |
| --- | --- |
| Application author | Define data, queries, rules, processes, UI and policies; validate, test and publish them; see clear errors and where each resource is used. |
| Process participant (end user) | Use the application's site: fill in forms, find records, work through assigned tasks, follow status. |
| Application administrator | Manage users, roles and application settings; track running work; retry or resolve failures. |
| Extension developer | Write C# operations and custom UI components against stable contracts, and test them in isolation. |
| Platform operator | Install and upgrade Axis; manage tenants, databases, identity providers and secrets; watch health and diagnose incidents. |

## Product goals

1. **Complete applications through configuration.** Data, behaviour, UI and
   security are configured and enforced by the platform.
2. **Reliable execution.** Accepted work is never silently lost. Running
   processes survive restarts and keep the version they started on. Protected
   business effects, such as one purchase order per approved request, happen
   once.
3. **Secure by default.** Authorization is server-side and default-deny, at
   resource, record, field and action level. Configuration can never bypass it.
4. **Visible execution.** Authorized users can see where a process is, what it
   did, with which inputs and outputs, and why it failed or is waiting.
5. **Fast enough at realistic sizes.** Large applications (see
   [knowledge](domain/knowledge.md#typical-application-size)) stay responsive to
   author and to use. Every query and expression has a bounded cost.
6. **Author-friendly.** Resources are readable files with stable identities,
   so they can be reviewed, diffed and edited by people and AI agents. Studio edits
   the same files through the same contracts.

## Out of scope for now

These are expected later. They must not shape early milestones beyond keeping
the agreed extension points (see [decisions](decisions.md)).

- Formal security certification. FAPI 2.0 and ASVS verification come in the
  Hardening milestone, M16 (see the [roadmap](roadmap.md#milestones)).
- Provisioning many tenants self-service, and Axis-hosted SaaS operations.
- Custom UI widgets. They come after the visual designers (D8, D46).
- Migrating live business data between installations.

## First application: purchase requests

The first application is the first proving sample of the platform. It exercises
the whole platform. It lives as
configuration in `samples/apps/purchase-requests/`. It is not platform code.

### Data

- **Purchase request**: number (generated sequence), title, requester,
  department, supplier, currency, total amount (computed from line items),
  status, submitted at, decision comments.
- **Line item**: description, quantity, unit price, amount (computed).
- **Department** and **supplier**: reference data. A department has a
  manager, who decides on its requests.
- **Attachments**: supporting documents on a request.

### Flow

1. An employee creates a draft with line items and attachments and saves it.
2. Submitting validates the request and starts the approval process. A given
   submission starts the process only once.
3. The department manager gets a task. They approve, return for correction or
   reject it.
4. If the total is at or above the configured threshold, a finance reviewer
   gets a second task. The test fixture threshold is 10,000.
5. An approved request triggers a background call to a purchasing system. The
   call is idempotent per request, retries safely, and is reconciled when the
   outcome is unknown.
6. A returned request can be edited and submitted again, which starts a new
   submission.
7. Users see their requests, their tasks, status and history. Authorized users
   can inspect each process run.

### Scenarios the application must pass

- The happy path with and without finance review, plus return and reject.
- Denied access: users cannot see another department's requests or act on
  another user's task.
- A worker restarts while a request waits for approval.
- Duplicate or concurrent submissions and decisions.
- The purchasing system accepts the order but the response is lost.
- A new release is published while an older request is still waiting; that
  request finishes on its original version.

## Second application: leave requests

A second, smaller application is the second proving sample, of the same
shape. It shows that the platform is reused with no platform code change. It
lives as configuration in `samples/apps/leave-requests/`. It is not platform
code. See
[D41](decisions.md#d41-leave-requests-as-the-second-sample-application--agreed).
Later samples of different shapes follow from M11: a service desk, see
[Third application](#third-application-service-desk), and asset management. See [D46](decisions.md#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed).

### Data

- **Leave request**: number (sequence `LR-{yyyy}-{n:5}`), employee, department,
  leave type (`annual`, `sick` or `unpaid`), first day, last day, days
  (computed), reason, status and decision comment. The reason is required.
  It is the display field, so it labels the request in task lists.
- Days are calendar days, counting both ends:
  `daysBetween(firstDay, lastDay) + 1`. Weekends and holidays are not left out.
- **Department**: comes from the `departments` module, with its seed
  departments (see [D40](decisions.md#d40-reusable-modules--agreed)).

### Flow

1. An employee creates a draft and submits it. Submitting checks that the last
   day is not before the first day. It starts the approval process only once.
2. The department manager gets a task. They approve, return or reject the
   request.
3. A returned request can be edited and submitted again.
4. There is no finance step, connector or extension package.

### Security

- The roles are `employee` and `department-manager`. The sample uses the same
  development users as purchase requests: `anna` and `lan` are employees, and
  `binh` and `chau` are managers.
- An employee sees only their own requests. A manager sees the requests of
  their departments. Everything else is denied by default.

### Pages

My leave requests, the request form and My tasks, in a site at path `leave`.

### Scenarios the application must pass

- Approve, return then resubmit, and reject.
- Denied access: an employee cannot see another employee's request, and a
  user cannot act on a task that is not theirs.
- One Playwright journey: `anna` submits, `chau` approves, and `anna` sees the
  request approved.

The server activates this sample next to purchase requests under
`scripts/dev.sh`, Compose and E2E.

## Third application: service desk

A third application, of a different shape from the first
two, is the sample that M11 builds only through Studio. It runs as a sample
today. It lives as
configuration in `samples/apps/service-desk/`. It is not platform code. It is
thin on purpose: SLAs, escalation, loops that hold a task and notifications
wait for M14. See
[D56](decisions.md#d56-a-service-desk-as-the-third-sample-application--agreed).

### Data

- **Ticket**: number (sequence `SD-{yyyy}-{n:5}`), title, description,
  category, priority (`low`, `normal`, `high` or `urgent`), requester,
  assigned agent, status and resolution. The title is the display field.
- **Category**: name and default agent. A few categories are seeded.

### Flow

1. A requester submits a ticket.
2. The service desk lead triages it. They set the category and priority and
   assign an agent, or they reject the ticket.
3. The agent resolves the ticket with a resolution.
4. The requester confirms the resolution, which closes the ticket, or
   reopens it.
5. A reopened ticket ends the process with the status `reopened`. The
   requester submits it again. A loop that holds a task comes in M14.

### Security

- The roles are `requester`, `agent` and `service-desk-lead`.
- The development users are `anna` as a requester, `khoa` as an agent and
  `linh` as the service desk lead. `khoa` and `linh` are new, and `anna` gets
  the `requester` role. The roles of the existing users stay as they are.
- A requester reads and edits only their own tickets. An agent reads the
  tickets assigned to them. The lead reads every ticket.
- The requester cannot edit the resolution.
- Everything else is denied by default.

### Pages

My tickets, the ticket form, the task inbox and All tickets for the lead, in
a site at path `service-desk`. Texts are in `en` and `vi`.

### Scenarios the application must pass

- The happy path, a rejection, and a reopen then resubmit.
- Denied access for each role. A requester cannot see another requester's
  ticket, an agent cannot see a ticket assigned to someone else, and a user
  cannot act on a task that is not theirs.

### Built through Studio

_(planned for M11)_ The sample runs today under `scripts/dev.sh`, Compose and
E2E, and CI runs its scenarios. Building it only through Studio is planned.

- The committed folder `samples/apps/service-desk` records what the journey
  builds. CI activates it and runs its scenarios, as for the other samples.
- The main Playwright journey starts from a blank application in Studio. It
  builds the same application with Studio only.
- The journey may not use a file edit, the JSON view or an `axis-admin`
  command.
- At the end, the journey compares the folder it built with the committed
  sample. The comparison ignores resource ids and key order. Any other
  difference fails the journey.
- Then the journey runs the scenarios from Studio and publishes. Then `anna`,
  `linh` and `khoa` take one ticket through the process on the site.
- A second, smaller journey starts from the `request-approval` template.
