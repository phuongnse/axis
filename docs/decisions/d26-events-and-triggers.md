# D26. Events and triggers

Status: **Agreed**

## Context

Applications need things that happen to start work: a step that announces an outcome, a record that
changes, a time of day, as [what applications use most](../domain/knowledge.md#what-applications-use-most)
says. [D19](d19-process-resource-and-worker-host.md) runs a process for one record, with one running
instance per process and record. A start must happen at most once after a crash, and a broken
trigger must not block a user's save.

## Decision

- **Every trigger starts a process for one record** of its subject entity, or a process without a
  subject with parameters, as
  [D70](d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md) agrees.
- **An `event` resource** is about one entity and has typed scalar payload fields. A raise event step
  stores each occurrence in its transaction, and an occurrence is kept even when no trigger listens,
  so a later wait for event can match it.
- **A `trigger` resource** names the process it starts, and its type: `event`, `dataChange` or
  `schedule`. Inbound endpoints are their own resource, in [D27](d27-inbound-endpoints.md).
- **An event trigger** is `async`, delivered by the worker, or `sync`, committed with the raising
  step.
- **A data change trigger** fires on every committed write through the record command, system writes
  included, and starts in the write's transaction.
- **A schedule trigger** runs a cron schedule in its own IANA time zone, over the records of a data
  source. Each occurrence runs once per tenant across workers, and after downtime only the latest
  missed occurrence runs.
- **Each start happens at most once** per occurrence and trigger, also after a worker restart.
- **A start that cannot happen is skipped and logged, never retried:** when the process already runs
  for the record, or when a `when` or the `startCondition` is false or fails. The write or the
  raising step still commits.
- **Triggers come from the release active when they fire,** run as `system` without policy checks,
  and are named in the `process.started` audit record.

The detail is in [events and triggers](../reference/integration-resources.md#events-and-triggers) and
[process events and triggers](../reference/process-events.md).

## Consequences

- The one-running-instance rule is the guard against loops, so data change triggers can fire on
  system writes and let processes chain.
- A broken trigger never blocks a user's save or fails a running process, as a failing policy filter
  counts as false in [D24](d24-roles-and-policies.md).
- A start that was skipped never happens later, so a process never starts long after its cause.
