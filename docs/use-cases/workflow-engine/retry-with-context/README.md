# Use case — Retry with modified input context

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Modify the execution context before retrying so that I can fix data errors that caused the original failure.

## Primary actor

- Workspace Member with `execution:retry`

## Trigger

- User initiates: modify the execution context before retrying

## Main flow

1. Actor starts the — Retry with modified input context flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow execution fails at a step, users can manually retry from the failed step. Previously successful steps are not re-run; their outputs are carried forward from the original execution.

## Acceptance Criteria

*Happy path*
- [ ] "Retry with modified context" option (secondary action next to Retry) opens a JSON editor pre-populated with the execution context at the point of failure.
- [ ] The user edits the JSON and clicks "Start retry."
- [ ] The retry uses the modified context starting from the failed step.
- [ ] The execution history records that the retry used a modified context (a "Context modified by user" flag in the execution detail).

*Validation & errors*
- [ ] The context editor validates that the JSON is valid before the retry can be started; invalid JSON shows: "Context must be valid JSON."
- [ ] Removing a context key that is required by the failed step is allowed — the retry may fail again with a different error, which is the user's responsibility.

*Edge cases*
- [ ] The modified context is not restricted to keys relevant to the failed step; the user can modify any key. The full modified context is used for all remaining steps.
- [ ] A very large context (> 1 MB) may be slow to render in the JSON editor; the editor handles up to 5 MB.

*Out of scope*
- Structured field-by-field editing of context (showing fields by step/variable name) — raw JSON editor only.

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
> **Gaps vs spec:** `POST /api/executions/{id}/retry-with-context` ✅. JSON context editor UI and modified-context flag pending Frontend.
>
> **Decisions:** `CreateRetryWithModifiedContext` added to domain as private `CreateRetryCore` delegation — shares validation logic with `CreateRetry`.
>
> **Deferred follow-ups:**
> - N/A

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

