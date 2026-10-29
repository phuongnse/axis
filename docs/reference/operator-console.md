# Operator console

Detailed reference for the operator console: the web page where the platform operator sees what
failed in a tenant and runs the commands that fix it. Everything in this file is
_(planned for M23)_. The design follows
[D93](../decisions/d93-an-operator-web-console.md). Tenant provisioning stays in
[`Axis.Admin`](administration.md), as
[D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md) agrees.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Route and access

- **Route.** `/console` is a platform route on each tenant's hosts, outside every site, like
  `/studio`. It shows only the tenant of the host. There is no view across tenants.
- **Operator role.** An installation-level role, not a `role` resource. The server setting
  `Console:OperatorRole` names the roles claim value, such as `platform-operator`. The environment
  variable is `Console__OperatorRole`. It has no default, and with no value nobody may use the
  console. The Development settings set it to `platform-operator`, apart from the `operator` role of the purchase request sample. See
  [console settings](host-settings.md#console-settings).
- **Application policies play no part.** The role gives read access to the console views across
  every application of the tenant, and the commands below. It gives no grant on records, data
  sources, tasks or Studio.
- **Header link.** `GET /api/me` already answers `console: true` for a user who holds the role, while the
  console stays planned for M23. The app switcher then lists the operator console, linked to `/console`. A user without the role who opens the route gets a `403`
  from the API and sees a forbidden page. A signed-out user is sent to sign in.
- **API.** The console calls routes under `/api/console/`. Each needs a session and the operator
  role, and every write needs the CSRF header, as the other session routes do. They are checked
  on the server whatever the page shows.

## Views

Each view lists the newest items first, pages through them and filters by application.

- **Failed work.** Process instances that are `failed`, and connector and package calls whose
  outcome is `unknown`, across every process of every application. An entry opens the
  [instance detail](process-inspection.md#instance-detail), with the graph, the history, the
  tasks and the calls.
- **Dead letters.** Event deliveries that reached the event delivery limit that M14 adds,
  including [webhook deliveries](integration-resources.md#outbound-webhooks). Each shows the
  event, the trigger or webhook, the attempts and the last error.
- **Webhook deliveries.** Recent deliveries of each webhook, with their status code and attempts.
- **Schedules.** Each schedule trigger of each active release, with its last and next
  occurrence, see [schedules](process-events.md#schedules).
- **Publishing.** Publish requests and the [release history](studio.md) of each application,
  read-only. Publishing and rollback stay in Studio.
- **Tenant.** The tenant's status and each application's active release.

## Commands

- **Retry, cancel and resolve.** The [operator commands](process-inspection.md#operator-commands)
  with their rules, run from the instance detail. The operator role stands in for the `process`
  grants `retry`, `cancel` and `resolve`.
- **Redeliver and discard.** The commands that M14 adds for dead letters.
- **A reason each time.** Every command needs a reason. It writes the same history rows and audit
  records as from the process inspector, with the operator as actor.
- **Nothing else.** The console has no command that creates, suspends, backs up or restores a
  tenant, sets a binding, migrates a database or activates a release. Those stay in
  [`Axis.Admin`](administration.md).

## What the console never shows

- A field marked `sensitive`, by the [masking rules](process-inspection.md#values-and-masking)
  of process inspection.
- Record data beyond what process inspection holds. The console reads no record through the
  record API.
- A secret, a binding value or a webhook body.
