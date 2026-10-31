# Process inspection and operator commands

Detailed reference for process inspection, where authorized users see an instance's graph, path,
tasks and calls, and for the operator commands retry, cancel and resolve.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Process inspection

This section follows
[D34](../decisions/d34-process-inspection.md). Authorized users see
where an instance is, what it did and why it failed or waits. Two routes list
the instances of a process and read one of them. The `inspect` grant and the
[instance list](#instance-list) and the [instance detail](#instance-detail)
are built, with the detail's `calls` and `timerDueAt`. The `sensitive` flag, which hides more
values, is built too. The widget is built, and so is the record history link to an
instance.
The user is the user of the
[session](security.md#sign-in-and-the-session), or a
[service account](security.md#service-accounts) with a bearer token. A bad
token is a `401` with `WWW-Authenticate: Bearer`. The
[process inspector widget](process-inspector-widget.md) shows the same data.

**Grant.** A `process` grant takes the action `inspect`, next to `start`.
See [Roles and policies](roles-and-policies.md). The grant
decides who may inspect, and the signed-in user needs nothing else. A user who
holds only `start` cannot inspect, and a user who holds only `inspect` cannot
start.

**Order of checks.** A request is checked in this order, and the first
failure is the response:

1. The session or the bearer token, `401`. The title is the record API's "Sign
   in to read or write this data.", or the bearer challenge's for a token.
2. The application and the process, `404`. An unknown application or one with
   no active release has the title "No application is active under this
   name.". An unknown process has the title "The application has no process
   with this name.".
3. The `inspect` grant on the process, `403`. The title is the record API's
   "You have no grant for this action.". The grant comes from the policies of
   the active release.
4. The query parameters of the list, `400`.
5. A `read` filter of the user on the subject entity that reads a pending
   field, `409`. For the detail, a malformed `{id}` is a `404` first.
6. For the detail, the instance, `404`. An `{id}` that is not a UUID in the
   hyphenated form, an unknown id, an instance of another process or
   application, and an instance whose subject record the user may not read all
   give one answer, with the title "No instance of this process exists with
   this id.". So the user cannot tell them apart.

A process without a subject has no subject record. Step 5 and the subject
record part of step 6 do not apply to it, so the `inspect` grant alone is
enough, see [D70](../decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md).

Both routes only read. They write no audit record.

### Instance list

`GET /api/apps/{app}/processes/{process}/instances?state=&subject=&page=&pageSize=`
lists the instances of the process, whatever release they are pinned to.

- `page` and `pageSize` follow the record API's
  [paging rules](record-api.md#paging-and-sorting). There is no `sort` or
  `search`.
- `state` is optional. It is one of `running`, `waiting`, `completed`, `failed`
  and `cancelled`. Any other value is a `400` keyed `state`.
- `subject` is optional. It is a record id in the hyphenated UUID form.
  Anything else is a `400` keyed `subject`.
- Instances are ordered by `created_at` descending, then by `id` descending.
- The subject entity's read filters of the user's `read` grants go into the
  SQL. An instance whose subject record the user may not read is left out of
  the page and of `totalCount`. A user with no `read` grant on the subject
  entity sees an empty list.
- A `subject` filter that names a record the user may not read, or that does
  not exist, gives an empty page. It is not a `404`. A list never reveals that
  a hidden record exists.
- A process without a subject lists its instances, with `subjectId` `null`, on
  the `inspect` grant alone. No `read` grant or filter applies. A `subject`
  filter gives an empty page, because these instances have no subject record.

```json
{
  "items": [
    {
      "id": "0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b",
      "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
      "subjectLabel": "Replace the worn carpet tiles",
      "state": "waiting",
      "step": "managerApproval",
      "releaseId": "0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01",
      "releaseNumber": 3,
      "createdAt": "2026-10-10T02:05:00.123456Z",
      "endedAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

- `step` is the step the instance runs next, or waits on. `endedAt` is `null`
  until the instance is `completed` or `failed`.
- Each item also has `subjectLabel` and `releaseNumber`, so the
  [process inspector widget](process-inspector-widget.md) shows no identifier
  ([D96](../decisions/d96-rules-from-the-design-system-review.md)).
  `subjectLabel` is the value of the subject record's display field, such as
  `"Replace the worn carpet tiles"`. It is `null` in these cases: the process
  has no subject, the entity has no display field, the user's field grants on
  the subject entity do not cover the display field, or the display field is
  [sensitive](roles-and-policies.md#sensitive-fields) in the active release or in the release the
  instance is pinned to. `releaseNumber` is the number of the release the
  instance is pinned to in the application's
  [release history](storage.md#release-history), such as `3`, a JSON number, so
  the widget writes no API text of its own. It is `null` only for a release
  with no row in the release history, such as an old release from before the
  history existed. Both come from the one query that reads the page.

### Instance detail

`GET /api/apps/{app}/processes/{process}/instances/{id}` returns one instance
in any state. The body has these parts:

- `id`, `process`, `subjectId`, `releaseId`, `state`, `step`, `createdAt` and
  `endedAt`, as in the list. `process` is the process name. `subjectId` is
  `null` for an instance of a process without a subject.
- `timerDueAt` is the due time of the instance's `process.timer` work item while
  the instance is `waiting` on a timer step, as a UTC time like `createdAt`. It
  reads only the item of the instance's current revision, so a stale item from
  an earlier pass of a loop is not shown. It is `null` in every other case. It
  is the same for every user who may inspect the instance, see
  [D69](../decisions/d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md).
- `parent` is `{ "id", "process", "step" }` for an instance that a `subProcess`
  step started, and `null` for any other instance. `step` is the name of the
  `subProcess` step in the parent that started this instance. It is not the step
  this instance is at.
- `subProcesses` are the instances that this instance started, ordered by
  `createdAt`, then by `id`. Each is
  `{ "id", "process", "step", "state", "createdAt", "endedAt" }`. `step` is the
  name of the `subProcess` step in this instance that started the child.
  - A parent or child appears only when the user holds `inspect` on its process
    in the active release. A parent the user may not inspect, or whose process
    the active release no longer has, shows as `null`. Such a child is missing
    from the list.
  - Neither has a subject id, and the read filters of the subject record do
    not apply. The `inspect` grant alone decides. The user may still get `404`
    when they open a child whose subject record they may not read.
- `graph` is the graph of the release the instance is pinned to:
  `{ "start", "steps": [ { "name", "type", "labelKey", "transitions" } ] }`.
  - `type` is the step type: `decision`, `task`, `operation`, `timer` or `end`.
  - `labelKey` is the step's label text key, or `null` when it has none.
  - `transitions` is a list of `{ "next", "on" }`, one for each step the step
    can lead to. `on` is always text or `null`. It is the zero-based index of
    a decision branch as text, such as `"0"`, as in the history's `decision`,
    then `"otherwise"` for the fallback branch, the name of a task outcome, or
    `null` for the `next` of an operation, a `subProcess` or a timer. An `end` step has none.
  - A `callConnector`, package, `raiseEvent` and `subProcess` step have type `operation`.
  - `labelKey` is set only for a `task` step.
- `path` is the history rows of the instance, ordered by `finished_at`, then
  by `id`. Each is
  `{ "step", "startedAt", "finishedAt", "decision", "output", "error" }`.
  - `decision` and `output` are the stored values, see
    [Tables](process-tables.md#tables). `output` holds no field values.
  - `error` is the stored error, see
    [Step errors](#step-errors-in-inspection).
  - The history's `input` is not returned. It holds only the subject id and
    version, which the instance already has.
- `tasks` are the tasks of the instance, ordered by `createdAt`, then by `id`.
  Each is
  `{ "id", "step", "assigneeKind", "assignee", "state", "outcome", "completedBy", "createdAt", "completedAt" }`.
  `assigneeKind` is `user` or `role`. `outcome`, `completedBy` and
  `completedAt` are `null` while the task is `open`. A task row holds no field
  values.
- `calls` are the connector and package calls of the instance, ordered by
  `createdAt`, then by `id`. Each is
  `{ "id", "kind", "step", "connector" or "package" with "version" and "idempotent", "operation", "state", "attempts", "input", "inputHidden", "resolvable", "outputs" }`.
  - `kind` is `connector` or `package`. A connector call has `connector`. A
    package call has `package` and `version`, the version its release pins.
  - `state` is the call's state: `pending`, `sending`, `unknown`,
    `succeeded` or `failed`.
  - `attempts` lists the rows of [Attempts](#attempts) for the call, ordered
    by `startedAt`, then by `id`. Each is
    `{ "kind", "number", "startedAt", "finishedAt", "outcome", "httpStatus", "error" }`,
    with `kind` either `attempt` or `lookup`.
  - `input` and `inputHidden` follow [Values and masking](#values-and-masking).
  - A call has no output. The engine writes the outputs of a call straight to
    the subject record and stores no response body.
  - `idempotent` is only on a package call. It is the value stored when the
    engine staged the call, for the inspector's retry warning.
  - `resolvable` is `true` when the user holds the `resolve` grant, the call
    is `unknown`, the instance is `waiting` on the call's step, and the
    operation is declared. A call that does not meet all four has `false`.
  - `outputs` is only on a resolvable call. It lists the operation's declared
    outputs as `{ "name", "type", "required" }`, from the connector operation
    of the pinned release or the manifest of the pinned package version. When
    the pinned release lacks the connector operation, or the package version is
    not installed, the call is not resolvable.
  - A connector call has no `package`, `version` or `idempotent` key, and a
    package call has no `connector` key.
- `commands` lists the [operator commands](#operator-commands) the user may
  take on the instance. It is an array and may be empty.
  - It holds `retry` when the instance is `failed` and the user holds the
    `retry` grant.
  - It holds `cancel` when the instance is `running` or `waiting`, none of
    its calls is `sending` or `unknown`, and the user holds the `cancel`
    grant.
  - The grants are read from the policies of the active release, as the
    commands themselves do.
  - It is empty for an instance of a process without a subject, and none of
    its calls is `resolvable`, because the commands still answer `404` for it.
  - It does not run the other checks of a retry: another active instance for
    the record, or a destructive migration since the pinned release. A command
    in the list can still be refused with `409`.

```json
{
  "id": "0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b",
  "process": "PurchaseRequestApproval",
  "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
  "releaseId": "0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01",
  "state": "waiting",
  "step": "registerPurchase",
  "createdAt": "2026-10-10T02:05:00.123456Z",
  "endedAt": null,
  "timerDueAt": null,
  "parent": null,
  "subProcesses": [],
  "graph": {
    "start": "managerApproval",
    "steps": [
      {
        "name": "managerApproval",
        "type": "task",
        "labelKey": "purchaseRequest.managerApproval",
        "transitions": [
          { "next": "registerPurchase", "on": "approve" },
          { "next": "done", "on": "reject" }
        ]
      },
      {
        "name": "registerPurchase",
        "type": "operation",
        "labelKey": null,
        "transitions": [{ "next": "done", "on": null }]
      },
      { "name": "done", "type": "end", "labelKey": null, "transitions": [] }
    ]
  },
  "path": [
    {
      "step": "managerApproval",
      "startedAt": "2026-10-10T02:05:00.123456Z",
      "finishedAt": "2026-10-10T02:05:00.130000Z",
      "decision": null,
      "output": { "state": "waiting", "taskId": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01" },
      "error": null
    }
  ],
  "tasks": [
    {
      "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
      "step": "managerApproval",
      "assigneeKind": "role",
      "assignee": "manager",
      "state": "completed",
      "outcome": "approve",
      "completedBy": "manager",
      "createdAt": "2026-10-10T02:05:00.123456Z",
      "completedAt": "2026-10-10T04:00:00.000000Z"
    }
  ],
  "calls": [
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c",
      "kind": "connector",
      "step": "registerPurchase",
      "connector": "PurchasingSystem",
      "operation": "createOrder",
      "state": "pending",
      "attempts": [
        {
          "kind": "attempt",
          "number": 1,
          "startedAt": "2026-10-10T04:00:01.000000Z",
          "finishedAt": "2026-10-10T04:00:03.000000Z",
          "outcome": "retryable",
          "httpStatus": 503,
          "error": null
        }
      ],
      "input": null,
      "inputHidden": true,
      "resolvable": false
    }
  ],
  "commands": []
}
```

The example shortens the lists. The graph, the path, the tasks and `timerDueAt` are the
same for every user who may inspect the instance. Only a call's `input`,
`commands`, `parent` and `subProcesses`, and each call's `resolvable` and `outputs` differ between users.

### Values and masking

Inspection shows no stored field value except a call's `input`. The history's
`input` and `output` hold only ids, versions, step names and outcomes, and a
task row holds none, so they need no masking. A call's `input` is the
evaluated input that the call sent, so it holds field values.

- **The hiding rule.** A call's `input` is shown only when the user may read
  every field that the call's mapping reads or writes. The mapping is the
  step's `input` and its `output`. Otherwise `input` is `null` and
  `inputHidden` is `true`. This fails closed: a partly readable input is never
  shown, and the user sees none of it.
- **Which fields count.** The fields are every field the mapping reads or
  writes, on any entity, not only the subject entity.
  - A path through `reference` fields counts every field along the path.
  - A named rule that a mapping expression uses counts the fields the rule
    reads, and so does every rule that rule uses.
  - A child field counts through the owner's grant, because child rows are
    read through their owner's `fields` list. A list mapping of a
    `callConnector` step counts the owner's child collection field and the
    fields reached through reference paths. It does not count the child
    entity's own fields, as an aggregate does not.
  - The `output` mapping counts, even though no output value is stored or
    shown. A user who may not read a field that the call writes sees no
    input.
- **Readable.** A field is readable when it is in the union of the `fields`
  of the user's `read` grants on its entity, from the policies of the active
  release. A grant with no `fields` covers every field. A user with no `read`
  grant on an entity reads none of its fields. Only the `fields` lists are
  used here. The record filters of the grants are not, because they select
  records and a call input is not a record.
- **Pinned and active release.** The fields a mapping reads or writes come
  from the step in the release the instance is pinned to. Which of them the
  user may read comes from the policies of the active release, as for
  [tasks](task-api.md) and [D29](../decisions/d29-release-pinning-and-text-resources.md).
  A field that the active release no longer has counts as unreadable.
- **No subject.** A call's `input` of an instance of a process without a
  subject is always hidden. The mapping reads parameters, not fields, and they
  can carry field values from a record that no longer exists, so the rule
  cannot show that the input is safe.
- **Sensitive fields.** A field that declares
  [`sensitive`](roles-and-policies.md#sensitive-fields) is never shown in
  inspection, for any user. A call whose mapping reads or writes one has its
  `input` hidden. A field counts as sensitive when the pinned release or the
  active release marks it. So a release that adds or removes the flag does not
  expose instances that still run on an older release. The record API still
  shows the field to users whose grants cover it. A `child-collection` field in
  a mapping counts as sensitive when any field of its child entity is
  sensitive, in either release.
- **`inputHidden`.** It is `true` only when the rule above hides an `input`
  that is stored. It is `false` when `input` is shown, and when the input is
  no longer stored: `input` is then `null` too. A package call keeps its
  input only while the call is `pending` or `unknown`, and clears it when it
  settles, so inspection shows a settled package call with `input` `null` and
  `inputHidden` `false`.

The [operator console](operator-console.md#route-and-access) shows the same detail with the same
rules and the operator as the reader. The operator reads every field that the active release has, so only a
sensitive field, or a field the active release no longer has, hides an `input`.

### Call input kept

A connector call keeps its input after it settles, whether it succeeded or
failed. The kept input lets inspection show what was sent. It is never copied
to history, audit records or logs, and inspection
shows it only under the [masking rule](#values-and-masking). Package calls
keep today's rule: their input is cleared when the call settles, see
[Staging a package call](process-execution.md#staging-a-package-call).

### Attempts

`axis.process_call_attempts` holds one row per attempt and per lookup of a
connector or package call, see [Tables](process-tables.md#tables). The call row counts its
attempts and keeps only the last status and error, so the attempt rows hold the
earlier ones.

- **Attempt.** One send of a connector call, or one run of a package
  operation. Its outcome is `succeeded`, `retryable`, `unknown` or `failed`,
  the classes of [Call outcomes](process-execution.md#call-outcomes) and
  [Package call outcomes](process-execution.md#package-call-outcomes).
- **Lookup.** One lookup of [reconciliation](process-execution.md#reconciliation). Its outcome is
  `found`, `notFound` or `failed`. Package calls have none.
- **Numbers.** `number` counts attempts and lookups apart, each from 1. An
  attempt's number is the highest attempt number of the call plus one, as a
  lookup's number is the highest lookup number plus one. In the call's first
  run it equals the call's `attempts` count when the attempt ran, the number
  in `connector.called` or `operation.called`. After an operator
  [retry](#retry) the call's `attempts` count from 0 again, while the numbers
  carry on, so history is kept.
- **Outcome.** A row's outcome is the class of that attempt, not what became
  of the call. A retryable error with no attempts left is recorded as
  `retryable`, and the call becomes `failed`. The one exception is a `2xx`
  answer or an output whose record write is refused: the attempt did not
  succeed, so its row is `failed`. A lookup that found the call stays `found`.
- **Writing.** The outcome transaction of the attempt or lookup writes its
  row, in the same transaction that updates the call. A rolled back outcome
  leaves no row. A worker that crashes after it sends leaves no row either. The
  next worker finds the call still `sending` and writes that attempt's row
  with the outcome `unknown`. Its `started_at` is when that worker found the
  call, because the start of the lost attempt is not stored. An outcome that is
  stale, because the instance moved on, writes no row, and neither does a
  call whose release lacks the step before any attempt was counted.
- **Content.** A row holds the times, the outcome, the HTTP status and a fixed
  error text. It never holds a request or response body, an exception message
  or a secret.

### Step errors in inspection

The `error` of a path entry is the history's stored error. The engine
stores it as one of these, and never as the exception message, because a
message can hold a value:

- The exception type, such as `System.DivideByZeroException`.
- A fixed text that names the step, the part (branch, field, input, payload
  field or assignee) and the kind of expression error, such as
  `The when of branch 0 of the step 'check' failed: DivisionByZero.`
- A fixed text that holds the field pointer and the message key, such as the
  refused-write text of [Running a step](process-execution.md#running-a-step).
- For a connector call that cannot be sent or whose success cannot be
  recorded, a fixed text that ends with the exception type. The same text is
  the call's `last_error`.

None of them holds a value.

History rows written before this change may still hold exception messages.
Inspection shows them as stored.

### Inspection errors

Errors are problem details, as in the [record API](record-api.md#errors). The
titles are fixed and never contain text from the request.

- **`401`.** Nobody is signed in.
- **`404`.** An unknown application, process or instance, as the order of
  checks says.
- **`403`.** The user has no `inspect` grant on the process.
- **`400`.** A list parameter is invalid: `page`, `pageSize`, `state` or
  `subject`, keyed by its name. The detail has none.
- **`409`.** A `read` filter of the user's grants on the subject entity reads a
  [pending field](migrations.md#recomputing-computed-fields). The title is the
  record API's "Your access to this entity cannot be checked while a field is
  being recomputed.", with no `errors`. It comes before any instance is looked
  up, so it holds for an unknown instance too.
- **`400`.** A read filter of the user fails at run time with a database data
  error for some record, in the list only. The title is the record list's "A
  record filter could not be evaluated for these records.". The server logs
  each filter by policy name and grant index. The detail checks one record, so
  a filter that fails for it does not match, and the detail answers `404`.

## Operator commands

This section follows
[D35](../decisions/d35-operator-retry-cancel-and-resolve.md). Cancel,
retry and resolve are built. Three
commands let an authorized user act on process work that the engine cannot
finish alone. **Retry** runs a `failed` instance again. **Cancel** stops a
`running` or `waiting` instance. **Resolve** settles a connector or package
call whose outcome is `unknown`. Each is an audited command with its own
grant, a required reason and one transaction. The
[instance detail](#instance-detail) lists the commands the user may take. The
[process inspector widget](process-inspector-widget.md) shows Cancel, Retry and Resolve.

Two more commands, **redeliver** and **discard**, act on a dead letter of the
process with the same `retry` grant, a required reason and an audit record, see
[Dead letters](process-events.md#routes). The widget shows Redeliver and
Discard on each open dead letter whose `commands` list them.

Cancel also cancels an instance's running and waiting sub-processes, see
[Cancel](#cancel). An instance without a subject needs only the grant, see
[processes without a subject](process-flow.md#processes-without-a-subject).

```http
POST /api/apps/{app}/processes/{process}/instances/{id}/retry
POST /api/apps/{app}/processes/{process}/instances/{id}/cancel
Content-Type: application/json

{ "reason": "The supplier code is fixed." }
```

```http
POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve
Content-Type: application/json

{
  "reason": "Order PO-2026-0001 exists in the purchasing system.",
  "outcome": "succeeded",
  "output": { "orderNumber": "PO-2026-0001" }
}
```

The content type follows the same rules as the record API, see
[Create, update and delete](record-api.md#create-update-and-delete). `{id}` is
the instance id and `{callId}` is the call's operation identity. A success is
`200` with the instance, in the shape of a start's body:
`{ "id", "process", "subjectId", "releaseId", "state" }`.

- `reason` is required. It must not be blank and has at most 500 characters.
- `outcome` is required for a resolve, and is `succeeded` or `failed`.
- `output` is for a resolve. It is allowed only with `succeeded`.

### Who may act

A `process` grant takes the actions `retry`, `cancel` and `resolve`, as well as
`start` and `inspect`, see [Authorization](security.md#authorization).
A [service account](security.md#service-accounts) may run the commands with a
bearer token, under the grants of its roles. It needs no session and no CSRF
header. The audit records name it as the actor, by its id.
Each is checked
on its own, and none implies another. The subject record must also be readable
by the user, by the same rule as a start. A subject record the user may not
read makes the instance an unknown instance, `404`, so the user cannot tell the
two apart. An instance without a subject has no record, so the grant is the
only check. No `read` check runs and there is no policy `409` for a pending
field. The check depends on the stored instance, so an instance with a subject
is never exempt from the read check. Every request is also checked on the server, whatever the widget
shows. _(planned for M23)_ In the [operator console](operator-console.md), the
operator role stands in for these grants.

### Retry

Retry takes an instance that is `failed`. The instance stays on its pinned
release. Before it looks for the instance, it answers the policy `409` when a
`read` filter of the user reads a pending field, see
[Operator command errors](#operator-command-errors).

One transaction does this:

1. It takes the [start lock](processes.md#releases-and-running-work) as its first
   statement, as a start does. So it cannot race the old-release check of a
   destructive migration. When the active release changed under the lock, the
   command runs again on the new release. After three changes it answers `503`.
2. It locks the instance and requires it to be `failed`, otherwise `409`.
3. It requires that no other `running` or `waiting` instance exists for the
   process and the subject record, otherwise `409`. For a process without a
   subject, no other instance of the process may be `running` or `waiting`.
4. It requires that no applied `renameField`, `dropField`, `dropEntity` or
   `convert` migration changed a column that the pinned release uses,
   otherwise `409`. It uses the same has-the-column test as `AXC0178`, see
   [Migrations](migrations.md#migrations). That check counts only `running` and
   `waiting` instances, so a `failed` instance never blocks a destructive
   migration. Without this check, the retry would run old steps against a
   changed table. Only migrations applied since the pinned release count:
   those of the application whose file the pinned release does not hold. The
   release was activated after it applied every migration it holds, and its
   model already reflects them. Activation stores the columns each migration
   changed in `axis.applied_migrations`, see
   [Applied migrations](migrations.md#applied-migrations), and the retry reads
   them back. A row stored before the columns were recorded counts as having
   changed every column. A pinned release whose stored content is missing
   counts as using every column, so any applied destructive migration of the
   application refuses the retry. A refused instance stays `failed`. The
   operator starts a new instance for the record.
5. It sets the instance to `running` on the same step, with the next revision,
   and clears `ended_at`.
6. It inserts a `process.step` work item for the step.
7. It writes a history row for the step, at the revision the instance had when
   it failed. Its output is `{ "command": "retry" }`.
8. It writes the audit record `process.retried` for the subject record, or with
   no record for an instance without a subject, with
   details `{ "step", "reason" }`. The actor is the user with their display
   name.

The worker then runs the step as usual, see [Running a step](process-execution.md#running-a-step).
It reads the subject record as it is now. A step that failed because of the
record's data can succeed after the record is fixed.

A `callConnector` step or a package operation step whose
call row is `failed` stages the call again under the **same operation identity**, see
[Staging and the call](process-execution.md#staging-and-the-call). The input is evaluated again
on purpose, so the retry sends the current data. A failed connector call keeps
its stored input, but the retry still evaluates it again. A failed package
call's stored input is cleared when it settles. The attempts count from zero,
so the step's full retry policy applies again. The attempt rows carry on
numbering after the earlier run, see [Attempts](#attempts).

**Risks.** A retry of a connector call sends it again under the same
idempotency key. Some systems replay their stored failure for a key they have
seen, so the retry can fail the same way. The widget warns before a retry only
when the failed call is a package call whose operation is not
`idempotent: true`. Such an operation may have acted before it failed, and a
retry runs it again. A connector call needs no warning, because every unknown
outcome of one was sent again under the idempotency key or
[reconciled](process-execution.md#reconciliation), or was [resolved](#resolve) by an operator.

### Cancel

Cancel takes an instance that is `running` or `waiting`. It does not undo
steps that already ran, because there is no compensation yet
([D25](../decisions/d25-connectors-deployment-bindings-and-secrets.md)).
Before it locks anything, it answers the policy `409` when a `read` filter of
the user reads a pending field, see
[Operator command errors](#operator-command-errors).

One transaction does this:

1. When the instance is a sub-process, it locks the parent's row first. A
   cancel of a child and a cancel of its parent then meet on the parent, so
   the lock order is always parent before child.
2. It locks the instance's `open` tasks, then the instance, then its call
   rows. A task completion locks the task and then the instance, so locking
   the instance first would deadlock with a manager who approves at the moment
   an operator cancels. A task that was created while the cancel waited for the
   instance lock is closed too.
3. It requires the instance to be `running` or `waiting`, otherwise `409`.
4. It repeats step 2 for each `running` or `waiting` child, depth first,
   before it writes anything.
5. It requires that no call of the instance or of any child is `sending` or
   `unknown`, otherwise `409`. The problem names the call. The system may have
   acted, so the call must settle or be [resolved](#resolve) first. Cancel
   never forgets an external effect.
6. It sets the instance and each child to `cancelled`, with the next revision
   and `ended_at`.
7. It sets their `open` tasks to `cancelled`, and their `pending` calls to
   `cancelled` with their stored input cleared.
8. It deletes their work items.
9. It writes a history row for the step of each, at the revision it had. Its
   output is `{ "command": "cancel" }`.
10. It writes the audit record `process.cancelled` for the subject record of
    each, or with no record for an instance without a subject, with details `{ "step", "reason", "taskIds", "operationIds" }`. The
    two lists name the tasks and calls it cancelled. The actor is the user and
    the reason is the same for every instance.

**Failing a waiting parent.** When the cancelled instance is a child and its
parent is `waiting` on the sub-process step for this child, the cancel also
fails the parent's step. The parent becomes `failed` with `ended_at`, and its
history row has the error `The sub-process of the step '{step}' was cancelled.`
and the output `{ "childInstanceId" }`. The audit record `process.failed` is
written with the actor `system` and details `{ "step" }`. A parent that is
itself being cancelled is not `waiting`, so it ends `cancelled`. A child that
is already `failed` stays as it is when its parent is cancelled.

A `cancelled` instance does not block a new start for the same record. Its
cancelled tasks leave every inbox, and a completion of one is `409`, see
[Task API errors](task-api.md#task-api-errors).

**Races.** A worker that runs a step when the cancel commits cannot commit
after it, because the step changes the instance only at the revision it loaded,
see [Running a step](process-execution.md#running-a-step). A worker that marks a call `sending`
either commits before the cancel, so the cancel finds the call `sending` and
answers `409`, or finds its work item deleted and sends nothing.

Cancel is also the way to drain old-release work before a destructive
migration, see [Migrations](migrations.md#limits). An `unknown` call must be
resolved first.

### Resolve

Resolve takes a call that is `unknown`, on an instance that is `waiting` on the
call's step. The operator has checked what the other system did, and says so.
It works for a connector call with no lookup and for a package call, which has
none. It also works for a connector call that waits for its lookup, in place of
the lookup.

One transaction does this:

1. It takes the [start lock](processes.md#releases-and-running-work) as its first
   statement, with the same rerun and `503` as a retry. Output fields can set
   off update triggers, so resolve follows the rule of a task completion.
2. It locks the instance and the call. It requires the call to be `unknown` and
   the instance to be `waiting` on its step, otherwise `409`. A lookup or a
   worker that settled the call first makes this a `409`.
3. For `succeeded`, it checks `output` against the operation's declared
   outputs, as a response body would be checked, see
   [Call outcomes](process-execution.md#call-outcomes). That is the connector operation's outputs,
   or the manifest's for a package call. Then it writes the step's output
   fields to the subject record with the record update rules, as a successful
   outcome does. An `output` that does not fit, or a write that the record
   refuses, is a `400`. Nothing changes and the call stays `unknown`.
   An instance without a subject has no record, so a `succeeded` resolve only
   checks `output` and writes no record. Its step maps no outputs.
   - It sets the call to `succeeded`. A package call clears its stored input,
     and a connector call keeps it, see [Call input kept](#call-input-kept).
   - It sets the instance to `running` on `next`, with the next revision.
   - It writes the history row of the step, at the revision the instance
     waited at. Its output is
     `{ "operationId", "outcome", "next", "resolved": true }`, plus
     `subjectVersion` when it wrote output fields.
   - It writes `record.updated` when it wrote output fields, and inserts the
     work item of the next step.
4. For `failed`, it sets the call to `failed`. A package call clears its
   stored input, and a connector call keeps it, see
   [Call input kept](#call-input-kept). It
   sets the instance to `failed` with `ended_at`, and writes a history row
   with output `{ "operationId", "outcome": "failed", "resolved": true }` and
   a fixed error sentence. It writes `process.failed` with details
   `{ "step" }`. The actor is the user.
5. For both outcomes, it deletes the call's `connector.call` or `package.call`
   work item, because a lookup may still be due.
6. For both outcomes, it writes `connector.callResolved` or
   `operation.callResolved` instead of `connector.called` or `operation.called`.
   Its details are `{ "step", "connector", "operation", "operationId", "outcome", "reason" }`
   for a connector call, and
   `{ "step", "package", "operation", "operationId", "outcome", "reason" }` for
   a package call. The actor is the user. So each settled call has one audit
   record, and it shows who settled it by hand.

A resolved call keeps its last HTTP status and last error from its final
attempt. Only its state and its settled time change, and a package call also
clears its stored input. A resolve records no
instance-ended metric, as a cancel does not.

A resolve with a mistake in `output` is a `400` and does not fail the
instance. The operator can still resolve the call as `failed` on purpose. A
later [retry](#retry) of that instance stages the call again under the same
operation identity.

When the operator resolves a call as `failed` and the other system had in fact
acted, a retry can create a second effect. Axis trusts the operator's judgement
there.

### Operator command errors

Errors are problem details, as in the [record API](record-api.md#errors). The
titles are fixed and never contain text from the request. The shape of the
answers matches the [start endpoint](processes.md#start-endpoint).

A request is checked in this order, and the first failure is the response:

1. The CSRF header, `403`, for a session request only. A bearer token needs none.
2. The session or the bearer token, `401`.
3. The application and the process, `404`.
4. The matching grant on the process, `403`: `retry`, `cancel` or `resolve`.
5. The content type, `415`.
6. The body, `400`. For a resolve this is the body's shape: `reason`, `outcome` and
   `output` as an object. Each output value is checked later, in the
   transaction, because the declared outputs come from the call's pinned
   release. A bad `reason` and a bad output value are reported one after the
   other, not together.

One transaction then checks the instance, and for a resolve the call. A cancel
or a retry first answers the policy `409` below, before it looks for the
instance. It answers `404` for the instance, the subject record and the call,
and then the `409` checks.

- **`401`.** Nobody is signed in. The title is the record API's "Sign in to
  read or write this data.". A bad bearer token gets `WWW-Authenticate: Bearer`
  and its own title, as in [process inspection](#process-inspection).
- **`403`.** The user has no grant for the command on the process. The title
  is the record API's "You have no grant for this action.". The CSRF header
  failure is a `403` too.
- **`404`.** An unknown application, an application with no active release, or
  an unknown process. In the transaction, also:
  - an unknown instance, an `{id}` that is not a UUID in the hyphenated form,
    or an instance of another process. The cancel's title is "No instance of
    this process exists with this id.";
  - an instance whose subject record the user may not read;
  - for a resolve, an unknown `{callId}`, a `{callId}` that is not a UUID in the
    hyphenated form, or a call of another instance. The title is "No call of this
    instance exists with this id.".
- **`400`.** The body is a validation problem keyed by JSON Pointer. Every
  invalid part is reported in the same response.
  - A missing, blank or too long `reason` is keyed `/reason`.
  - A missing `outcome`, or one that is not `succeeded` or `failed`, is keyed
    `/outcome`.
  - An `output` with `failed`, or an `output` that is not an object, is keyed
    `/output`.
  - An output that is not declared, or has a value of the wrong type, is keyed
    `/output/{name}`. So is a required output that is missing, and an output
    whose field write the record refuses, with the record API's message.
  - Any other property, a repeated property and a body that is not a JSON
    object get the record API's keys and messages, such as "Unknown
    property.".
- **`409` for a policy that reads a pending field.** Cancel, retry and resolve. A
  `read` filter of the user's grants on the subject entity reads a
  [pending field](migrations.md#recomputing-computed-fields). The title is the
  record API's "Your access to this entity cannot be checked while a field is
  being recomputed.", with no `errors`. Nothing is written.
- **`409`.** The state does not allow the command. Nothing is written.

  | Command | Cause |
  | --- | --- |
  | Retry | The instance is not `failed`. The title is "The instance is not failed.". |
  | Retry | Another instance of the process is `running` or `waiting` for the record. A start that commits first on the unique index gives the same answer. The title is the start endpoint's: "A running or waiting instance of this process already exists for this record.". |
  | Retry | Another instance of a process without a subject is `running` or `waiting`. The title is "A running or waiting instance of this process already exists.". |
  | Retry | An applied migration renamed, dropped or converted a column that the pinned release uses. The title is "A migration changed a column that the instance's release uses.". |
  | Cancel | The instance is not `running` or `waiting`. The title is "The instance is not running or waiting.". |
  | Cancel | A call of the instance is `sending` or `unknown`. The problem names the call in a `callId` member. The title is "A call of this instance may have reached the other system. It must settle or be resolved first.". |
  | Resolve | The call is not `unknown`. The title is "The call's outcome is not unknown.". |
  | Resolve | The instance is not `waiting` on the call's step. The title is "The instance is not waiting on the call's step.". |

- **`503`.** Retry and resolve only. The application's active release changed
  on each of three attempts, found under the
  [start lock](processes.md#releases-and-running-work). The server already ran the command
  again on the new release each time. Nothing was written, and the client may
  send the request again. The title is the start endpoint's: "The
  application's active release changed while the request ran. Try again.".

### Walkthrough: an unknown purchase order

Suppose the `PurchasingSystem` connector declares neither `idempotency` nor a
`lookup`. The request still ends with exactly one purchase order.

1. The finance reviewer approves request `PR-2026-00042`. `markApproved` runs,
   `announceApproved` raises `PurchaseRequestApproved`, and it queues
   `createPurchaseOrder`.
2. The step stages call C as `pending` and sets the instance `waiting`. Worker
   A marks C `sending` and sends `POST /purchase-orders`.
3. The purchasing system creates `PO-2026-0001`. Its response is lost, and the
   request times out.
4. A timeout is an unknown outcome. The connector declares no `idempotency`, so
   C is not sent again. It declares no `lookup`, so nothing can ask the system.
   C becomes `unknown`, its work item is deleted, and the instance stays
   `waiting`. A history row with output `{ "operationId", "outcome": "unknown" }`
   makes it visible. A second start for the request is blocked.
5. An operator who holds a `resolve` grant on the process checks the purchasing
   system. It holds `PO-2026-0001` for `PR-2026-00042`.
6. The operator posts a resolve for C with a reason, `outcome` `succeeded` and
   `output` `{ "orderNumber": "PO-2026-0001" }`.
7. One transaction takes the start lock and checks that C is `unknown` and the
   instance is `waiting` on `createPurchaseOrder`. It checks the output
   against the operation's declared output. It writes
   `purchaseOrderNumber = PO-2026-0001` and `record.updated`, and sets C to
   `succeeded`. The instance becomes `running` on `assignPaymentReference`. It
   writes the history row, and one `connector.callResolved` with the operator
   as its actor and their reason. It queues the next step.
8. The worker runs `assignPaymentReference` and then `done`, which completes
   the instance.

The purchasing system holds one order, and the request records its number. No
`connector.called` was written, because C settled by hand, and the audit trail
shows who did it.

- **Resolved as failed, then retried.** Had the operator found no order, they
  would resolve C with `outcome` `failed`. C becomes `failed` and the instance
  becomes `failed` on `createPurchaseOrder`, with `process.failed` written. The
  operator can then post a retry. The instance goes back to `running` on the
  same step, which stages C again under the same operation identity with fresh
  input and 0 attempts. Without `idempotency`, a wrong "no order" finding means
  a second order, so the operator must check first.
