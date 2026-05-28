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

| Use case | Description |
|---|---|---|
| [Execution Management](execution-management.md) | Create, track, and terminate workflow executions |
| [Step Execution Handlers](step-handlers.md) | Dedicated handler per step type (Form, HTTP, Condition, Script, Notification) |
| [Error Handling & Notification](error-handling.md) | Detect failures, notify configured channels, mark execution as failed |
| [Execution History & Audit Log](execution-history.md) | Full history of executions, step results, and context data |
| [Manual Retry](manual-retry.md) | Resume a failed execution from the failed step |
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
| **Backend — high** | ⚠️ | [execution-management](execution-management.md): schedule/webhook/event triggers, stale-PENDING recovery. [error-handling](error-handling.md): notification dispatch, `GetExecution` error detail, channel config. [step-handlers](step-handlers.md): real `IScriptExecutor` / `INotificationSender` (stubs today). |
| **Backend — medium** | ⚠️ | [execution-history](execution-history.md): date/trigger filters, CSV export, role-scoped list. Cancel: abandon Wolverine jobs + cancel form tasks. **[Organization deletion](../platform-foundation/organization-management.md):** `OrganizationExecutionCanceller` cancels Pending/Running executions before org hard-delete (`FixedTenantContext`). |
| **Frontend** | ⏳ | Execution monitor, retry UI, SignalR live updates — see [execution-management](execution-management.md) and related use-case callouts. |

Start here when workflow-builder “pending workflow-engine” items block runtime behavior; feature callouts list exact use-case gaps.

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)
- [Identity & Access](../identity-access/README.md)
- [Workflow Builder](../workflow-builder/README.md)
- [Form Builder](../form-builder/README.md)

## Dependents

- [Page Builder](../page-builder/README.md) *(display execution data on pages)*
