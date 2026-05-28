# Use case — Step execution is isolated and resilient

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Run each step handler in isolation so that a failure in one step does not crash the engine or affect other executions.

## Primary actor

- Platform operator / workflow engine

## Trigger

- Workflow engine dispatches a step execution message to a type-specific handler.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Handler specifications (Form, HTTP, Condition, Script, Notification) remain documented in the workflow-engine domain README under **Handler specifications** — this use case covers cross-cutting isolation, logging, and idempotency.

## Acceptance Criteria

*Happy path*
- [ ] Each step handler runs as an independent Wolverine message handler with its own exception boundary.
- [ ] A step handler completing successfully reports `StepCompleted(executionId, stepId, output)` back to the engine via a Wolverine message.
- [ ] Step start time, end time, and duration are recorded for every step.

*Validation & errors*
- [ ] An unhandled exception in any step handler marks only that step as `Failed` — the engine and all other executions continue normally.
- [ ] All step handler exceptions are logged with structured context: `{ tenantId, executionId, stepId, stepType, errorType, errorMessage, stackTrace }`.
- [ ] A step handler that takes longer than 5 minutes (engine-level timeout, separate from step-level config) is forcibly killed and the step is marked Failed with: "Step execution exceeded the maximum allowed time."

*Edge cases*
- [ ] A step handler that loses its DB connection mid-execution retries the DB operation up to 3 times with exponential backoff before failing.
- [ ] Wolverine's at-least-once delivery guarantee: if a step handler message is re-delivered (e.g., after a crash), the handler checks if the step is already in a terminal state (`COMPLETED`, `FAILED`, `CANCELLED`) and exits immediately (idempotent).
- [ ] Two concurrent deliveries of the same step handler message (race condition): the second one detects the step is already `RUNNING` and exits; only one execution proceeds.

*Deferred capabilities*
- Custom step types defined by users.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ⚠️ |
> | API | ⏳ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:**
> - AC "concurrent delivery: second detects Running and exits" — concurrent-duplicate protection is implemented via `UseXminAsConcurrencyToken()` on `execution_steps` rows. The second concurrent writer receives a `DbUpdateConcurrencyException` (translated to `ConcurrencyException`), logs, and exits gracefully.
> - Engine-level 5-minute step timeout not yet implemented.
> - `IScriptExecutor` and `INotificationSender` are stubs; real JS sandbox and notification dispatch deferred.
>
> **Decisions:** `ExecutionStep.IsTerminal` covers Completed/Failed/Cancelled. Concurrent-duplicate protection uses PostgreSQL `xmin` on owned `execution_steps` — no migration required. Running-guard approach rejected (see workflow-engine open work).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| execution-detail | [source](./execution-detail.excalidraw) | [preview](./execution-detail.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
