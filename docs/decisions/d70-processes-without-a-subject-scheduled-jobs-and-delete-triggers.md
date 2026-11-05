# D70. Processes without a subject, scheduled jobs and delete triggers

Status: **Agreed**

## Context

Every process of [D26](d26-events-and-triggers.md) runs for one subject record. Reports, reminders and
clean-ups run on a clock and belong to no record, and a deleted record can no longer be a subject, yet
these are among [what applications use most](../domain/knowledge.md#what-applications-use-most).

## Decision

- **A process may have no subject.** It then declares typed `parameters` instead of an `entity`, and
  steps that need a subject record are compile errors in it.
- **One instance at a time.** A second start while one runs is skipped from a trigger and refused
  through the start endpoint.
- **A scheduled job is a schedule trigger without a data source.** It gives the parameters as literal
  values and starts the process once per occurrence.
- **A delete trigger** maps the record as it was before the delete to the parameters, and the start
  commits in the delete's transaction.

The detail is in [processes without a subject](../reference/process-flow.md#processes-without-a-subject),
[scheduled jobs](../reference/process-events.md#scheduled-jobs) and
[delete triggers](../reference/process-events.md#delete-triggers).

## Consequences

- Typed parameters keep these processes checked by the compiler.
- A slow job cannot pile up.
- Inspecting such an instance needs only the grant, because there is no record to read.
