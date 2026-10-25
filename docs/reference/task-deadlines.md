# Task deadlines and claims

Detailed reference for task escalations and deadlines, overdue tasks, and the
routes that claim, release and reassign a task. The task step is in
[processes.md](processes.md#steps), and the task API in
[task-api.md](task-api.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md).

Everything in this file is _(planned for M14)_. The design follows
[D71](../decisions/d71-task-deadlines-escalation-claim-and-reassign.md).

## Escalations and deadline

A task step may add `escalations` and a `deadline`:

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
  of `dueIn` (`AXC0084`). It counts from the task's `created_at`. There are no
  business hours or calendars.
- **Escalation.** Each has `after` and at least one of `reassign` and
  `notify`. `reassign` has the shape of `assignee`. `notify` has the shape of
  the [`notify` operation](notifications.md#notify-operation), without `next`.
  An escalation does not end the task.
- **Deadline.** `after` and `next`. `next` names a step of the process. Each
  escalation's `after` is shorter than the deadline's, otherwise a new
  diagnostic at that escalation's `/after`.
- **Work items.** The task step's transaction queues one `task.escalate` item
  per escalation, with the task id and the escalation's index, and one
  `task.deadline` item. Each is due at `created_at` plus its `after`.
- **Escalation runs.** The item locks the task row. When the task is still
  `open`, it applies `reassign` as a [reassign](#reassign) by `system`, and
  stages `notify` as the operation does. The step history gets a row with
  `{ "escalation": <index> }`.
- **Deadline runs.** The item locks the task row. When the task is still
  `open`, the task becomes `expired`, the instance resumes at `next`, and the
  history row's `decision` is `deadline`. The audit record is `task.expired`.
- **One winner.** Completion, cancel, an escalation and the deadline all lock
  the task row first. A task that is no longer `open` makes the item do
  nothing. Completing, cancelling or expiring a task deletes its pending
  `task.escalate` and `task.deadline` items.
- **Release.** Escalations and the deadline come from the task's pinned
  release. Role checks use the active release, as today.

## Task states

The task states of [processes.md](processes.md#task-states) gain `expired`: the
deadline passed before a decision, and the instance moved on. An expired task
leaves every inbox and cannot be completed.

**Overdue.** A task is overdue when it is `open` and its `due_at` has passed.
The task API's list and read add `"overdue": true|false`, worked out when read.
Nothing is stored.

## Claim and release

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
| `claimed_by` | `text`, null | The user who claimed the task. Null when unclaimed |
| `claimed_at` | `timestamptz`, null | When it was claimed |
| `expired_at` | `timestamptz`, null | When the deadline expired it |

Its `state` takes `expired` as well. `axis.process_work_items` adds the kinds
`task.escalate`, with `task_id` and `escalation`, and `task.deadline`, with
`task_id`.

## Inbox and task page

- The [task inbox widget](task-inbox-widget.md) marks an overdue task and
  shows who claimed a task.
- The task page shows **Claim** on an unclaimed role task, **Release** to the
  claimer, and **Reassign** to a user with the grant. Reassign opens a dialog
  with a role list, the user picker and the reason.
