# Use case — Create a parallel step group

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure multiple steps to run in parallel so that independent tasks don't block each other.

## Primary actor

- Workspace Member

## Trigger

- Configure multiple steps to run in parallel.

## Main flow

1. Actor starts the — Create a parallel step group flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Multiple steps can run concurrently inside a Parallel Group. The workflow fans out, runs them in parallel, and waits for all (or any) to complete before continuing.

## Acceptance Criteria

*Happy path*
- [ ] "Add Parallel Group" option in the step type sidebar adds a special container node to the canvas.
- [ ] Steps are added inside the Parallel Group by dragging from the sidebar into the group container.
- [ ] The canvas shows the group as a bordered region with a clear visual distinction from sequential steps.
- [ ] Connections enter the group at the group's input handle and exit at the group's output handle.

*Validation & errors*
- [ ] A Parallel Group with fewer than 2 steps blocks publishing: "A Parallel Group must contain at least 2 steps."
- [ ] A Condition step inside a Parallel Group is allowed; branching within a parallel branch is valid.
- [ ] Steps outside the Parallel Group cannot be connected to steps inside it (bypassing the group boundary is not allowed).

*Edge cases*
- [ ] Nested Parallel Groups (a Parallel Group inside another Parallel Group) are not supported; the canvas blocks this configuration.
- [ ] A Form step inside a Parallel Group is valid; the group waits for all form submissions before completing (with AND join).

*Out of scope*
- Dynamic parallelism (creating N parallel branches based on a list of records at runtime).

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
> **Gaps vs spec:**
> - canvas container node rendering and step nesting UI pending Frontend
> - parallel group represented via step config in existing JSONB storage. `ParallelGroup` and `JoinType` are not yet implemented — shown as planned (dashed) in diagram.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
