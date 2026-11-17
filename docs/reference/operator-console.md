# Operator console

The operator console is the web page where the platform operator sees what failed in a tenant and
runs the commands that fix it. This page covers who may open it, the routes it calls, its views and
its commands. Tenant provisioning is not part of it: that stays in [`Axis.Admin`](administration.md).

## Route and access

The console is a platform route of each tenant, open only to the installation's operator role, and
every route it calls checks that role on the server.

- **Route.** `/console` is a platform route on each tenant's hosts, outside every site, like
  `/studio`. It shows only the tenant of the host. There is no view across tenants.
- **Operator role.** An installation-level role, not a `role` resource. The server setting
  `Console:OperatorRole` names the roles claim value, see [console settings](host-settings.md#console-settings).
- **Application policies play no part.** The role gives read access to the console views across
  every application of the tenant, and the commands below. It gives no grant on records, data
  sources, tasks or Studio.
- **Header link.** `GET /api/me` answers `console: true` for a user who holds the role. The app
  switcher then lists the operator console, linked to `/console`. A user without the role who opens
  the route gets a `403` from the API and sees a forbidden page. A signed-out user gets the shell's
  sign-in prompt after the `401`.

### API

The console calls routes under `/api/console/`. Each needs a session (`401`) and the operator role
(`403`, also for everyone when `Console:OperatorRole` has no value), and every write needs the CSRF
header, as the other session routes do. They are checked on the server whatever the page shows.

| Route                                                     | What it does                                                              |
| --------------------------------------------------------- | ------------------------------------------------------------------------- |
| `GET /api/console/tenant`                                 | Answers the tenant of the host and its active applications                |
| `GET /api/console/failed-work`                            | Lists one page of the failed work of the tenant, searched, filtered and sorted in PostgreSQL |
| `GET /api/console/webhook-deliveries`                     | Lists one page of the tenant's webhook deliveries, searched, filtered and sorted in PostgreSQL |
| `GET /api/console/instances/{id}`                         | Answers one instance of the tenant, in the process inspection shape       |
| `POST /api/console/instances/{id}/retry`                  | Runs the retry command of an instance on its active release               |
| `POST /api/console/instances/{id}/cancel`                 | Runs the cancel command of an instance on its active release              |
| `POST /api/console/instances/{id}/calls/{callId}/resolve` | Runs the resolve command of a call of an instance                         |
| `GET /api/console/applications/{id}/publishing`           | Answers one application's release history and one page of its publish requests, read-only, the requests searched, filtered and sorted in PostgreSQL |
| `GET /api/console/dead-letters`                           | Lists one page of the tenant's dead letters, searched, filtered and sorted in PostgreSQL |
| `GET /api/console/schedules`                              | Lists one page of the schedule triggers of the active release of each application |
| `POST /api/console/dead-letters/{id}/redeliver`           | Redelivers an open dead letter                                            |
| `POST /api/console/dead-letters/{id}/discard`             | Discards an open dead letter                                              |

- **`GET /api/console/tenant`.** Answers the tenant of the host: `id`, `status` and
  `applications`. Each application has its `id`, `name`, `releaseId` (the active release),
  `releaseNumber` and `releaseLabel` (the number and label of that release in the application's
  release history, as Studio shows them) and `activatedAt`. The applications are the ones with an
  active release, in name order. The `status` is `active` or `readOnly`, because a host serves
  only those tenants: the hosts of a suspended tenant answer `503`.
- **`GET /api/console/failed-work`.** Answers one page of the failed work of the tenant.
  PostgreSQL filters, searches, sorts and pages it. The route takes `page`, `pageSize`, `sort`,
  `search` and `filter` in the [record API's format](record-api.md#paging-and-sorting) over the
  fields `application` (the application id), `process` (the process id), `instance` (the instance
  id), `stepOrCall`, `failedAt` and `error`. The `stepOrCall` field is the text the view shows: the
  step, or for a call `step: connector-or-package operation`. `failedAt` filters by its UTC day and
  sorts by its instant. A search looks in `stepOrCall` and `error`. A sort or filter on another
  field, and an invalid `page` or `pageSize`, is a `400` that names the parameter. Without `sort`
  the list is newest first, and a sorted list breaks ties newest first. The optional `application`
  id, a hyphenated UUID, keeps one application, and a malformed one is a `400` keyed
  `application`. The answer has the paging shape of the record API (`items`, `page`, `pageSize`,
  `totalCount`) and `processes`. Each item has `kind` (`instance`, `connector` or `package`),
  `applicationId`, `application`, `process`, `instanceId`, `step`, `callId`, `connector`,
  `package`, `operation`, `failedAt` and `error`. The keys that do not apply are `null`: an
  instance has no `callId`, `connector`, `package` or `operation`. The `application` and `process`
  names come from the active release, and from the release the instance is pinned to when the
  application is not active. An item never holds a call input, a history input or output, or a
  record value. The time of a failed instance is its `ended_at`, and its error is the newest
  history error of the instance. The time of a call with an unknown outcome is the finish of its
  newest attempt, or its `created_at` when it has no attempt row, and its error is the call's
  `last_error`. `processes` lists the processes of every active release, each with its `id`,
  `name`, `applicationId` and `application`, for the view's Process filter.
- **`GET /api/console/webhook-deliveries`.** Answers one page of the webhook deliveries of the
  tenant. PostgreSQL filters, searches, sorts and pages them. The route takes `page`, `pageSize`,
  `sort`, `search` and `filter` in the [record API's format](record-api.md#paging-and-sorting) over
  the fields `application` (the application id), `webhook` (the webhook id), `event` (the event
  occurrence id), `state`, `attempts`, `statusCode` and `deliveredAt`. `deliveredAt` filters by its
  UTC day and sorts by its instant. A search looks in `event`, the occurrence id that a dead letter
  shows as its event. A sort or filter on another field, and an invalid `page` or `pageSize`, is a
  `400` that names the parameter. Without `sort` the deliveries are newest first, and a sorted list
  breaks ties newest first. The optional `application` and `webhook` ids, each a hyphenated UUID,
  keep one application or one webhook, and a malformed one is a `400` that names it. An item has its
  `id`, `applicationId`, `application`, `webhookId`, `webhook`, `eventId` and `event`, the names of
  the application, the webhook and the event as the release the delivery was staged with holds
  them (`null` when that release no longer holds the webhook), then the `state`, the `attempts`,
  the last `statusCode`, `createdAt` and `deliveredAt`. The `state` is `delivered`, `pending`,
  `deadLettered` (an open dead letter of the delivery) or `discarded` (a discarded one). A
  redelivered dead letter is queued again and shows as `pending`. The answer also lists
  `webhooks`, the webhooks of every active release with their application, for the view's Webhook
  filter, and `totalCount`. It never holds a body, a URL, a secret or a binding value.
- **`GET /api/console/instances/{id}`.** Answers one instance of the tenant, whatever its
  application or process, in the shape of the
  [process inspection detail](process-inspection.md#instance-detail). An id that is not a
  hyphenated UUID and an id that names no instance are one `404`. The operator role stands in
  for the `inspect` grant and for the subject record's read check: the instance of an application
  that is no longer active opens too, and its subject record need not exist. The operator reads
  fields like a `read` grant with no `fields` list, so a call's `input` is hidden only by the
  [masking rules](process-inspection.md#values-and-masking), when its mapping uses a `sensitive`
  field or a field the active release no longer has. `commands` lists the commands that apply to
  the instance and a call is `resolvable`, as for a user who holds the grants, because the
  operator role stands in for them. They are listed only when the application has an active
  release that still has the instance's process, and an instance of a process without a subject
  lists none, as in the process inspector. Otherwise `commands` is empty and no call is
  `resolvable`. When the application has no active release, the release the instance is pinned
  to stands in for the active one.
- **`POST /api/console/instances/{id}/retry`, `…/cancel` and `…/calls/{callId}/resolve`.**
  Check, in order, the content type (`415`), the body (`400`), which is the body of the
  [process route](process-inspection.md#operator-commands), the `id` and then the `callId`
  (`404` when it is not a hyphenated UUID), the instance (`404` when none has the id), the
  active release of its application (`409` when it has none) and the instance's process in
  that release (`409` when it is no longer there). The CSRF middleware refuses a write without
  its header with `403`. Then the command runs against the active release, in the same
  transaction and under the same start lock as the process route, with the operator as actor.
  The operator needs no grant in the application, and the operator role also stands in for the
  `read` check on the subject record, so a command runs even when the operator could not read
  that record. The rules, bodies and `409` errors are those of the process routes. A command
  that is refused writes nothing. A success answers `200` with the instance, as the process
  routes do.
- **`GET /api/console/applications/{id}/publishing`.** Answers one application, named by its id,
  with its release history and one page of its publish requests, read-only. The group's checks apply (`401`,
  `403`) and the Studio author and publisher roles play no part. An `id` that is not a hyphenated
  UUID is a `404`. A valid `id` that no release or request names answers empty lists. `active` is
  the active release (`releaseId`, `number` and `contentHash`), or `null`. `releases` is the
  whole history, newest first, with the rows and fields of Studio's
  [releases route](studio.md): `releaseId`, `number`, `contentHash`, `activatedAt`, `actor`,
  `actorName`, `label`, `note` and `requestId`. It leaves out the folder's content hash, because
  the console has no Studio folders. `requests` is one page of the publish requests, which
  PostgreSQL filters, searches, sorts and pages. The route takes `page`, `pageSize`, `sort`,
  `search` and `filter` in the [record API's format](record-api.md#paging-and-sorting) over the
  fields `kind`, `target`, `status`, `label`, `requestedBy`, `requestedAt` and `finishedAt`.
  `requestedAt` and `finishedAt` filter by their UTC day and sort by their instant. A search looks
  in `label` and `requestedBy`, where `requestedBy` is the name stored with the request, or the user
  id when none was stored. A sort or filter on another field, and an invalid `page` or `pageSize`,
  is a `400` that names the parameter. Without `sort` the requests are newest first, and a sorted
  list breaks ties newest first. The answer has `page`, `pageSize` and `totalCount`. A request has
  its `id`, `kind`, `target`, `status`, `label`, `note`, `requestedBy`, `requestedByName`,
  `requestedAt`, `startedAt`, `finishedAt` and `releaseId`. It never holds the file snapshot, the
  diagnostics or the test results.
- **`GET /api/console/dead-letters`.** Answers one page, `{ "items": [...], "page", "pageSize",
  "totalCount" }`, of the dead letters of every application of the tenant, including the letters
  that belong to no process. PostgreSQL filters, searches, sorts and pages them. The route takes
  `page`, `pageSize`, `sort`, `search` and `filter` in the
  [record API's format](record-api.md#paging-and-sorting) over the fields `application` (the
  application id), `kind`, `event`, `target` (the trigger or webhook id), `attempts`,
  `lastError`, `state` and `createdAt`. `createdAt` filters by its UTC day and sorts by its
  instant. A search looks in `kind` and `lastError`. A sort or filter on another field, and an
  invalid `page` or `pageSize`, is a `400` that names the parameter. Without `sort` the letters
  are newest first, and a sorted list breaks ties newest first. An optional `application` (an
  application id) keeps one application and an optional `state` (`open`, `redelivered` or
  `discarded`) keeps one state. An invalid `application` or `state` is a `400` that names it. An
  item is a [process's dead letter](process-events.md#dead-letters) with its `applicationId` and
  `processId` (null for a letter with no process). Its `commands` are `redeliver` and `discard`
  while the letter is `open` and empty otherwise.
- **`GET /api/console/schedules`.** Answers one page, `{ "items": [...], "page", "pageSize",
  "totalCount" }`, of the schedule triggers of the active release of every application. It takes
  `page`, `pageSize`, `sort`, `search` and `filter` in the
  [record API's format](record-api.md#paging-and-sorting) over the fields `application`,
  `trigger`, `process`, `schedule`, `timeZone`, `lastOccurrenceAt` and `nextOccurrenceAt`. A time
  filters by its UTC day and sorts by its instant. A search looks in `application`, `trigger` and
  `process`. A sort or filter on another field, and an invalid `page` or `pageSize`, is a `400`
  that names the parameter. Without `sort` the items are ordered by application name and then by
  trigger name. An optional `application` (an application id) keeps one application. One that is
  not a hyphenated UUID is a `400` keyed `application`. A module's schedule trigger is listed
  once for each application that uses the module. A trigger that the active release no longer
  has, and every trigger of an application with no active release, is left out. Each item has
  `applicationId`, `application`, `triggerId`, `trigger`, `process`, `schedule`, `timeZone`,
  `lastOccurrenceAt` (`null` while no occurrence has run) and `nextOccurrenceAt`.
- **`POST /api/console/dead-letters/{id}/redeliver` and `…/discard`.** Check, in order, the
  content type (`415`), the body, which is exactly `{ "reason": "<text>" }` (`400`), the `id`
  (`404` when it is not a hyphenated UUID or no dead letter has it) and the active release of
  the letter's application (`409` when it has none). Then one transaction runs the same command
  as the process routes, with the operator as actor. It answers `409` for a letter that is not
  `open`, for a trigger that is not in the active release and for a kind that cannot be
  redelivered, and writes nothing then. A success answers `200` with the settled letter.
  `ConsoleRouteGuardTests` reads every route under `/api/console/` from the endpoint data source, so a
  new route is checked without a list. It expects `401` signed out, `403` without the role and, for a
  write, `403` without the CSRF header. Every read must answer the operator `200` without a seeded
  sensitive value, secret, binding value or webhook body. `ConsoleRouteGuardSelfTests` shows that the
  guard reports a route that breaks each rule.

## Views

The views show the tenant's failed work and its history of deliveries, schedules and releases. Each
view filters by application. Unless a view says otherwise, it lists the newest items first and pages
through them. Each view is a part of the page, see [layout](shell.md#layout).

- **Failed work.** Process instances that are `failed`, and connector and package calls whose
  outcome is `unknown`, across every process of every application. An entry opens the
  [instance detail](process-inspection.md#instance-detail) at `/console/instances/{id}`, with the
  graph, the history, the tasks and the calls. The page is outside every site, so it has no
  release texts and shows step names, not their labels. Its tables filter as the
  [instance page's](process-inspector-widget.md#instance-page) do. It offers the commands that the detail
  lists. The view is paged on the server. Its table has the toolbar of
  [tables](actions-and-controls.md#tables) with Filter, and a pager. Each column filters, and the
  Application and Process columns filter by name, so the view has no Application picker. The
  Process filter offers the processes of the active releases. The address keeps the view as
  `failedWork.filter`, `failedWork.page` and `failedWork.pageSize`, because the page has other
  tables.
- **Dead letters.** Event deliveries that reached the event delivery limit,
  including [webhook deliveries](outbound-webhooks.md#outbound-webhooks) and those that belong to
  no process. The view lists them in every state, newest first until it is sorted, and is paged on
  the server. Each shows the application, the kind, the event, the trigger or webhook, the
  attempts, the last error, the state and the time it was created. Its table has the toolbar of
  [tables](actions-and-controls.md#tables), with Search, Filter, Columns and Refresh, a sort on
  Kind, Attempts, State and Created, and a pager with a page size. The Application column filters
  on the applications of the tenant by name, and the view has no Application picker. Search looks
  in the kind and the last error. The address keeps the view as `deadLetters.search`,
  `deadLetters.filter`, `deadLetters.sort`, `deadLetters.page`, `deadLetters.pageSize` and
  `deadLetters.columns`, because Schedules on the same page keeps the plain names. An `open`
  letter offers redeliver and discard.
- **Webhook deliveries.** Recent deliveries of each webhook, with their status code and attempts.
  The view lists them newest first until it is sorted, and is paged on the server. Each shows the
  application, the webhook, the event, the state, the attempts, the last status code and the
  delivery time. Its table has the toolbar of [tables](actions-and-controls.md#tables), with
  Search, Filter, Columns and Refresh, a sort on State, Attempts, Status code and Delivered, and a
  pager with a page size. The Application and Webhook columns filter by name, and the view has no
  pickers above the table. The Webhook filter offers the webhooks of the active releases, so a
  delivery whose webhook an older release held cannot be picked by name. The Event column has no
  filter, and search finds the event occurrence id. The address keeps the view as
  `webhookDeliveries.search`, `webhookDeliveries.filter`, `webhookDeliveries.sort`,
  `webhookDeliveries.page`, `webhookDeliveries.pageSize` and `webhookDeliveries.columns`, because
  Schedules keeps the plain names and Failed work, Dead letters and Publishing their own prefixes.
- **Schedules.** Each schedule trigger of each active release, with its process, its schedule and
  time zone, and its last and next occurrence, see [schedules](process-events.md#schedules). The
  view is ordered by application and trigger name until it is sorted and is paged on the server.
  Its table has the toolbar of [tables](actions-and-controls.md#tables), with Search, Filter,
  Columns and Refresh, a sort on every column but Schedule, and a pager with a page size. The
  address keeps the view as `search`, `filter`, `sort`, `page`, `pageSize` and `columns`.
- **Publishing.** Publish requests and the [release history](studio.md) of each application,
  read-only. Publishing and activating a release stay in Studio. A picker of the applications of
  the tenant sits above both tables and starts on the first one. The picker is not kept in the
  address, so a link with a `publishRequests.` view opens the first application. A releases table
  shows the number, label, note, who activated it and when, and marks the active release. Who
  activated it is named in words, as [names](studio-workspace.md#names) says for Studio. It marks
  an [off-standard release](module-contracts.md#off-standard-releases) with the Off-standard tag and
  the module, point and reason of each relaxed entry, as Studio does. The releases are never paged.
  The publish requests table is paged on the server and shows each request with its kind, target,
  status, label, who asked, when it was asked and when it finished. Its table has the toolbar of
  [tables](actions-and-controls.md#tables), with Search, Filter, Columns and Refresh, a sort on
  Status, Label, Requested by, Requested and Finished, and a pager with a page size. Kind and Target
  filter by their values and do not sort. Requested by filters, searches and sorts on the name
  stored with the request, so it can differ from the name the cell shows after a person is renamed.
  The address keeps the requests view as `publishRequests.search`, `publishRequests.filter`,
  `publishRequests.sort`, `publishRequests.page`, `publishRequests.pageSize` and
  `publishRequests.columns`. Picking another application starts again at page 1 and keeps the rest
  of the view. A failed load shows the error in each table in place of its rows, and the requests
  table keeps its toolbar. It offers no publish or activate button.
- **Tenant.** The tenant's status and each application's active release, as a table with the
  application, the release number and label, and the activation time. The table has Filter on
  Application, Release and Activated (the day), kept in the address as `applications.filter`. It
  shows every row with no pager.

## Commands

The console runs the operator commands of processes and dead letters, with the operator role in place
of the grants, and nothing that changes the tenant itself.

- **Retry, cancel and resolve.** The [operator commands](process-inspection.md#operator-commands)
  with their rules, run from the instance detail. The operator role stands in for the `process`
  grants `retry`, `cancel` and `resolve`.
- **Redeliver and discard.** The commands on [dead letters](process-events.md#dead-letters), run
  from the Dead letters view. The operator role stands in for the `process` grant `retry`.
- **A reason each time.** Every command needs a reason. It writes the same history rows and audit
  records as from the process inspector, with the operator as actor.
- **Nothing else.** The console has no command that creates, suspends, backs up or restores a
  tenant, sets a binding, migrates a database or activates a release. Those stay in
  [`Axis.Admin`](administration.md).

## What the console never shows

The console shows the operator what failed and why, never the data or the secrets of the tenant:

- A field marked `sensitive`, by the [masking rules](process-inspection.md#values-and-masking)
  of process inspection.
- Record data beyond what process inspection holds. The console reads no record through the
  record API.
- A secret, a binding value or a webhook body.
