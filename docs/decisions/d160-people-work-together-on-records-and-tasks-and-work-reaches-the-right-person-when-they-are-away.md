# D160. People work together on records and tasks, and work reaches the right person when they are away

Status: **Agreed**

## Context

A task went to one user or one role, a person heard of it only if a step said so, and an inbox showed
only open tasks. A record had no place for people to talk, delegation lived inside the `approvals`
module and only for its own steps, and nothing kept a requester from deciding their own request
outside a module contract. An agent and a requester discuss a ticket, a manager clears forty leave
requests, an approver answers from their phone, and work moves on while someone is away. ServiceNow,
Jira, SAP and Lark all do this. [D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts it among the foundations. This decision replaces
D20 and D124, and keeps every part of them that still holds.

## Decision

- **A task is a step,** with a label, its assignees, a form, an optional due time and its outcomes.
  [D156](d156-working-time-calendars-durations-in-working-hours-and-slas.md) adds deadlines,
  escalation, claim and reassign, and
  [D155](d155-processes-run-paths-at-once-repeat-steps-give-tasks-to-many-people-and-take-error-paths.md)
  tasks for several people with a rule of completion.
- **A task goes to a user, a role or a list of people.** A user and a list come from expressions, so a
  list may be the people of a department. A role task is claimed, or given to the holder with the
  fewest open tasks, or to each holder in turn.
- **Exactly one decision for each task.** An open task is completed once, with one of its outcomes,
  and the task form saves with it in the same transaction.
- **Who may act is checked at the moment of action:** an assignee, a holder of the assignee role, or
  the delegate of an assignee who is away. Policies add to this check and never replace it.
- **A task may name who must not act on it:** the person who created its record, the person who acted
  on an earlier step, or people an expression names. The engine refuses them.
- **A person who is away names a delegate.** Each person sets the dates of an absence and a delegate.
  When it starts, their open tasks move to the delegate, and new tasks go to the delegate while it
  lasts. A decision records whom it was made for. This holds for every process.
- **A task inbox widget** lists the person's open tasks and the tasks they completed, and lets them
  decide several tasks at once with one outcome when their forms ask for nothing more, with a result
  for each. A form widget can start a process after it saves the record.
- **A person hears of a task given to them,** in the application and by email, unless they turn it
  off. Each person chooses which notifications reach them, by which channel, and whether they come
  together in a daily digest.
- **A task can be decided from an email.** An email's outcome buttons open Axis, where the person
  signs in, with the second step their account needs, and confirms with one click. No link decides on
  its own, since an email can be forwarded.
- **Records and tasks have comments.** A comment may mention people, who are told, and carry files. A
  comment is internal, or shared with the people outside the organization who may see the record, such
  as a customer on a portal. A person edits or deletes their own comment for a short time, and history
  keeps each version.
- **A record page has one timeline** of its comments, its changes, the steps of its processes and the
  emails sent about it, which a reader filters by kind.
- **Pages update live.** The bell, the task inbox and the lists a person has open change as the work
  does, through server-sent events on the person's session.
- **One `form` resource serves a page and a task:** sections of one entity's fields, each of which
  can be read-only.
- **Request types decide the approval path.** `approvals` holds request types, each with its
  approval steps in order: the requester's line manager, the head of their department or a named
  employee, each with an optional minimum amount. A business's administrator changes them in the
  running application, with no Studio and no publish. Studio and layers stay for deeper changes.
- **`organization` holds departments and employees.** A department has a head and a parent, and an
  employee a line manager and the account they sign in with. It keeps only the contact and routing
  data an office needs, and no further personal data, see
  [the organization module](../reference/built-in-modules.md#the-organization-module).
- **Approvals follow the platform's absences.** While an approver is away, their approval goes to
  their delegate, and the decision records whom it was made for.
- **The contract of approvals.** Nobody decides their own request, and each step is decided once,
  whatever a business changes. `approved` and `rejected` are protected states, moving to the next
  step needs a new deciding task through the `decisionTask()` of
  [D121](d121-how-a-module-contract-is-declared-and-checked.md), and a step whose approver would be
  the requester goes to the `admin` slot.
- **General expression features, not approvals code.** An aggregate may follow a path to another
  record's collection, `outer.<field>` reads the outer record inside an item expression,
  `first(list, condition, item)` picks the first matching row, and a reference field may `match` its
  record by a unique field. Expressions still run no query of their own: their rows come from the
  references a step follows.
- **A built-in application is a template marked built in.** `applications/approvals` is a thin
  template with `"builtIn": true`, which Studio lists first and from which tenants are created.

The detail is in [processes](../reference/processes.md#steps), the [task API](../reference/task-api.md),
the [task inbox widget](../reference/task-inbox-widget.md), the [form resource](../reference/form-widget.md),
[notifications](../reference/notifications.md), [built-in modules](../reference/built-in-modules.md),
the [expression functions](../reference/expressions.md#functions) and
[matched references](../reference/resource-files.md#matched-references).

## Consequences

- Every application gets the same inbox, assignment, absences and authorization for human work.
- A layout is written once for a page and a task.
- People discuss a record where it lives, and a customer on a portal sees only the comments shared
  with them.
- Work does not wait for someone who is away, in any process.
- A requester cannot decide their own request in any process that says so, not only in `approvals`.
- A live page holds a connection for each open session, which the server's limits count.
- A new request type needs no Studio and no publish, as in Lark Approval, Base.vn and Odoo Approvals.
- The protected states, the deciding task and the accountable user make the module's promise hold
  against any layer.

## Alternatives

- **Decide from an email with one click.** A forwarded email would let anyone decide.
- **Shared comments later, with portals.** The service desk of M30 needs them first.
- **WebSockets for live pages.** Server-sent events go one way, which is all these pages need, and use
  the session the page already has.
- **Delegation for each module.** Each module would build absences again, and a process outside them
  would wait.
- **A fixed approval process.** Every new request type would go through Studio and a publish.
- **A `lookup()` function in expressions.** It would hide a query inside an expression.
