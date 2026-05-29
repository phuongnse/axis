# Use case — Retry a failed execution

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Retry a failed execution from the point of failure so that I don't have to re-run steps that already succeeded.

## Primary actor

- Organization Member with `execution:retry`

## Trigger

- User initiates: retry a failed execution from the point of failure

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow execution fails at a step, users can manually retry from the failed step. Previously successful steps are not re-run; their outputs are carried forward from the original execution.

## Acceptance Criteria

*Happy path*
- [ ] "Retry" button appears on the execution detail page when status is `FAILED`.
- [ ] Clicking Retry creates a new Execution record (status: `PENDING`) linked to the original via a `retry_of_execution_id` field.
- [ ] The retry loads the context snapshot from just before the failed step, skips all previously completed steps, and re-runs from the failed step onward.
- [ ] If the retry succeeds, it is marked `COMPLETED`. If it fails again, it is marked `FAILED` and can be retried again.

*Validation & errors*
- [ ] Retrying an execution with status other than `FAILED` is blocked: "'Retry' is only available for failed executions."
- [ ] Retrying an execution whose workflow definition has been archived since the original run shows a warning: "The workflow has been archived. The retry will use the last active version." The retry proceeds with the archived definition.
- [ ] A user without `execution:retry` permission does not see the Retry button and gets HTTP 403 from the API.

*Edge cases*
- [ ] If the failed step's configuration was changed in the workflow builder since the original execution, the retry uses the updated step config (not the original). A warning is shown: "The workflow definition has changed since this execution. The retry may behave differently."
- [ ] Retrying a workflow where the failed step referenced a form that has since been deleted: the retry fails immediately at that step with "Referenced form no longer exists."
- [ ] Multiple concurrent retries of the same execution are prevented: the Retry button is disabled while a retry is already in progress.

*Out of scope*
- Automatic retry (without user action).

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
> **Gaps vs spec:** `POST /api/executions/{id}/retry` and retry-with-context ✅. Retry UI and archived-definition warning pending Frontend.
>
> **Decisions:**
> - `CreateRetry()` produces a new `WorkflowExecution` with `RetryOfExecutionId` set
> - context is copied from original at time of retry.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
