# D19. Process resource and worker host

Status: **Agreed**

## Context

[D11](d11-durable-process-engine.md) asks for a durable engine. Applications need a resource that
declares a process, and a host that runs its steps away from the request thread.

## Decision

- **A process runs for one record,** of the entity it names as its subject, or without a subject as
  [D70](d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md) agrees.
- **Steps name the step that follows.** The step types are `decision`, `task`
  ([D20](d20-human-tasks-task-inbox-and-forms.md)), `operation`, `end` and those of
  [D69](d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md). Decisions route
  by expressions and named rules, so a threshold is configuration.
- **One running instance per process and record,** pinned to the release active at its start.
- **A worker host, `Axis.Worker`, runs the steps,** never the server.
- **Each step is one transaction** with its business writes, the instance's state and revision, its
  history, its audit records and the next work item.
- **Workers claim work with leases and fencing.**
- **A failing step fails the instance,** and only an operator retries it
  ([D35](d35-operator-retry-cancel-and-resolve.md)).

The detail is in [processes](../reference/processes.md) and
[process execution](../reference/process-execution.md#execution).

## Consequences

- A crash leaves no partial step, and another worker takes over an item whose lease expired.
- The one-running-instance rule stops a record from being submitted twice, and stops triggers from
  looping ([D26](d26-events-and-triggers.md)).
- Every installation runs a worker host beside the server.
