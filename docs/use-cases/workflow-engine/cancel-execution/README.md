# Use case — Cancel a running execution

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Cancel a running execution so that I can stop a process that is no longer needed.

## Primary actor

- Organization Member with `execution:cancel`

## Trigger

- User initiates: cancel a running execution

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

The engine manages the full lifecycle of a workflow execution — from creation through completion, failure, or cancellation. Each execution is a runtime instance of a workflow definition.

## Acceptance Criteria

*Happy path*
- [ ] "Cancel" button appears on the execution detail page when status is `RUNNING` or `WAITING`.
- [ ] Clicking Cancel shows a confirmation dialog: "Are you sure you want to cancel this execution? This cannot be undone."
- [ ] After confirmation, the execution transitions to `CANCELLED` within 10 seconds.
- [ ] Pending Wolverine jobs for cancelled executions are abandoned before they run.
- [ ] Active Form Tasks for the cancelled execution are marked `CANCELLED`; their form links show: "This workflow has been cancelled."

*Validation & errors*
- [ ] Attempting to cancel a `COMPLETED`, `FAILED`, or already `CANCELLED` execution returns HTTP 422: "Cannot cancel an execution with status: {status}."
- [ ] A non-authorized user who calls the cancel API gets HTTP 403.

*Edge cases*
- [ ] Cancelling an execution during a step that is currently executing (e.g., an HTTP Request step in-flight): the step is allowed to finish its current operation, then the engine marks it as Cancelled and stops. Steps do not die mid-operation.
- [ ] Completed steps in a cancelled execution retain their outputs in the execution history.
- [ ] A concurrent cancel request (two users clicking Cancel at the same time) is handled idempotently: only one cancellation takes effect.

*Deferred capabilities*
- Pausing an execution and resuming it. (cancel only).

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
> **Gaps vs spec:** `POST /api/executions/{id}/cancel` ✅. Cancel button UI, Wolverine job abandonment, and Form Task cancellation pending engine.
>
> **Decisions:** `Cancel()` domain guard rejects terminal statuses (`Completed`, `Failed`, `Cancelled`) with `InvalidOperationException`.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
