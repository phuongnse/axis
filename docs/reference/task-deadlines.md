# Task deadlines and claims

A task need not wait for ever on one person. This page covers what moves a task on its own, its
escalations and its deadline, when a task is overdue, and the routes that let people claim, release
and reassign a task. The `task` step is in [processes](processes.md#steps), and the task API in
[task API](task-api.md).

## Escalations and deadline

A task step may add `escalations`, which reassign the task or notify people after a time, and a
`deadline`, which ends the task and moves the instance on when nobody decided in time:

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
      "name": "escalation1",
      "after": "P1D",
      "notify": {
        "to": [{ "role": "service-desk-lead" }],
        "channels": ["inApp"],
        "message": { "subject": { "textKey": "ticket.late.subject" }, "body": { "textKey": "ticket.late.body" } }
      }
    },
    { "name": "escalation2", "after": "P2D", "reassign": { "role": "service-desk-lead" } }
  ],
  "deadline": { "after": "P5D", "next": "markExpired" },
  "outcomes": [{ "name": "resolved", "label": { "textKey": "task.resolved" }, "next": "confirm" }]
}
```

- **Durations.** Every `after` is a positive fixed-length duration, by the rule of `dueIn`
  (`AXC0084`, at `/steps/{i}/deadline/after` for the deadline and at
  `/steps/{i}/escalations/{j}/after` for an escalation). It counts from the time of the task step's
  transaction, the same time `dueIn` counts from and the task's `created_at`. In a
  [test scenario](test-scenarios.md) that time follows the scenario's `now`. There are no business
  hours or calendars.
- **Escalation.** Each has a `name`, `after` and at least one of `reassign` and `notify`, otherwise
  `AXC0280` at `/steps/{i}/escalations/{j}`. A missing `name` is `AXC0004`, and a later escalation
  with a name already used in the task, ignoring letter case, is `AXC0359` at
  `/steps/{i}/escalations/{j}/name`. A [customization layer](customization-layers.md#list-keys) finds
  an escalation by its `name`. `reassign` has the shape and the checks of `assignee`, reported under
  `.../reassign`. `notify` has the shape and the checks of the
  [`notify` operation](notifications.md#notify-operation), without `next`, reported under
  `.../notify`. The compiler keeps escalations in file order and does not require them to be in
  order of `after`. An escalation does not end the task.
- **Deadline.** It has `after` and `next`, and `next` names a step of the process (`AXC0070` when it
  names none). It counts as a transition of the step graph, checked after the outcomes, so a step
  that only a deadline leads to is reachable. Each escalation's `after` is shorter than the
  deadline's, otherwise `AXC0281` at that escalation's `/after`.
- **Work items.** The task step's transaction queues one `task.deadline` item, with the task id and
  the instance id, due at the step's time plus the deadline's `after`. It also queues one
  `task.escalate` item per escalation, with the task id, the instance id and the escalation's index
  in file order, due at the step's time plus its `after`. The escalation items are queued by `after`,
  before the deadline item, so a [test scenario](test-scenarios.md) that runs items in queue order
  passes them in order of time.
- **Release.** The deadline comes from the task's pinned release, and role checks
  use the active release, as [Releases and running work](processes.md#releases-and-running-work)
  says.
- **Test scenarios.** `advanceTime` runs due `task.escalate` and `task.deadline` items, and a scenario
  action never runs one early, see [Test scenarios](test-scenarios.md).
- **Speed.** A worker runs 500 due `task.escalate` items, each of which reassigns its task and stores
  a notification, and then the 500 `task.deadline` items of the same tasks, within 2 minutes.
  `TaskEscalationPerformanceTests` fails past that limit.

An escalation runs when its `task.escalate` item is due:

1. The item locks the task row. A task that is no longer `open` makes it do nothing.
2. A `reassign` is applied as a [reassign](#reassign) by `system`: the task goes to the target and a
   claim is cleared. The task stays `open`, and its instance and its deadline do not change. A `user`
   expression is evaluated over the subject record when the item runs, as a task step evaluates its
   assignee. If it gives null, blank text or an error, the task is left as it is, the handler logs a
   warning and the escalation's `notify` still runs, because a late reassign must not fail an
   instance that waits correctly. A `role` target is not checked against the active release, and a
   `user` target is not checked against the user profiles, because the compiler checked the role
   against the pinned release and a step's `assignee` is not checked against profiles either.
3. A `notify` is evaluated over the subject record, with its pending computed fields and at the
   transaction's time, as a `notify` step is, and the notification stores the task's pinned release.
   It stores one in-app notification row for each user and role recipient when the `inApp` channel is
   listed, and stages emails in the email outbox when the `email` channel is listed, as a `notify`
   step does, see [email](notifications.md#email). The notification adds no audit record: only a
   `reassign` writes `task.reassigned`.
4. The step history gets a row with `{ "escalation": <index> }` and no decision. When the escalation
   has `notify`, the row's output adds `notificationId`.

An escalation with both `reassign` and `notify` does both in one transaction, and one with only
`notify` leaves the task, its assignee and its deadline as they are. If the handler fails, for
example because a `notify` expression fails at run time, the whole escalation is rolled back, and the
handler logs the error type and does not fail the instance.

The deadline runs when its `task.deadline` item is due. The item locks the task row. When the task
is still `open`, the task becomes `expired` with `expired_at` set, the instance resumes at `next`,
the history row's `decision` is `deadline`, and the audit record is `task.expired`, by `system`. A
task that is no longer `open` is left alone. If the handler fails, it logs the error type and does
not fail the instance, since it can fail only when the stored data is inconsistent.

An open task ends exactly one way. Completion, the deadline, an escalation and a reassign lock the
task row first, and a cancel closes the instance's open tasks in its own transaction, so a task that
is no longer `open` makes an item do nothing. Completing a task deletes its pending `task.deadline`
and `task.escalate` items, the deadline's expiry deletes the `task.escalate` items, and a cancel
deletes every work item of the instance.

## Task states

This section says how the task API answers for an expired task and when a task counts as overdue.
The states themselves are in [process states](process-states.md#task-states).

An expired task leaves every inbox and cannot be completed: the task API answers a completion of it
with the `409` it gives for a completed task. It reads with `state` `expired`, and the API does not
return `expired_at`.

**Overdue.** A task is overdue when it is `open` and its `due_at` is before the database's current
time. The task API's list and read add `"overdue": true|false`, worked out when read with database
time, and nothing is stored. A completed, cancelled or expired task is never overdue.

## Claim and release

A user who holds a task's role may claim it, so that the other holders of the role stop working on
it.

| Route                                     | What it does                                                          |
| ----------------------------------------- | --------------------------------------------------------------------- |
| `POST /api/apps/{app}/tasks/{id}/claim`   | Claims an open role task, see [claim and release](#claim-and-release) |
| `POST /api/apps/{app}/tasks/{id}/release` | Releases a claim, see [claim and release](#claim-and-release)         |

- **Claim.** A user who holds the task's role may claim an open role task. The task stores
  `claimed_by` and `claimed_at`. Then only the claimer may complete it, and it leaves the lists of
  the other role holders.
- **Release.** The claimer may release a claim. A user with the `reassign` grant on the process may
  release anyone's claim, with no role on the task. The task goes back to every holder of the role.
- **Response.** Both routes answer `200` with the task in the shape of a single read of the
  [task API](task-api.md#read-one-task), whose `claimedBy` is the claimer's user id, or `null`.
- **Errors.** The checks run in the task API's order: `401`, `404`, `403`, then `409`. A user who may
  not act on the task gets the `403` of the task API, and so does a role holder without the
  `reassign` grant who releases another holder's claim. An unknown task is a `404`, and a task that
  is no longer open is the task API's `409`. Three more cases are a `409` with a fixed title:
  - Claiming a task assigned to a user: "Only a task assigned to a role can be claimed."
  - Claiming a task that is already claimed: "This task is already claimed."
  - Releasing a task that is not claimed: "This task is not claimed."
- **Reading.** Other role holders may still read a claimed task and ask for its form state. Only
  completing it, and listing it, is limited to the claimer.
- **Audit.** `task.claimed` and `task.released`, with the actor, the task id, the subject record and
  the instance.

## Reassign

| Route                                      | What it does                                                    |
| ------------------------------------------ | --------------------------------------------------------------- |
| `POST /api/apps/{app}/tasks/{id}/reassign` | Gives a task to another user or role, see [reassign](#reassign) |
| `GET /api/apps/{app}/users`                | Lists the users to pick from, see [reassign](#reassign)         |

A user with the `reassign` grant may give a task to another user or role, with a reason:

```http
POST /api/apps/{app}/tasks/{id}/reassign
Content-Type: application/json

