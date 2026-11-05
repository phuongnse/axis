# D69. Timer, wait for event and sub-process steps, loops and create record

Status: **Agreed**

## Context

A process of [D19](d19-process-resource-and-worker-host.md) waits only at a task and may not loop. Real
processes also wait for a time or an event, hand work to another process, go round again when work
comes back, as a reopened ticket of [D56](d56-a-service-desk-as-the-third-sample-application.md) does,
and create records. A waiting instance must survive restarts, as
[execution](../domain/knowledge.md#execution) asks.

## Decision

- **Waiting is a step.** A `timer` step waits for a duration or until a time, and a `waitEvent` step
  waits for an event about the instance's own subject, with an optional timeout. The instance stays
  `waiting` in the database, as at a task, and only the first resume at its revision counts.
- **An occurrence wakes a wait once.** A wait takes the earliest unused occurrence about its subject
  raised since the instance started, and event delivery wakes it
  ([D72](d72-a-delivery-limit-and-dead-letters.md)).
- **A sub-process runs on its parent's release,** and the parent waits until it ends. Processes may not
  call each other in a cycle.
- **A loop passes through a waiting step:** a `task`, a `timer` or a `waitEvent`.
- **A `createRecord` operation goes through the record command,** as `system` in the step's
  transaction, with every rule of a create.

The detail is in [process flow](../reference/process-flow.md).

## Consequences

- A long wait costs a row and a work item, not a thread, and a loop cannot spin.
- A call step in a loop stages a call on every pass, so a call is known by its step and its pass.
- A child keeps the contract the compiler checked, even after a newer release is activated.

## Alternatives

- **Keep the ban on cycles.** A reopened ticket would need a new instance.
- **Run a child on the active release.** A new release could break the contract its parent was
  compiled against.
