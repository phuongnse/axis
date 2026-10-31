# Operator console

Detailed reference for the operator console: the web page where the platform operator sees what
failed in a tenant and runs the commands that fix it. The route, the access rules, the Tenant view, the
list of the Failed work view, the instance detail it opens, the Webhook deliveries view, the Publishing view, the
Dead letters view and the redeliver and discard commands are built. Everything else in this file is _(planned for M23)_. The design follows
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
- **Header link.** `GET /api/me` answers `console: true` for a user who holds the role. The app
  switcher then lists the operator console, linked to `/console`. A user without the role who opens
  the route gets a `403` from the API and sees a forbidden page. A signed-out user gets the shell's
  sign-in prompt after the `401`.
- **API.** The console calls routes under `/api/console/`. Each needs a session (`401`) and the
  operator role (`403`, also for everyone when `Console:OperatorRole` has no value), and every
  write needs the CSRF header, as the other session routes do. They are checked on the server
  whatever the page shows.
  - **`GET /api/console/tenant`.** Answers the tenant of the host: `id`, `status` and
    `applications`. Each application has its `id`, `name`, `releaseId` (the active release),
    `releaseNumber` and `releaseLabel` (the number and label of that release in the application's
    release history, as Studio shows them) and `activatedAt`. The applications are the ones with an
    active release, in name order. The `status` is `active`, because a host serves only active
    tenants: the hosts of a suspended tenant answer `503`.
  - **`GET /api/console/failed-work`.** Lists the failed work of the tenant, newest first, in the
    paging shape of the record API (`items`, `page`, `pageSize`, `totalCount`). The parameters are
    `application` (an application id), `page` and `pageSize`, all optional. An invalid one is a
    `400` keyed by its name, and an `application` that is not a hyphenated UUID is keyed
    `application`. Each item has `kind` (`instance`, `connector` or `package`), `applicationId`,
    `application`, `process`, `instanceId`, `step`, `callId`, `connector`, `package`, `operation`,
    `failedAt` and `error`. The keys that do not apply are `null`: an instance has no `callId`,
    `connector`, `package` or `operation`. The `application` and `process` names come from the
    active release, and from the release the instance is pinned to when the application is not
    active. An item never holds a call input, a history input or output, or a record value. The
    time of a failed instance is its `ended_at`, and its error is the newest history error of the
    instance. The time of a call with an unknown outcome is the finish of its newest attempt, or its
    `created_at` when it has no attempt row, and its error is the call's `last_error`.
  - **`GET /api/console/webhook-deliveries`.** Answers one page of the webhook deliveries of the
    tenant, newest first. The query parameters are `page` and `pageSize`, read as the other list
    routes read them, and the optional `application` and `webhook` ids, each a hyphenated UUID. A
    malformed parameter answers one `400` validation problem. An item has its `id`,
    `applicationId`, `application`, `webhookId`, `webhook`, `eventId` and `event`, the names of
    the application, the webhook and the event as the release the delivery was staged with holds
    them (`null` when that release no longer holds the webhook), then the `state`, the `attempts`,
    the last `statusCode`, `createdAt` and `deliveredAt`. The `state` is `delivered`, `pending`,
    `deadLettered` (an open dead letter of the delivery) or `discarded` (a discarded one). A
    redelivered dead letter is queued again and shows as `pending`. The answer also lists
    `webhooks`, the webhooks of every active release with their application, for the view's
    filter, and `totalCount`. It never holds a body, a URL, a secret or a binding value.
  - **`GET /api/console/instances/{id}`.** Answers one instance of the tenant, whatever its
    application or process, in the shape of the
    [process inspection detail](process-inspection.md#instance-detail). An id that is not a
    hyphenated UUID and an id that names no instance are one `404`. The operator role stands in
    for the `inspect` grant and for the subject record's read check: the instance of an application
    that is no longer active opens too, and its subject record need not exist. The operator reads
    fields like a `read` grant with no `fields` list, so a call's `input` is hidden only by the
    [masking rules](process-inspection.md#values-and-masking), when its mapping uses a `sensitive`
    field or a field the active release no longer has. `commands` is empty and no call is
    `resolvable` until the console's command routes are built. When the application has no active
    release, the release the instance is pinned to stands in for the active one.
  - **`GET /api/console/applications/{id}/publishing`.** Answers one application, named by its id,
    with its release history and its publish requests, read-only. The group's checks apply (`401`,
    `403`) and the Studio author and publisher roles play no part. An `id` that is not a hyphenated
    UUID is a `404`. A valid `id` that no release or request names answers empty lists. `active` is
    the active release (`releaseId`, `number` and `contentHash`), or `null`. `releases` is the
    whole history, newest first, with the rows and fields of Studio's
    [releases route](studio.md): `releaseId`, `number`, `contentHash`, `activatedAt`, `actor`,
    `actorName`, `label`, `note` and `requestId`. It leaves out the folder's content hash, because
    the console has no Studio folders. `requests` is one page of the publish requests, newest
    first, and the answer has `page`, `pageSize` and `totalCount`. The optional `page` and
    `pageSize` are read as the other list routes read them, and an invalid one is a `400`. A
    request has its `id`, `kind`, `target`, `status`, `label`, `note`, `requestedBy`,
    `requestedByName`, `requestedAt`, `startedAt`, `finishedAt` and `releaseId`. It never holds the
    file snapshot, the diagnostics or the test results.
  - **`GET /api/console/dead-letters`.** Answers one page of the dead letters of every
    application of the tenant, newest first, as the instance list pages, including the letters
    that belong to no process. An optional `application` (an application id) keeps one
    application and an optional `state` (`open`, `redelivered` or `discarded`) keeps one state.
    An invalid `application`, `state`, `page` or `pageSize` is a `400` that names each. An item is
    a [process's dead letter](process-events.md#dead-letters) with its `applicationId` and
    `processId` (null for a letter with no process). Its `commands` are `redeliver` and `discard`
    while the letter is `open` and empty otherwise.
  - **`POST /api/console/dead-letters/{id}/redeliver` and `…/discard`.** Check, in order, the
    content type (`415`), the body, which is exactly `{ "reason": "<text>" }` (`400`), the `id`
    (`404` when it is not a hyphenated UUID or no dead letter has it) and the active release of
    the letter's application (`409` when it has none). Then one transaction runs the same command
    as the process routes, with the operator as actor. It answers `409` for a letter that is not
    `open`, for a trigger that is not in the active release and for a kind that cannot be
    redelivered, and writes nothing then. A success answers `200` with the settled letter.

## Views

Each view lists the newest items first, pages through them and filters by application.

- **Failed work.** Process instances that are `failed`, and connector and package calls whose
  outcome is `unknown`, across every process of every application. The list, its paging and the
  application filter are built. An entry opens the
  [instance detail](process-inspection.md#instance-detail) at `/console/instances/{id}`, with the
  graph, the history, the tasks and the calls. The page is outside every site, so it has no
  release texts and shows step names, not their labels. It offers no command yet.
- **Dead letters.** Event deliveries that reached the event delivery limit,
  including [webhook deliveries](outbound-webhooks.md#outbound-webhooks) and those that belong to
  no process. The view lists them in every state, with an application filter. Each shows the
  application, the kind, the event, the trigger or webhook, the attempts, the last error and the
  state. An `open` letter offers redeliver and discard. Built.
- **Webhook deliveries.** Recent deliveries of each webhook, with their status code and attempts.
  A table under the Tenant view with the application, webhook, event, state, attempts, status
  code and delivery time, and two filters, one for the application and one for the webhook. Built.
- **Schedules.** Each schedule trigger of each active release, with its last and next
  occurrence, see [schedules](process-events.md#schedules).
- **Publishing.** Publish requests and the [release history](studio.md) of each application,
  read-only. Publishing and rollback stay in Studio. A card under the Webhook deliveries view with
  a picker of the applications of the Tenant view, which starts on the first one. A releases table
  shows the number, label, note, who activated it and when, and marks the active release. A paged
  table shows each publish request with its kind, target, status, label, who asked, when it was
  asked and when it finished. It offers no publish, activate or rollback button. Built.
- **Tenant.** The tenant's status and each application's active release, as a table with the
  application, the release number and label, and the activation time. Built.

## Commands

- **Retry, cancel and resolve.** The [operator commands](process-inspection.md#operator-commands)
  with their rules, run from the instance detail. The operator role stands in for the `process`
  grants `retry`, `cancel` and `resolve`.
- **Redeliver and discard.** The commands on [dead letters](process-events.md#dead-letters), run
  from the Dead letters view. The operator role stands in for the `process` grant `retry`. Built.
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
