# Workflow Execution Engine

[← Back to Use Cases](../README.md)

---

## Overview

The runtime engine that takes a workflow definition and executes it step-by-step. Manages execution state, handles all step types, deals with errors, persists execution history, and notifies users of failures. Users can manually retry failed executions.

## Business Value

A workflow builder without an execution engine is just a drawing tool. This domain is what makes workflows actually run and deliver value.

## Phase

**MVP**

---

## Use Cases

| Use case | Summary |
|---|---|
| [Cancel a running execution](cancel-a-running-execution.md) | cancel a running execution so that I can stop a process that is no longer needed. |
| [Configure error notification channels per workflow](configure-error-notification-channels-per-workflow.md) | configure who gets notified when my workflow fails so that the right people are alerted. |
| [Receive error notification when a workflow fails](receive-error-notification-when-a-workflow-fails.md) | be notified when a workflow execution fails so that I can investigate and take action. |
| [Retry a failed execution](retry-a-failed-execution.md) | retry a failed execution from the point of failure so that I don't have to re-run steps that already succeeded. |
| [Retry with modified input context](retry-with-modified-input-context.md) | modify the execution context before retrying so that I can fix data errors that caused the original failure. |
| [Start a workflow execution](start-a-workflow-execution.md) | start a workflow execution so that the defined process begins running. |
| [Step execution is isolated and resilient](step-execution-is-isolated-and-resilient.md) | Run each step handler in isolation so that a failure in one step does not crash the engine or affect other executions. |
| [# Use Case Group — Step Execution Handlers](step-handlers.md) | # Use Case Group — Step Execution Handlers |
| [Track execution status in real time](track-execution-status-in-real-time.md) | see the live status of a running execution so that I know where it is in the process. |
| [View detailed error information](view-detailed-error-information.md) | see the full error details of a failed step so that I can understand what went wrong. |
| [View execution detail and step timeline](view-execution-detail-and-step-timeline.md) | see the full detail of a specific execution so that I can understand exactly what happened at each step. |
| [View execution history for a workflow](view-execution-history-for-a-workflow.md) | see the execution history for a specific workflow so that I can monitor its performance and identify patterns. |
| [View org-wide execution history](view-org-wide-execution-history.md) | see all executions across all workflows so that I have a global overview of automation activity. |
| [View retry history](view-retry-history.md) | see the retry history of a failed execution so that I can track how many times it has been retried. |


---

## Diagrams

![Execution Flow](./diagrams/execution-flow.svg)

---

## Execution States

```
PENDING → RUNNING → COMPLETED
                 ↘ FAILED
                 ↘ CANCELLED
```

## Step States

```
PENDING → RUNNING → COMPLETED
                 ↘ FAILED
                 ↘ SKIPPED   (conditional branch not taken)
                 ↘ WAITING   (Form step — waiting for human input)
```

---

## Execution Context

Each execution carries a **context object** — a key/value map that accumulates data as steps complete:

- Input variables (from trigger payload)
- Form submission data
- HTTP response data
- Script output variables

Subsequent steps can reference context values using expressions like `{{context.step_id.field_name}}`.

---

## Acceptance Criteria (domain)

- [ ] A manually triggered workflow starts and completes all steps in order.
- [ ] A scheduled workflow fires at the correct cron time (±30 seconds).
- [ ] An incoming webhook payload correctly starts a configured workflow.
- [ ] A failed HTTP step marks the execution as Failed and sends an error notification.
- [ ] Execution history shows each step's start time, end time, status, and output.
- [ ] A failed execution can be retried from the failed step and completes successfully.
- [ ] Real-time status updates are pushed to the UI via SignalR during execution.

---

## Code style

Repo-wide C# conventions (explicit types, naming, Allman braces) are enforced via [`.editorconfig`](../../../.editorconfig). Run `dotnet format Axis.sln` before push ([CONTRIBUTING.md](../../../CONTRIBUTING.md)).

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `WorkflowExecution` aggregate + `ExecutionStep` entity; full execution state machine; `WorkflowSnapshot` local read model; domain events (ExecutionStarted, Completed, Failed, Cancelled, StepCompleted, StepFailed, FormStepReached) |
| Application | ✅ Done | All commands/queries (StartExecution, Cancel, Retry, RetryWithContext, GetExecution, GetAllExecutions, GetExecutionsByWorkflow, GetRetryHistory); step handler messages and orchestrator (ExecuteNextStepHandler, StepCompletedHandler, StepFailedHandler, per-step handlers); ConditionEvaluator; IStepDispatcher / IHttpStepExecutor / IScriptExecutor / INotificationSender interfaces |
| Infrastructure | ⚠️ Partial | Database `axis_workflowengine` ([ADR-011](../../TECH_STACK.md#adr-011-per-module-database-with-schema-per-tenant-inside)); EF migrations through `AddWorkflowSnapshot` + snapshot sync migration; tests/fixtures use `MigrateAsync` ([ADR-023](../../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)). Repositories + step executors. `WorkflowEngineEventMapper` translates domain events to Avro at `SaveChangesAsync` and publishes via outbox → Kafka ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). `FormTaskSubmittedHandler` + `FormTaskExpiredHandler` consume FormBuilder's `FormTaskSubmittedEvent` / `FormTaskExpiredEvent` from Kafka (Contracts only — no Domain reference). Workflow snapshot/active-status handlers consume `Axis.WorkflowBuilder.Contracts` Kafka events. `OrganizationVerifiedHandler` provisions tenant schema via `TenantModuleProvisionAttempt` (reports `TenantModuleProvisionReportEvent` to Identity; retries via `RetryTenantModuleProvisionHandler` + shared `TenantSchemaProvisioner`, tenant provisioning use case). **Deferred (PR follow-up — workflow-engine organization-management step handlers):** real `IScriptExecutor` and `INotificationSender` — currently stubs. |
| Contracts | ✅ Done | `Axis.WorkflowEngine.Contracts` — Avro schema `FormStepReachedEvent` (the cross-module event FormBuilder reacts to). Hand-written `ISpecificRecord` generated code + `WorkflowEngineKafkaTopics` + `WorkflowEngineEventExtensions` (typed GUID accessors). |
| API | ✅ Done | `ExecutionEndpoints`: list, detail, start, cancel, retry, retry-with-context, retry history. Default-input shaping moved into `StartExecutionHandler`. Form task routes live under form-builder `FormTaskEndpoints` |
| Frontend | ⏳ Pending | — |

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend — high** | ⚠️ | [execution-management](./README.md): schedule/webhook/event triggers, stale-PENDING recovery. [error-handling](./README.md): notification dispatch, `GetExecution` error detail, channel config. [step-handlers](step-handlers.md): real `IScriptExecutor` / `INotificationSender` (stubs today). |
| **Backend — medium** | ⚠️ | [execution-history](./README.md): date/trigger filters, CSV export, role-scoped list. Cancel: abandon Wolverine jobs + cancel form tasks. **[Organization deletion](../platform-foundation/README.md):** `OrganizationExecutionCanceller` cancels Pending/Running executions before org hard-delete (`FixedTenantContext`). |
| **Frontend** | ⏳ | Execution monitor, retry UI, SignalR live updates — see [execution-management](./README.md) and related use-case callouts. |

Start here when workflow-builder “pending workflow-engine” items block runtime behavior; feature callouts list exact use-case gaps.

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)
- [Identity & Access](../identity-access/README.md)
- [Workflow Builder](../workflow-builder/README.md)
- [Form Builder](../form-builder/README.md)

## Dependents

- [Page Builder](../page-builder/README.md) *(display execution data on pages)*
