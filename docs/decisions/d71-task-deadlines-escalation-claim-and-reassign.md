# D71. Task deadlines, escalation, claim and reassign

Status: **Agreed**

M14 makes a task's time count and lets people take and hand on tasks. The
owner agreed this on 2026-10-25. It is built in M14, except the email channel of
an escalation's `notify`, which is _(planned for M18)_. See
[task deadlines and claims](../reference/task-deadlines.md). It replaces the
"Escalation needs timers, so it comes later" and "Queues come later" points of
[D20](d20-human-tasks-task-inbox-and-forms.md), and adds the `reassign` action
to the `process` grant of [D24](d24-roles-and-policies.md).

- **Fixed durations:** deadlines and escalations count from the task's
  creation, in fixed-length durations by the rule of `dueIn`. There are no
  business hours or calendars.
- **Escalations:** a task step may list `escalations`, each
  `{ "after", "reassign", "notify" }` with at least one of `reassign` and
  `notify`.
  - An escalation does not end the task. Each runs once, in order of `after`.
  - `reassign` takes an assignee, as the step's `assignee` does. `notify` takes
    the shape of the `notify` operation of
    [D73](d73-in-app-and-email-notifications.md).
- **Deadline:** a task step may have `deadline: { "after", "next" }`.
  - When it passes, the task becomes `expired`, a new task state, and the
    instance takes `next`. The history `decision` is `deadline`.
  - Every escalation's `after` is shorter than the deadline's. Otherwise it is
    a new diagnostic.
- **One winner:** completion, cancel, an escalation and the deadline each lock
  the task row first, so only one of them changes an open task. Completing,
  cancelling or expiring a task deletes its pending escalation and deadline
  work items.
- **Overdue:** a task is overdue when it is open and its due date has passed.
  The task API reports it as `overdue`, worked out when read. Nothing is
  stored.
- **Claim and release:** a user who holds a task's role may claim it. Then only
  that user may complete it, and it leaves the other role holders' lists. The
  claimer, or a user with the `reassign` grant, may release it.
  - Claiming a task assigned to a user, or one already claimed, is a `409`.
  - The audit records are `task.claimed` and `task.released`.
  - A role with claim is M14's answer to queues.
- **Reassign:** a user with the new `reassign` action on the process's grant
  may give an open task to a role the active release declares, or to a known
  user, with a reason.
  - A known user is one in the user profiles of
    [D73](d73-in-app-and-email-notifications.md), so a reassign never targets
    an id that nobody has signed in with.
  - The assignee may not reassign a task without the grant. They may only
    release a claim.
  - Reassigning clears a claim. The audit record is `task.reassigned`, with the
    old and the new assignee. An escalation's reassign writes it as `system`.
  - The assignee check still runs at the moment of action.
- **Release:** escalations and the deadline come from the task's pinned
  release. Roles and policies come from the active release, as today.
- **Inbox:** the task inbox and the task page show Claim, Release and, with the
  grant, Reassign. An overdue task is marked.
- **Service desk:** the agent's task escalates to the service desk lead when it
  is not resolved in time.
- **Effect on earlier decisions:** D20 and D24 link here where this decision
  changes them.

_Why:_ see [human work](../domain/knowledge.md#human-work). A task that nobody
acts on must reach someone, and a deadline must be able to move the process on.
Locking the task row first gives exactly one outcome for an open task, as
completion already does. A claim lets a team share a role's tasks without two
people doing the same work. Reassigning only to roles and to users who have
signed in keeps a task from going to an id nobody holds.
