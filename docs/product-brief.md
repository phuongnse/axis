# Axis product brief

## What Axis is

Axis is a platform for building and running enterprise business applications
through configuration. An application is a set of typed resources:

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
   so they can be reviewed, diffed and edited by people and AI agents. A visual
   Studio comes later and uses the same contracts.

## Out of scope for now

These are expected later. They must not shape early milestones beyond keeping
the agreed extension points (see [decisions](decisions.md)).

- FAPI 2.0 conformance and formal security certification.
- Provisioning many tenants self-service, and Axis-hosted SaaS operations.
- Custom UI widgets.
- Visual process and page designers.
- Migrating live business data between installations.

## First application: purchase requests

The first application exercises the whole platform. It lives as
configuration in `samples/apps/purchase-requests/`. It is not platform code.

### Data

- **Purchase request**: number (generated sequence), title, requester,
  department, supplier, currency, total amount (computed from line items),
  status, submitted at, decision comments.
- **Line item**: description, quantity, unit price, amount (computed).
- **Department** and **supplier**: reference data.
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

A second, smaller application is added after purchase requests to show that
the platform is reused without platform code changes. It will be chosen when
that milestone starts.
