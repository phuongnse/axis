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
`task.deadline` work item are built. Escalations, overdue tasks, claim,
release and reassign are _(planned for M14)_.

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

**Overdue** _(planned for M14)_. A task is overdue when it is `open` and its `due_at` has passed.
The task API's list and read add `"overdue": true|false`, worked out when read.
Nothing is stored.

## Claim and release

_(Planned for M14)_ This section is not built yet.

```http
POST /api/apps/{app}/tasks/{id}/claim
POST /api/apps/{app}/tasks/{id}/release
```

- **Claim.** A user who holds the task's role may claim an open role task.
  The task stores `claimed_by` and `claimed_at`. Then only the claimer may
  complete it, and it leaves the lists of the other role holders.
- **Release.** The claimer, or a user with the `reassign` grant on the
  process, may release a claim. The task goes back to every holder of the role.
- **Errors.** A task assigned to a user, or already claimed, is a `409` with a
  fixed title. A user who may not act on the task gets the `403` of the task
  API. An unknown task is a `404`.
- **Audit.** `task.claimed` and `task.released`, with the task and the actor.
- The list and read add `"claimedBy"`, the claimer's user id, or `null`.

## Reassign

_(Planned for M14)_ This section is not built yet.

```http
POST /api/apps/{app}/tasks/{id}/reassign
Content-Type: application/json

{ "role": "service-desk-lead", "reason": "Agent on leave." }
```

- The body has exactly one of `user`, a user id, and `role`, a role name, and
  a `reason` of at most 500 characters that is not blank.
- **Who may act.** A user with the `reassign` action on a grant of the task's
  process. The assignee may not reassign without it. A `reassign` grant needs
  no role on the task.
- **Target.** A `role` must be declared by the active release. A `user` must
  have a [user profile](notifications.md#user-profiles), so a task never goes
  to an id that nobody has signed in with. Otherwise it is a `400` keyed
  `/role` or `/user`.
- **Effect.** The task's assignee kind and assignee change, and a claim is
  cleared. The assignee check runs again at the moment of each later action.
- **User picker.** `GET /api/apps/{app}/users?search=` lists up to 20 user
  profiles whose display name contains the search text, ignoring case. It
  needs a `reassign` grant on some process of the application, otherwise
  `403`.
- **Audit.** `task.reassigned`, with the old and new assignee and the reason.
  An escalation writes it with the actor `system`.

## Tables

`axis.process_tasks` adds these columns:

| Column | Type | Meaning |
| --- | --- | --- |
| `expired_at` | `timestamptz`, null | When the deadline expired it |
| `claimed_by` | `text`, null | The user who claimed the task. Null when unclaimed _(planned for M14)_ |
| `claimed_at` | `timestamptz`, null | When it was claimed _(planned for M14)_ |

Its `state` takes `expired` as well. `axis.process_work_items` adds a `task_id`
column, a foreign key to the task, and the kind `task.deadline`, with `task_id`
and `process_instance_id`. _(Planned for M14)_ It also adds the kind
`task.escalate`, with `task_id` and `escalation`.

## Inbox and task page

_(Planned for M14)_ This section is not built yet.

- The [task inbox widget](task-inbox-widget.md) marks an overdue task and
  shows who claimed a task.
- The task page shows **Claim** on an unclaimed role task, **Release** to the
  claimer, and **Reassign** to a user with the grant. Reassign opens a dialog
  with a role list, the user picker and the reason.
