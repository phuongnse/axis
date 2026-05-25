# E04 — Workflow Builder

[← Back to Epics](../README.md)

---

## Overview

Provide a visual drag-and-drop canvas where users can design workflows composed of typed steps connected by transitions. Workflows can branch conditionally, run steps in parallel, and be triggered by multiple trigger types. Definitions can be imported and exported as JSON.

## Business Value

The workflow builder is the heart of the platform. It is what differentiates Axis from a simple CRUD data tool.

## Phase

**MVP**

---

## Features

| ID | Feature | Description |
|---|---|---|
| [F01](./features/F01-workflow-definition.md) | Workflow Definition Management | CRUD operations on workflow definitions |
| [F02](./features/F02-visual-canvas.md) | Visual Workflow Canvas | Drag & drop canvas powered by React Flow |
| [F03](./features/F03-step-types.md) | Step Type Configuration | Configure Form, HTTP Request, Condition, Script, Notification steps |
| [F04](./features/F04-triggers.md) | Trigger Configuration | Manual, Schedule (cron), Webhook, Event triggers |
| [F05](./features/F05-branching.md) | Branching & Conditional Logic | If/else conditions, switch, dynamic routing |
| [F06](./features/F06-parallel-execution.md) | Parallel Step Execution | Fan-out and fan-in parallel step groups |
| [F07](./features/F07-import-export.md) | Workflow Import / Export | Export workflow as JSON, import from JSON file |

---

## Diagrams

![Workflow Data Model](./diagrams/workflow-model.svg)

---

## Step Types

| Type | Description | Config |
|---|---|---|
| **Form** | Pauses workflow and presents a form for user input | Form definition reference, assignee |
| **HTTP Request** | Calls an external REST API | URL, method, headers, body, auth, output mapping |
| **Condition** | Evaluates an expression, routes to different branches | Expression (field comparisons, logical operators) |
| **Script** | Runs a sandboxed JavaScript snippet | Script body, input context, output variable |
| **Notification** | Sends an email or webhook notification | Template, recipient, channel |

## Trigger Types

| Type | Description |
|---|---|
| **Manual** | User starts the workflow explicitly via UI or API |
| **Schedule** | Cron expression — runs at defined intervals |
| **Webhook** | An incoming HTTP POST to a unique URL starts the workflow |
| **Event** | A platform event (e.g., record created, form submitted) starts the workflow |

---

## Acceptance Criteria (Epic Level)

- [ ] Users can create a workflow with at least 3 connected steps on the visual canvas.
- [ ] All 5 step types can be added and configured without errors.
- [ ] All 4 trigger types can be configured and activated.
- [ ] A workflow with an if/else condition correctly routes to the appropriate branch.
- [ ] Parallel steps fan out and the workflow waits for all to complete before continuing.
- [ ] A workflow exported as JSON can be imported into another organization and runs correctly.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `WorkflowDefinition`, `Step`, `Trigger` aggregates; all step types and domain events; ConfigureStep method; AddTrigger duplicate-type guard |
| Application | ✅ Done | All 15 handlers: CreateWorkflow, PublishWorkflow, ArchiveWorkflow, UnarchiveWorkflow, UpdateWorkflow, DuplicateWorkflow, AddStep, RemoveStep, ConfigureStep, AddTransition, RemoveTransition, AddTrigger, RemoveTrigger, ImportWorkflow, BulkExportWorkflows; GetWorkflows, GetWorkflow, ExportWorkflow queries |
| Infrastructure | ✅ Done | WorkflowBuilderDbContext, EF Core configuration (WorkflowDefinition with steps/transitions/triggers as JSONB), WorkflowRepository, 7 integration tests (Testcontainers). Schema managed via EF Core migrations (e.g. `AddUniqueConstraintOnWorkflowName`). DbContext + UnitOfWork inlined per ADR-017. `OrganizationVerifiedHandler` provisions tenant schema via `TenantModuleProvisionAttempt` (reports `TenantModuleProvisionReportEvent` to Identity; retries via `RetryTenantModuleProvisionHandler` + shared `TenantSchemaProvisioner`, E01 US-003). Cross-module lifecycle events publish via `Axis.WorkflowBuilder.Contracts` Avro + Kafka ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). |
| API | ✅ Done | 18 endpoints: workflow CRUD + publish/archive/unarchive/duplicate, step/transition/trigger management, JSON export, JSON import, ZIP bulk export |
| Frontend | ⏳ Pending | — |

---

## Dependencies

- [E01 — Platform Foundation](../E01-platform-foundation/README.md)
- [E02 — Identity & Access Management](../E02-identity-access/README.md)
- [E03 — Data Modeling](../E03-data-modeling/README.md)
- [E05 — Form Builder](../E05-form-builder/README.md) *(for Form step type)*

## Dependents

- [E06 — Workflow Execution Engine](../E06-workflow-engine/README.md)
