# Process inspection

This page covers process inspection, where an authorized user sees where an instance is, what it did
and why it failed or waits. The [process operator commands](process-operator-commands.md) page covers
the commands that act on the work. The [process inspector widget](process-inspector-widget.md) shows
the same data, and the [operator console](operator-console.md) shows it to the platform operator.

## Process inspection

Two routes let an authorized user see where an instance is, what it did and why it failed or waits:
the [instance list](#instance-list) lists the instances of a process and the
[instance detail](#instance-detail) reads one of them. The user is the user of the
[session](security.md#sign-in-and-the-session), or a
[service account](security.md#service-accounts) with a bearer token. A bad token is a `401` with
`WWW-Authenticate: Bearer`. Both routes only read, and they write no audit record.

| Route                                                    | What it does                                                          |
| -------------------------------------------------------- | --------------------------------------------------------------------- |
| `GET /api/apps/{app}/processes/{process}/instances`      | Lists the instances of a process, see [instance list](#instance-list) |
| `GET /api/apps/{app}/processes/{process}/instances/{id}` | Reads one instance, see [instance detail](#instance-detail)           |

### Who may inspect

A `process` grant takes the action `inspect`, next to `start`, as
[roles and policies](roles-and-policies.md) say. The grant decides who may inspect, and the
signed-in user needs nothing else. A user who holds only `start` cannot inspect, and a user who holds
only `inspect` cannot start.

### Order of checks

A request is checked in this order, and the first failure is the response:

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

A [process without a subject](process-flow.md#processes-without-a-subject) has no subject record.
Step 5 and the subject record part of step 6 do not apply to it, so the `inspect` grant alone is
enough.

### Instance list

`GET /api/apps/{app}/processes/{process}/instances?state=&subject=&page=&pageSize=&sort=&search=&filter=`
lists the instances of the process, whatever release they are pinned to.

- `page` and `pageSize` follow the record API's
  [paging rules](record-api.md#paging-and-sorting).
- `sort`, `search` and `filter` follow the same rules and PostgreSQL applies
  them. The fields are `subjectLabel` (text), `state` (one of the five states),
  `step` (text), `releaseNumber` (number), and `createdAt` and `endedAt`
  (dates). A `search` looks in `subjectLabel` and `step`. The two time fields
  filter by their UTC day and sort by their instant. A sort or filter on any
  other field is a `400`, keyed `sort` or `filter`.
- `subjectLabel` is a field only for a process with a subject whose display
  field the user's `read` grant covers and the active release does not mark
  sensitive. Otherwise a sort or filter on it is a `400` and a `search` never
  matches it. The label that the filter and search read is the record's
  current value, as the item shows it.
- `state` is optional and may repeat. Each value is one of `running`, `waiting`,
  `completed`, `failed` and `cancelled`, and the list keeps the instances in any
  of the given states. Without `state`, every state is listed. Any other or
  empty value is a `400` keyed `state`.
- `subject` is optional. It is a record id in the hyphenated UUID form.
  Anything else is a `400` keyed `subject`.
- Without a `sort`, instances are ordered by `created_at` descending, then by
  `id` descending. With a `sort`, they are ordered by it, then by `created_at`
  descending and `id` descending.
- `totalCount` counts only the instances that match `filter` and `search`, and
  leaves out those the read filters hide.
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
  [process inspector widget](process-inspector-widget.md) shows no identifier.
  `subjectLabel` is the value of the subject record's display field, such as
  `"Replace the worn carpet tiles"`. It is `null` in these cases: the process
  has no subject, the entity has no display field, the user's field grants on
  the subject entity do not cover the display field, or the display field is
  [sensitive](roles-and-policies.md#sensitive-fields) in the active release or in the release the
  instance is pinned to. `releaseNumber` is the number of the release the
  instance is pinned to in the application's
  [release history](publish-request-storage.md#release-history), such as `3`, a JSON number, so
  the widget writes no API text of its own. It is `null` only for a release
  with no row in the release history. Both come from the one query that reads the page.

### Instance detail

`GET /api/apps/{app}/processes/{process}/instances/{id}` returns one instance
in any state. The body has these parts:

- `id`, `process`, `subjectId`, `releaseId`, `state`, `step`, `createdAt` and
  `endedAt`, as in the list. `process` is the process name. `subjectId` is
  `null` for an instance of a process without a subject.
- `timerDueAt` is the due time of the instance's `process.timer` work item while
  the instance is `waiting` on a timer step, or on a `waitEvent` step with a `timeout`, as a UTC time
  like `createdAt`. It
  reads only the item of the instance's current revision, so a stale item from
  an earlier pass of a loop is not shown. It is `null` in every other case. It
  is the same for every user who may inspect the instance.
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
  - `type` is the step type: `decision`, `task`, `operation`, `timer`, `waitEvent` or `end`.
  - `labelKey` is the step's label text key, or `null` when it has none.
  - `transitions` is a list of `{ "next", "on" }`, one for each step the step
    can lead to. `on` is always text or `null`. It is the zero-based index of
    a decision branch as text, such as `"0"`, as in the history's `decision`,
    then `"otherwise"` for the fallback branch, the name of a task outcome, or
    `null` for the `next` of an operation, a `subProcess`, a timer or a `waitEvent`. A `waitEvent`
    step's timeout transition has `"timeout"`. An `end` step has none.
  - A `callConnector`, package, `raiseEvent` and `subProcess` step have type `operation`.
  - **Blocks.** _(planned for M38)_ The steps inside a block follow the block in list order, after it. Each step
    has `block`, the name of its block step, and `branch`, the name of its branch, or `null` in a repeat. A block's
    `type` is `parallel` or `repeat`, and its transition is `{ "next", "on": null }`. Each `onError` entry adds a
    transition `{ "next", "on": "error:<kind>" }`, such as `error:call`. A top-level step has `block` and `branch`
    set to `null`.
- `path` is the history rows of the instance, ordered by `finished_at`, then
  by `id`. Each is
  `{ "step", "startedAt", "finishedAt", "decision", "output", "error", "traceId" }`.
  - `decision` and `output` are the stored values, see
    [Tables](process-tables.md#tables). `output` holds no field values.
  - `error` is the stored error, see
    [Step errors](#step-errors-in-inspection).
  - `traceId` is the trace id of the run that wrote the row, see [Attempts](#attempts).
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
    `{ "kind", "number", "startedAt", "finishedAt", "outcome", "httpStatus", "error", "traceId" }`,
    with `kind` either `attempt` or `lookup`. `traceId` follows [Attempts](#attempts).
  - `input` and `inputHidden` follow [Values and masking](#values-and-masking).
  - **Output and response.** _(planned)_ A call has no output of its own. The engine writes the outputs
    of a call straight to the subject record. Each attempt's `response` is kept, see [Attempts](#attempts).
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
- `commands` lists the [operator commands](process-operator-commands.md#operator-commands) the user may
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
- **Process title.** _(planned)_ `processTitleKey` is the text key of the process's `title` on the
  detail, on `parent` and on each `subProcesses` entry. It is `null` where the release gives no title.
- **Record and release.** _(planned)_ The detail has `subjectEntity`, the name of the process's subject entity, or
  `null` for a process with no subject. Its `subjectLabel` and `releaseNumber` are as the
  [instance list](#instance-list) defines them. With `subjectId`, `subjectEntity` addresses the subject record, see
  [routing](frontend.md#routing).
- **Labels.** _(planned)_ Each `graph` step's `labelKey` is the key of its `label`, or `null`. Each
  transition's `labelKey` is the key of its outcome's or branch's `label`, or `null`. A `waitEvent` step
  has `event`, the event's name, and `eventLabelKey`, the key of the event's `label`, or `null`.
- **Who started it.** _(planned)_ `startedBy` is `{ "kind", "name", "id", "via" }`. `kind` is `person`,
  `trigger`, `endpoint`, `schedule` or `parent`. `name` is the person's name, the resource's name for a
  trigger, endpoint or schedule, or the parent process's name for `parent`. `id` is the user's id, or the
  parent instance's id, and `null` otherwise. `via` is the text key of the action's label for a person,
  and `null` otherwise.
- **Input and result.** _(planned)_ `input` holds the parameters the instance started with, or `null`,
  and follows [values and masking](#values-and-masking). `result` is `{ "step", "values" }` once the
  instance completed, and `null` before. Its `values` follow the same rule.
- **Studio.** _(planned)_ `studio` is `{ "app", "resource" }`, from which the SPA builds Studio's address
  for the process. It is `null` for a reader who may not author the application.
- **Tasks.** _(planned)_ Each task has `dueAt`, the time it is due as UTC like `createdAt`, or `null`
  when its step has no `dueIn`. It has `assigneeId`, the id of a user assignee or of the person who
  claimed it, else `null`, and `completedById`, the id of the person who completed it, else `null`.
  `assignee` and `completedBy` hold a person's name, and a role assignee keeps its role's name.
- **Runs.** _(planned)_ Each `path` entry has `read`, the values that a decision's conditions read, as
  `[{ "name", "value", "hidden" }]`, and `made`, a `RunOutput`:
  `{ "changes": [{ "field", "before", "after", "hidden" }], "event": { "name", "fields" }, "notified": { "to", "channels", "subject" }, "created": { "entity", "id" } }`.
  Each part of `made` is `null` when the run did not make it.
- **Calls.** _(planned)_ A connector call has `request`, `{ "method", "url", "query", "headers", "body", "truncated" }`,
  and each of its `attempts` has `response`, `{ "status", "headers", "body", "truncated" }`. A package
  call has `returned`, the values its operation returned. `request`, `response` and `returned` follow the
  hiding rule of `input`.
- **Errors.** _(planned)_ `problem` is `{ "code", "values" }` on a path entry and on an attempt, and
  `null` with no error. Its codes are in [step errors](#step-errors-in-inspection).
- **Paths.** _(planned for M38)_ `revision` is the revision of the root path, which is the instance
  itself. `paths` lists the paths that a block started, ordered by `createdAt`, then by `id`. Each is
  `{ "id", "parentPathId", "block", "branch", "pass", "state", "step", "revision", "createdAt", "endedAt", "commands" }`.
  - `parentPathId` is the path that reached the block that started this path, and `null` when the root
    path did. `block` is the name of that block's step.
  - `branch` is the name of a `parallel` branch, and `pass` is the number of a `repeat` pass. Each is
    `null` for the other kind of path.
  - `state`, `step` and `revision` are the path's own, see [path states](process-states.md#path-states)
    and [paths](process-execution.md#paths). `item` and `gathered` are left out, because they can hold
    field values. The root path has no entry.
- **Path, task and call rows.** _(planned for M38)_ Each `path` row, task and call has `pathId` and
  `pass`. They name the path that the row belongs to and the pass of a repeat it ran on. Both are `null`
  for the root path.
- **Commands of paths and tasks.** _(planned for M38)_ A path's `commands` holds `cancel` when the path is
  `running` or `waiting`, the user holds the `cancel` grant, and no call of the path, of a path inside it
  or of their child instances is `sending` or `unknown`. A task's `commands` holds `cancel` when the task
  is `open`, it has a group, the cancel would not be refused, and the user holds the `cancel` grant. See
  [cancel one path or one task](process-operator-commands.md#cancel-one-path-or-one-task).
- **Variables.** _(planned for M38)_ `variables` lists the instance's variables in the order that the
  pinned release declares them. Each is `{ "name", "type", "list", "value", "hidden" }`, and its `value`
  follows [values and masking](#values-and-masking).

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
      "error": null,
      "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
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
          "error": null,
          "traceId": "0af7651916cd43dd8448eb211c80319c"
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
same for every user who may inspect the instance. A call's `input`,
`commands`, `parent` and `subProcesses`, and each call's `resolvable` and `outputs` differ between users.

**Values by user.** _(planned)_ `studio`, the values that the masking rule hides, and a call's `request`,
`response` and `returned` also differ between users.

**Paths and variables.** _(planned for M38)_ The `variables`, and the `commands` of each path and task,
differ between users too. The rest of `paths` and `tasks` is the same for every user.

### Values and masking

Inspection shows a stored field value only where this section says it does. The history's
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
  [tasks](task-api.md).
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
- **Kept values.** _(planned)_ A call's `request`, each attempt's `response` and a package call's `returned`
  follow the hiding rule of the call's `input`. So do a run's `read` and `made`, and the instance's `input`
  and `result`, each over the fields it came from. A hidden `read` or `made` value is `null` with `hidden`
  `true`. No kept value is copied to history, audit records or logs.
- **Variables.** _(planned for M38)_ A variable's `value` is shown only when the user may read every field
  that each expression setting the variable reads. Those expressions are the `set` entries of each
  `setVariables` step that sets it, and the `gather.value` of each repeat that gathers into it, both from
  the pinned release. The rules above apply to them, and the rule fails closed, as a call's input does.
  - The rule follows the expressions through the other variables that they read, as it follows named
    rules above. A pass item counts the fields that an expression reaches through it.
  - An expression that reads a data source row hides the value. So does an instance without a subject,
    and a sensitive field in either release, as for a call's input.
  - A hidden value is `null` with `hidden` `true`. `hidden` is `false` otherwise.
  - A value is never copied to history, audit records or logs, see
    [process instances](process-tables.md#process-instances).

The [operator console](operator-console.md#api) shows the same detail with the same rules, with the
operator as the reader. The operator reads every field that the active release has, so only a
sensitive field, or a field the active release no longer has, hides an `input`.

### Call input kept

A connector call keeps its input after it settles, whether it succeeded or
failed. The kept input lets inspection show what was sent. It is never copied
to history, audit records or logs, and inspection
shows it only under the [masking rule](#values-and-masking). A package call's input is cleared when
the call settles, see [Staging a package call](process-execution.md#staging-a-package-call).

**Request and responses.** _(planned)_ A connector call also keeps its request and each attempt's
response. The body is kept as text, unchanged, up to a size limit. A longer body is cut and marked
`truncated`. Every secret is kept as `[secret]`. They are kept for as long as the instance's history,
never copied to history, audit records or logs, and shown only under the [masking rule](#values-and-masking).

### Attempts

`axis.process_call_attempts` holds one row per attempt and per lookup of a
connector or package call, see [Tables](process-tables.md#tables). The call row counts its
attempts and keeps only the last status and error, so the attempt rows hold the
earlier ones.

- **Trace ids.** Each `path` entry and each attempt carries the `traceId` of the
  run that wrote it, from its stored `trace_id`, and the process inspector shows it, so an operator can
  find the run's logs and spans, see [telemetry](telemetry.md). `traceId` is `null` for a row that
  stores no `trace_id`.

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
  [retry](process-operator-commands.md#retry) the call's `attempts` count from 0 again, while the numbers
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
  error text. It never holds an exception message or a secret's value.
- **Response and problem.** _(planned)_ A row also holds the attempt's `response`, see
  [Call input kept](#call-input-kept), and its `problem`, see [Step errors](#step-errors-in-inspection).

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
  A write that a module contract refused ends with the named point, such as
  `(named point Request/transitions/submit)`.
- For a connector call that cannot be sent or whose success cannot be
  recorded, a fixed text that ends with the exception type. The same text is
  the call's `last_error`.

None of them holds a value, and inspection shows the error as it is stored.

**Problem codes.** _(planned)_ A path entry and an attempt carry `problem`, a code with its values. The
page words it through `shell.stepProblem.<code>` in the reader's language. A row with no `problem` shows
its stored `error`. The codes are:

- `unavailable`: the outside system could not be reached. The stored error is the attempt's fixed text.
- `serverError`: the outside system answered with a server error. The stored error is the attempt's fixed text.
- `refused`: the outside system refused the call. The stored error is the attempt's fixed text.
- `timeout`: the outside system did not answer in time, with `seconds`, the wait that ran out. The stored error is the attempt's fixed text, such as a timeout.
- `notSent`: a connector call could not be sent, or its success could not be recorded. The stored error ends with the exception type.
- `exception`: any other failure. The stored error is the exception type.
- `expressionFailed`: an expression of the step failed. The stored error is the fixed text that names the step, the part and the kind of expression error.
- `writeRefused`: a write to a record was refused. The stored error is the refused-write text.

**No stack traces.** _(planned)_ No page of an application shows a stack trace or an exception message,
as [D161](../decisions/d161-connector-calls-run-on-bindings-and-secrets-and-process-inspection-keeps-what-a-run-sent-and-answered-with-step-errors-as-codes.md) says.

### Inspection errors

Errors are problem details, as in the [record API](record-api.md#errors). The
titles are fixed and never contain text from the request.

- **`401`.** Nobody is signed in.
- **`404`.** An unknown application, process or instance, as the order of
  checks says.
- **`403`.** The user has no `inspect` grant on the process.
- **`400`.** A list parameter is invalid: `page`, `pageSize`, `state`,
  `subject`, `sort`, `search` or `filter`, keyed by its name. The detail has
  none.
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
