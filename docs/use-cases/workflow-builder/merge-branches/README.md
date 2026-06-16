# Use case — Merge branches back to a single path

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Diverged branches to merge back to a single step so that the workflow continues on a unified path after branching.

## Primary actor

- Team account Member

## Trigger

- Diverged branches to merge back to a single step.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflows can take different execution paths based on data values using Condition steps. This enables if/else and multi-branch switch logic.

## Acceptance Criteria

*Happy path*
- [ ] Multiple incoming edges on a single step node are allowed and visually shown as converging arrows.
- [ ] The merge step executes as soon as any one incoming branch reaches it (OR-merge semantics by default for simple branching).
- [ ] Context from the branch that reached the merge step is carried forward; context from branches that were not taken is not present.

*Validation & errors*
- [ ] A step that is a merge point (multiple incoming edges) and also has its own complex config (e.g., HTTP Request) works normally — there is no restriction on which step types can act as merge points.

*Edge cases*
- [ ] If both branches of an if/else reach the merge point (e.g., both run a Notification step then merge), the merge step executes exactly once (the second arrival is ignored). This is the expected behavior and is documented in the execution history.
- [ ] This OR-merge behavior is distinct from the Parallel Group fan-in (AND-join) behavior described in [parallel execution](./README.md).

*Out of scope*
- Explicit merge/join nodes on the canvas — merging is implicit (any step with multiple incoming edges acts as a merge point). An explicit Join node is used only in Parallel Groups ([parallel execution](./README.md)).

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
> **Gaps vs spec:** OR-merge deduplication (execute-once on first arrival) is an execution engine concern — pending workflow-engine.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

