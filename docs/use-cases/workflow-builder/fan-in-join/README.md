# Use case — Configure fan-in (join) behavior

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure how the workflow continues after parallel steps complete so that I can handle different completion scenarios.

## Primary actor

- Workspace Member

## Trigger

- Configure how the workflow continues after parallel steps complete.

## Main flow

1. Member opens the Parallel Group configuration and chooses the join behavior.
2. System records the selected AND or OR join strategy and validates branch behavior for publish.
3. Member sees the join type label on the group's output handle and the workflow continues according to that strategy at execution time.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Multiple steps can run concurrently inside a Parallel Group. The workflow fans out, runs them in parallel, and waits for all (or any) to complete before continuing.

## Acceptance Criteria

*Happy path*
- [ ] The Parallel Group config panel has a "Join type" selector:
  - **Wait for all (AND)** — default; continues when all branches complete.
  - **Wait for first (OR)** — continues when any one branch completes; remaining branches are cancelled.
- [ ] The selected join type is shown as a label on the group's output handle on the canvas.

*Validation & errors*
- [ ] If join type is AND and any branch fails, the entire Parallel Group is marked as Failed immediately; remaining running branches are cancelled.
- [ ] If join type is OR and the first branch fails (before any other branch succeeds), the group continues waiting for another branch. If all branches fail, the group fails.

*Edge cases*
- [ ] With OR join, a branch that is still running when the group completes receives a cancellation signal. Long-running operations (e.g., HTTP requests) are given a 5-second grace period before being forcibly terminated.
- [ ] Changing the join type after the workflow is published requires creating a new version.

*Out of scope*
- "Wait for N of M" join type (e.g., wait for 2 out of 3 branches).

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
> **Gaps vs spec:** AND/OR join execution, branch cancellation, and grace period pending workflow-engine.
>
> **Deferred follow-ups:** "Wait for N of M" join type (e.g., wait for 2 of 3 branches).
>
> **Decisions:** Initial join semantics stay binary (`all`/`any`) so execution can be made correct before adding quorum joins.
>
