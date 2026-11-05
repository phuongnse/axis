# D20. Human tasks, task inbox and forms

Status: **Agreed**

## Context

Processes need people to decide. A platform without a task concept leaves every application to model
assignment with ordinary records, inconsistently and hard to secure, as
[human work](../domain/knowledge.md#human-work) describes.

## Decision

- **A task is a step,** with a label, an assignee (a user from an expression, or a role), a form, an
  optional due time and its outcomes.
  [D71](d71-task-deadlines-escalation-claim-and-reassign.md) adds deadlines, escalation, claim and
  reassign.
- **Exactly one decision.** An open task is completed once, with one of its outcomes, and the task
  form saves with it in the same transaction.
- **Who may act is checked at the moment of action:** the assignee or a holder of the assignee role.
  Policies add to this check and never replace it.
- **A task inbox widget** lists the user's open tasks, and a form widget can start a process after it
  saves the record.
- **One `form` resource serves a page and a task:** sections of one entity's fields, each of which
  can be read-only.

The detail is in [processes](../reference/processes.md#steps), the [task API](../reference/task-api.md),
the [task inbox widget](../reference/task-inbox-widget.md) and the
[form resource](../reference/form-widget.md).

## Consequences

- Every application gets the same inbox, assignment and authorization for human work.
- A layout is written once for a page and a task.
