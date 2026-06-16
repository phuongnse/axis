# Use case — View retry history

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See the retry history of a failed execution so that I can track how many times it has been retried.

## Primary actor

- Workspace Member with `execution:read`

## Trigger

- User initiates: see the retry history of a failed execution

## Main flow

1. Actor starts the — View retry history flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow execution fails at a step, users can manually retry from the failed step. Previously successful steps are not re-run; their outputs are carried forward from the original execution.

## Acceptance Criteria

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
- Comparing two retry attempts side-by-side.

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
>
> **Gaps vs spec:**
> - N/A
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

