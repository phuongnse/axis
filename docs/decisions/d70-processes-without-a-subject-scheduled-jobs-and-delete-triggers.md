# D70. Processes without a subject, scheduled jobs and delete triggers

Status: **Agreed**

M14 lets a process run without a subject record, so a schedule can run a job
and a delete can start a process. The owner agreed this on 2026-10-25. It is
built in M14. See [process flow](../reference/process-flow.md#processes-without-a-subject)
and [process events](../reference/process-events.md). It replaces two points of
[D26](d26-events-and-triggers.md): "processes without a subject come later" and
"`delete` comes later, because a deleted record cannot be a subject".

- **No subject:** `entity` becomes optional on a `process`. A process without
  one declares `parameters`, a list of `{ "name", "type" }` with the scalar
  types of rule parameters.
  - The instance stores the parameter values in a `parameters` column.
    Expressions see them by name.
  - `updateRecord`, `raiseEvent`, `task` and `waitEvent` steps need a subject,
    so they are compile errors in such a process. So is the `output` of a
    `callConnector` or package operation step, because it writes the subject.
  - `decision`, `createRecord`, `callConnector`, package operations, `timer`,
    `subProcess`, `notify` and `end` are allowed.
- **One running instance:** at most one instance of a process without a
  subject runs at a time. The partial unique index treats the missing subject
  as one value. A second start from a trigger is skipped, as the guard against
  loops does today. A second start through the endpoint is a `409`.
- **Where parameters come from:** a trigger, the start endpoint, with a body
  `{ "parameters": { … } }`, and a sub-process step. The `startCondition`
  sees the parameters.
- **Scheduled job:** a schedule trigger may leave out `dataSource`. It then
  names a process without a subject, and its `parameters` give the process's
  parameters as literal values, in the forms a data source's parameters take
  today. Each occurrence starts the process once. The start receipt uses the
  nil UUID as its record.
- **Delete trigger:** a data change trigger with `"on": ["delete"]` names a
  process without a subject. Its `parameters` and `when` are expressions over
  the record as it was before the delete.
  - `delete` cannot be listed with `create` or `update`, and takes no
    `fields`. Each is a new diagnostic.
  - The start commits in the delete's transaction. The `409` for a record in a
    running process still comes first.
- **Inspection:** the instance list and detail show "no subject". A user needs
  only the grant, because there is no record to read.
- **Service desk:** a daily scheduled job reminds the service desk lead of
  overdue tickets, and a delete trigger tells the lead which ticket was
  deleted, both through notifications
  ([D73](d73-in-app-and-email-notifications.md)).
- **Effect on earlier decisions:** D26 links here where this decision replaces
  its points.

_Why:_ see [what applications use most](../domain/knowledge.md#what-applications-use-most).
Reports, reminders and clean-ups run on a clock and belong to no record, and a
deleted record can no longer be a subject. Typed parameters keep these
processes checked by the compiler. One running instance per process keeps a
slow job from piling up, as the subject rule does for records.
