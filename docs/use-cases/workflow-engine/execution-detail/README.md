# Use case — View execution detail and step timeline

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See the full detail of a specific execution so that I can understand exactly what happened at each step.

## Primary actor

- Organization Member with `execution:read`

## Trigger

- User initiates: see the full detail of a specific execution

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Every workflow execution and each of its steps is recorded in full detail. Users can browse execution history, filter by status, and inspect the complete context and output of any past execution.

## Acceptance Criteria

*Happy path*
- [ ] Execution detail page shows: execution ID, status, total duration, input payload, trigger info, and created at.
- [ ] Step timeline shows all steps in execution order with: name, type icon, status, start time, end time, duration.
- [ ] Expanding a step shows: input (context snapshot before the step ran), output (data the step wrote to context), and error details (if failed).
- [ ] For Parallel Groups: the group is shown as a collapsible container with all parallel steps inside.

*Validation & errors*
- [ ] If a step's context snapshot is larger than 1 MB (e.g., large API response stored in context), it is shown truncated with a "Download full context" link.

*Edge cases*
- [ ] A step in `SKIPPED` status (branching condition not taken) shows with a neutral icon and "Skipped — branch not taken" as the reason.
- [ ] A step in `WAITING` status (Form step pending) shows "Waiting for: {assignee}" with a timestamp of when it was assigned.
- [ ] Context snapshots are immutable after being recorded — they reflect the exact state at that point in time, regardless of subsequent context changes.

*Out of scope*
- Replaying or simulating an execution from any point with a different context.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** Step timeline UI, context snapshot display, and parallel group rendering pending Frontend.
>
> **Done:** `GET /api/executions/{id}` returns execution + steps.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

