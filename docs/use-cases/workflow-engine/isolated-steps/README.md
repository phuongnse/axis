# Use case - Step execution is isolated and resilient

> **Navigation**: [<- Workflow Engine](../README.md) . [Use cases index](../README.md#use-cases)

## Purpose

Run each step handler in isolation so that a failure in one step does not crash the engine or affect other executions.

## Primary actor

- Platform operator / workflow engine

## Trigger

- Workflow engine dispatches a step execution message to a type-specific handler.

## Main flow

1. Wolverine dispatches a step handler message for a workflow execution step.
2. The handler loads the execution and target step in the current workspace scope.
3. If the step is already in a terminal state, the handler exits without side effects.
4. The handler records the step as running, captures start time, and executes the type-specific work inside its own exception boundary.
5. On success, the handler records output, end time, and duration, then reports `StepCompleted(executionId, stepId, output)` back to the engine.
6. On failure or timeout, the handler marks only that step as failed, logs structured context, and lets other executions continue.

## Alternate / error flows

- A re-delivered message for a terminal step exits idempotently.
- A duplicate delivery for a running step is rejected by the step row concurrency guard.
- Transient database failures retry up to 3 times with exponential backoff before the step is marked failed.

## Context

Handler specifications (Form, HTTP, Condition, Script, Notification) remain documented in the workflow-engine domain README under **Handler specifications**. This use case covers cross-cutting isolation, logging, timeout, retry, and idempotency behavior.

## Acceptance Criteria

*Happy path*
- [ ] Each step handler runs as an independent Wolverine message handler with its own exception boundary.
- [ ] A step handler completing successfully reports `StepCompleted(executionId, stepId, output)` back to the engine via a Wolverine message.
- [ ] Step start time, end time, and duration are recorded for every step.

*Validation & errors*
- [ ] An unhandled exception in any step handler marks only that step as `Failed` - the engine and all other executions continue normally.
- [ ] All step handler exceptions are logged with structured context: `{ workspaceId, executionId, stepId, stepType, errorType, errorMessage, stackTrace }`.
- [ ] A step handler that takes longer than 5 minutes (engine-level timeout, separate from step-level config) is forcibly killed and the step is marked Failed with: "Step execution exceeded the maximum allowed time."

*Edge cases*
- [ ] A step handler that loses its DB connection mid-execution retries the DB operation up to 3 times with exponential backoff before failing.
- [ ] Wolverine's at-least-once delivery guarantee: if a step handler message is re-delivered (e.g., after a crash), the handler checks if the step is already in a terminal state (`COMPLETED`, `FAILED`, `CANCELLED`) and exits immediately (idempotent).
- [ ] Two concurrent deliveries of the same step handler message (race condition): the second one detects the step is already `RUNNING` and exits; only one execution proceeds.

*Out of scope*
- Custom step types defined by users.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| execution-detail | [source](./execution-detail.excalidraw) | [preview](./execution-detail.svg) |

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
> - AC "concurrent delivery: second detects Running and exits" - concurrent-duplicate protection is implemented via `UseXminAsConcurrencyToken()` on `execution_steps` rows. The second concurrent writer receives a `DbUpdateConcurrencyException` (translated to `ConcurrencyException`), logs, and exits gracefully.
> - Engine-level 5-minute step timeout not yet implemented.
> - `IScriptExecutor` and `INotificationSender` are stubs; real JS sandbox and notification dispatch deferred.
>
> **Deferred follow-ups:**
> - Add the engine-level 5-minute step timeout and failure message.
> - Replace `IScriptExecutor` and `INotificationSender` stubs with real script sandbox and notification dispatch implementations.
> - Revisit the concurrent-running guard if product semantics require an explicit `RUNNING` check in addition to PostgreSQL row concurrency.
>
> **Decisions:** `ExecutionStep.IsTerminal` covers Completed/Failed/Cancelled. Concurrent-duplicate protection uses PostgreSQL `xmin` on owned `execution_steps`; no migration required. Running-guard approach rejected for now because the concurrency token already protects duplicate writers.
