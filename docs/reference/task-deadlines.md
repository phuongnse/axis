# Task deadlines and claims

Detailed reference for task escalations and deadlines, overdue tasks, and the
routes that claim, release and reassign a task. The task step is in
[processes.md](processes.md#steps), and the task API in
[task-api.md](task-api.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows
[D71](../decisions/d71-task-deadlines-escalation-claim-and-reassign.md). The
`deadline`, the `expired` task state, the `expired_at` column and the
`task.deadline` work item are built. [Claim and release](#claim-and-release) and
the `claimed_by` and `claimed_at` columns are built, and so are release by a
`reassign` grant and [reassign](#reassign) with its user picker. The
[overdue](#task-states) rule and the [inbox and task page](#inbox-and-task-page)
are built: the inbox marks, and Claim, Release and the Reassign dialog on the
task page. Escalations are _(planned for M14)_.

## Escalations and deadline

A task step may add `escalations` _(planned for M14)_ and a `deadline`:

```json
{
  "name": "resolve",
  "type": "task",
  "label": { "textKey": "ticket.resolve" },
  "assignee": { "user": "assignedAgent" },
  "form": "TicketResolve",
  "dueIn": "P1D",
  "escalations": [
    {
      "after": "P1D",
      "notify": {
        "to": [{ "role": "service-desk-lead" }],
        "channels": ["inApp"],
        "message": { "subject": { "textKey": "ticket.late.subject" }, "body": { "textKey": "ticket.late.body" } }
      }
    },
    { "after": "P2D", "reassign": { "role": "service-desk-lead" } }
  ],
  "deadline": { "after": "P5D", "next": "markExpired" },
  "outcomes": [{ "name": "resolved", "label": { "textKey": "task.resolved" }, "next": "confirm" }]
}
```

- **Durations.** Every `after` is a positive fixed-length duration, by the rule
  of `dueIn` (`AXC0084`, at `/steps/{i}/deadline/after` for the deadline). It
  counts from the time of the task step's transaction, the same time `dueIn`
  counts from and the task's `created_at`. In a [test scenario](test-scenarios.md)
  that time follows the scenario's `now`. There are no business hours or
  calendars.
- **Escalation** _(planned for M14)_. Each has `after` and at least one of `reassign` and
  `notify`. `reassign` has the shape of `assignee`. `notify` has the shape of
  the [`notify` operation](notifications.md#notify-operation), without `next`.
  An escalation does not end the task.
- **Deadline.** `after` and `next`. `next` names a step of the process
  (`AXC0070` when it names none). It counts as a transition of the step graph,
  checked after the outcomes, so a step that only a deadline leads to is
  reachable. _(Planned for M14)_ Each
  escalation's `after` is shorter than the deadline's, otherwise a new
  diagnostic at that escalation's `/after`.
- **Work items.** The task step's transaction queues one `task.deadline` item,
  with the task id and the instance id, due at the step's time plus the
  deadline's `after`. _(Planned for M14)_ It also queues one `task.escalate`
  item per escalation, with the task id and the escalation's index, due at the
  step's time plus its `after`.
- **Escalation runs** _(planned for M14)_. The item locks the task row. When the task is still
  `open`, it applies `reassign` as a [reassign](#reassign) by `system`, and
  stages `notify` as the operation does. The step history gets a row with
  `{ "escalation": <index> }`.
- **Deadline runs.** The item locks the task row. When the task is still
  `open`, the task becomes `expired` with `expired_at` set, the instance
  resumes at `next`, and the history row's `decision` is `deadline`. The audit
  record is `task.expired`, by `system`. A task that is no longer `open` is
  left alone. If the handler fails, it logs the error type and does not fail
  the instance, since it can fail only when the stored data is inconsistent.
- **One winner.** Completion and the deadline lock the task row first, and a
  cancel closes the instance's open tasks in its own transaction, so an open
  task ends exactly one way. A task that is no longer `open` makes the item do
  nothing. Completing a task deletes its pending `task.deadline` item, and a
  cancel deletes every work item of the instance. _(Planned for M14)_ Cancel,
  an escalation and a reassign follow the same rule, and the deadline's expiry
  deletes the pending `task.escalate` items.
- **Release.** The deadline comes from the task's pinned release. Role checks
  use the active release, as today.
- **Test scenarios.** `advanceTime` runs due `task.deadline` items. A scenario
  action never runs one early, see [Test scenarios](test-scenarios.md).

## Task states

The task states of [processes.md](processes.md#task-states) gain `expired`: the
deadline passed before a decision, and the instance moved on. An expired task
leaves every inbox and cannot be completed. The task API answers a completion
of it with the `409` it gives for a completed task. An expired task reads with
`state` `expired`. The API does not return `expired_at`.

**Overdue.** A task is overdue when it is `open` and its `due_at` is before the
database's current time. The task API's list and read add
`"overdue": true|false`, worked out when read with database time. Nothing is
stored. A completed, cancelled or expired task is never overdue.

## Claim and release

```http
POST /api/apps/{app}/tasks/{id}/claim
POST /api/apps/{app}/tasks/{id}/release
```

- **Claim.** A user who holds the task's role may claim an open role task.
  The task stores `claimed_by` and `claimed_at`. Then only the claimer may
  complete it, and it leaves the lists of the other role holders.
- **Release.** The claimer may release a claim. A user with the `reassign`
  grant on the process may release anyone's claim, with no role on the task.
  The task goes back to every holder of the role.
- **Response.** Both routes answer `200` with the task in the shape of a
  single read of the [task API](task-api.md).
- **Errors.** The checks run in the task API's order: `401`, `404`, `403`, then
  `409`. A user who may not act on the task gets the `403` of the task API, and
  so does a role holder without the `reassign` grant who releases another
  holder's claim. An unknown task is
  a `404`. A task that is no longer open is the task API's `409`. Three more
  cases are a `409` with a fixed title:
  - Claiming a task assigned to a user: "Only a task assigned to a role can be
    claimed."
  - Claiming a task that is already claimed: "This task is already claimed."
  - Releasing a task that is not claimed: "This task is not claimed."
- **Reading.** Other role holders may still read a claimed task and ask for its
  form state. Only completing it, and listing it, is limited to the claimer.
- **Audit.** `task.claimed` and `task.released`, with the actor, the task id,
  the subject record and the instance.
- The list and read add `"claimedBy"`, the claimer's user id, or `null`.

## Reassign

```http
POST /api/apps/{app}/tasks/{id}/reassign
Content-Type: application/json

{ "role": "service-desk-lead", "reason": "Agent on leave." }
```

- The body has exactly one of `user`, a user id, and `role`, a role name, and
  a `reason` of at most 500 characters that is not blank. A body that has
  neither or both of `user` and `role` is one problem on the whole body, keyed
  `""`. A value that is not text or is blank is `Required.` at its pointer.
- **Who may act.** A user with the `reassign` action on a grant of the task's
  process in the active release. The assignee may not reassign without it. A
  `reassign` grant needs no role on the task. A process the active release no
  longer has has no holder. Without the grant the answer is the `403` titled
  "You have no grant for this action.", whoever the assignee is.
- **Response.** `200` with the task in the shape of a single read of the
  [task API](task-api.md), as claim and release answer. The fields shown are
  still limited by the actor's `read` grants.
- **Errors.** The checks run in the task API's order: `401`, `404`, `403` for
  the grant, `409` for a task that is not open, `415`, `400` for the body, then
  `400` for the target. The route takes no start lock. It locks the task row.
- **Target.** A `role` must be declared by the active release. A `user` must
  have a [user profile](notifications.md#user-profiles), so a task never goes
  to an id that nobody has signed in with. Otherwise it is a `400` keyed
  `/role` or `/user`.
- **Effect.** The task's assignee kind and assignee change, and a claim is
  cleared. The assignee check runs again at the moment of each later action.
  The deadline does not change, so the new assignee inherits `due_at` and the
  pending `task.deadline` work item.
- **User picker.** `GET /api/apps/{app}/users?search=` lists up to 20 user
  profiles whose display name contains the search text, ignoring case, sorted
  by display name and then id. An empty or missing search lists the first 20. It
  answers `{ "items": [{ "id", "displayName" }] }` and nothing else of a
  profile, and it is the only route that lists profiles. It needs a `reassign`
  grant on some process of the application, otherwise `403`. Only people who
  have signed in have a profile, so a new colleague can receive a task after
  one sign-in.
- **Audit.** `task.reassigned`, with the actor, the task id, the subject record
  and the instance. Its details are `{ "taskId", "from", "to", "reason" }`.
  `from` and `to` have the shape of `assignee`: `{ "role": name }` or
  `{ "user": id }`. An escalation will write it with the actor `system`
  _(planned for M14)_.

## Tables

`axis.process_tasks` adds these columns:

| Column | Type | Meaning |
| --- | --- | --- |
| `expired_at` | `timestamptz`, null | When the deadline expired it |
| `claimed_by` | `text`, null | The user who claimed the task. Null when unclaimed |
| `claimed_at` | `timestamptz`, null | When it was claimed |

Its `state` takes `expired` as well. `axis.process_work_items` adds a `task_id`
column, a foreign key to the task, and the kind `task.deadline`, with `task_id`
and `process_instance_id`. _(Planned for M14)_ It also adds the kind
`task.escalate`, with `task_id` and `escalation`.

## Inbox and task page

The task API tells the page what to show.

- A single read, and the claim, release, reassign and complete responses, add
  `commands`: the actions the signed-in user may take on the task now, from
  `claim`, `release` and `reassign`, always in that order. It is empty unless
  the task is `open`, because each of these actions refuses a task that is not.
  - `claim` is listed for an unclaimed role task that the user may act on, that
    is, they hold its role.
  - `release` is listed for a claimed task when the user holds `reassign`, or
    when the user is the claimer and may still act on the task.
  - `reassign` is listed when the user holds the `reassign` grant on the task's
    process in the active release.
- The same responses add `reassignRoles`: the `{ name, labelKey }` of each role
  of the active release, ordered by name. `labelKey` is `null` for a role with
  no label. It is empty unless `commands` holds `reassign`.
- Who may read a task does not change. A user with the grant but without the
  task's role gets `403` on a read, so they see `reassign` only in the claim,
  release and reassign responses.

The [task inbox widget](task-inbox-widget.md) and the task page use these
fields.

- The inbox shows an "Overdue" tag beside the due date of an `overdue` task, and
  a "Claimed by you" tag on a row whose `claimedBy` is set.
- The task page shows the "Overdue" tag. It shows **Claim** and **Release** when
  `commands` lists them, and shows the task the route returns. A `403` or `409`
  shows a message and loads the task again.
- An `expired` task shows that its deadline passed, with no form or buttons.
- The task page shows **Reassign** when `commands` lists it. It opens a dialog
  that works from the keyboard. The user chooses a role or a person, and types
  a required reason of at most 500 characters.
  - The role list is `reassignRoles`, each shown by its label, or by its name
    when `labelKey` is `null`.
  - The person picker lists the first 20 people when it opens and asks the
    [user picker](#reassign) again as the user types. It shows each display name.
    The page takes its address from the inbox's `tasksPath`, by replacing the
    trailing `/tasks` with `/users`. If the picker cannot load, its list stays
    empty and choosing a role still works.
  - On success the page goes to the inbox page's plain address.
  - A `400` shows each message under the role, person or reason field its
    pointer names, and a problem keyed `""` above the form. The dialog stays
    open.
  - A `403` or `409` closes the dialog, loads the task again and shows the same
    message as Claim and Release.
  - Any other failure shows a generic message in the dialog and keeps what the
    user typed.
