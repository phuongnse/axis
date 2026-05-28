# Use case — View retry history

> **Navigation**: [← Workflow Engine](./README.md)

## Purpose

see the retry history of a failed execution so that I can track how many times it has been retried.

## Primary actor

- Organization Member with `execution:read`

## Trigger

- User initiates: see the retry history of a failed execution

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

When a workflow execution fails at a step, users can manually retry from the failed step. Previously successful steps are not re-run; their outputs are carried forward from the original execution.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



**Acceptance Criteria:**

*Happy path*
- [ ] Execution detail page shows a "Retry history" section listing all retries in chronological order.
- [ ] Each entry shows: attempt number, status, started at, completed at, triggered by.
- [ ] Each entry is a link to that retry's own execution detail page.
- [ ] The original execution and all its retries are interlinked (each shows its parent and children).

*Validation & errors*
- [ ] If the retry history fails to load, it shows an error state in the section (not a full page error).

*Edge cases*
- [ ] A retry that was itself retried creates a chain: Original → Retry 1 → Retry 2 → ... All are shown in the retry history of the original execution.
- [ ] There is no maximum retry count imposed by the platform; the user can retry as many times as needed.

*Out of scope*
- Comparing two retry attempts side-by-side — not in MVP.

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
> **Gaps vs spec:** `GET /api/executions/{id}/retry-history` ✅. Retry history UI and interlinked execution chain navigation pending Frontend.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| execution-detail | [source](./wireframes/execution-detail.excalidraw) | [preview](./wireframes/execution-detail.svg) |

[← Back to Workflow Engine](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
