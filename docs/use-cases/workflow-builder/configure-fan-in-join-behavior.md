# Use case — Configure fan-in (join) behavior

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

Configure how the workflow continues after parallel steps complete so that I can handle different completion scenarios.

## Primary actor

- Organization Member

## Trigger

- Configure how the workflow continues after parallel steps complete.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

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
- "Wait for N of M" join type (e.g., wait for 2 out of 3 branches) — not in MVP.

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](./wireframes/workflow-editor.excalidraw) | [preview](./wireframes/workflow-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
