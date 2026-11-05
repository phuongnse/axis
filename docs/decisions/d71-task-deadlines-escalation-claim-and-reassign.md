# D71. Task deadlines, escalation, claim and reassign

Status: **Agreed**

## Context

A task of [D20](d20-human-tasks-task-inbox-and-forms.md) has a due date that nothing acts on. A task
that nobody acts on must reach someone, and a deadline must be able to move the process on, as
[human work](../domain/knowledge.md#human-work) says. A team that shares a role also needs to take a
task without two people doing it, and to hand one on.

## Decision

- **Fixed durations** from the task's creation. There are no business hours or calendars.
- **Escalations** reassign the task, notify people as [D73](d73-in-app-and-email-notifications.md)'s
  `notify` does, or both, and do not end it.
- **A deadline** expires the task and moves the instance on. Every escalation comes before it.
- **One outcome.** Each action on an open task locks its row first.
- **Claim.** A role holder may claim a role task, and only the claimer then completes it.
- **Reassign.** A new `reassign` action on the `process` grant of [D24](d24-roles-and-policies.md)
  gives an open task, with a reason, to a declared role or to a user who has signed in.

The detail is in [task deadlines and claims](../reference/task-deadlines.md).

## Consequences

- A claim on a role is how a team works a queue, so there is no queue resource.
- A task never goes to an id nobody has signed in with, but a new colleague gets tasks only after a
  first sign-in.
- Business hours and calendars cannot be expressed.