{ "role": "service-desk-lead", "reason": "Agent on leave." }
```

- **Body.** It has exactly one of `user`, a user id, and `role`, a role name, and a `reason` of at
  most 500 characters that is not blank. A body that has neither or both of `user` and `role` is one
  problem on the whole body, keyed `""`. A value that is not text or is blank is `Required.` at its
  pointer.
- **Who may act.** A user with the `reassign` action on a grant of the task's process in the active
  release. The assignee may not reassign without it, and a `reassign` grant needs no role on the
  task. A process the active release does not have has no holder. Without the grant the answer is
  the `403` titled "You have no grant for this action.", whoever the assignee is.
- **Response.** `200` with the task in the shape of a single read of the [task API](task-api.md), as
  claim and release answer. The fields shown are still limited by the actor's `read` grants.
- **Errors.** The checks run in the task API's order: `401`, `404`, `403` for the grant, `409` for a
  task that is not open, `415`, `400` for the body, then `400` for the target. The route takes no
  start lock, and it locks the task row.
- **Target.** A `role` must be declared by the active release. A `user` must have a
  [user profile](notifications.md#user-profiles), so a task never goes to an id that nobody has
  signed in with. Otherwise it is a `400` keyed `/role` or `/user`.
- **Effect.** The task's assignee kind and assignee change, and a claim is cleared. The assignee check
  runs again at the moment of each later action. The deadline does not change, so the new assignee
  inherits `due_at` and the pending `task.deadline` work item.
- **User picker.** `GET /api/apps/{app}/users?search=` lists up to 20 user profiles whose display name
  or email contains the search text, ignoring case, sorted by display name and then id. An empty or
  missing search lists the first 20. It answers `{ "items": [{ "id", "displayName" }] }` and nothing
  else of a profile, so the email is a match condition only. It needs a `reassign` grant on some
  process of the application, otherwise `403`. A text field marked
  [`userId`](resource-files.md#entity-field-types-and-constraints) has a search route of its own for
  people who may write that field. Only people who have signed in have a profile, so a new colleague
  can receive a task after one sign-in.
- **Audit.** `task.reassigned`, with the actor, the task id, the subject record and the instance. Its
  details are `{ "taskId", "from", "to", "reason" }`, where `from` and `to` have the shape of
  `assignee`: `{ "role": name }` or `{ "user": id }`. An escalation writes it with the actor `system`,
  no display name, and the details `{ "taskId", "from", "to", "escalation" }`, which have the
  escalation's index and no `reason`, because no person typed one.

## Tables

The columns that deadlines and claims use are in [process tables](process-tables.md#tables):
`expired_at`, `claimed_by` and `claimed_at` of `axis.process_tasks`, and the `task_id` and
`escalation` columns and the `task.deadline` and `task.escalate` kinds of `axis.process_work_items`.

## Inbox and task page

The task API tells the inbox and the task page which actions to offer, and these show them.

- A single read, and the claim, release, reassign and complete responses, add `commands`: the actions
  the signed-in user may take on the task now, from `claim`, `release` and `reassign`, always in that
  order. It is empty unless the task is `open`, because each of these actions refuses a task that is
  not.
  - `claim` is listed for an unclaimed role task that the user may act on, that is, they hold its
    role.
  - `release` is listed for a claimed task when the user holds `reassign`, or when the user is the
    claimer and may still act on the task.
  - `reassign` is listed when the user holds the `reassign` grant on the task's process in the active
    release.
- The same responses add `reassignRoles`: the `{ name, labelKey }` of each role of the active
  release, ordered by name. `labelKey` is `null` for a role with no label. It is empty unless
  `commands` holds `reassign`.
- Who may read a task does not change. A user with the grant but without the task's role gets `403`
  on a read, so they see `reassign` only in the claim, release and reassign responses.

The [task inbox widget](task-inbox-widget.md) and the task page use these fields:

- The inbox shows an "Overdue" tag beside the due date of an `overdue` task, and a "Claimed by you"
  tag on a row whose `claimedBy` is set.
- The task page shows the "Overdue" tag. It shows Claim and Release when `commands` lists them, and
  shows the task the route returns. A `403` or `409` shows a message and loads the task again.
- An `expired` task shows that its deadline passed, with no form or buttons.
- The task page shows Reassign when `commands` lists it. It opens a dialog that works from the
  keyboard, where the user chooses a role or a person and types a required reason of at most 500
  characters.
  - The role list is `reassignRoles`, each shown by its label, or by its name when `labelKey` is
    `null`.
  - The person picker lists the first 20 people when it opens and asks the [user picker](#reassign)
    again as the user types, showing each display name. The page takes its address from the inbox's
    `tasksPath`, by replacing the trailing `/tasks` with `/users`. If the picker cannot load, its list
    stays empty and choosing a role still works.
  - On success the page goes to the inbox page's plain address.
  - A `400` shows each message under the role, person or reason field its pointer names, and a
    problem keyed `""` above the form. The dialog stays open.
  - A `403` or `409` closes the dialog, loads the task again and shows the same message as Claim and
    Release.
  - Any other failure shows a generic message in the dialog and keeps what the user typed.
