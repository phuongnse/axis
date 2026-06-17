# Workflow Builder

> **Navigation**: [← Use Cases](../README.md) · [← docs/README.md](../../README.md)

---

## Overview

Provide a visual drag-and-drop canvas where users can design workflows composed of typed steps connected by transitions. Workflows can branch conditionally, run steps in parallel, and be triggered by multiple trigger types. Definitions can be imported and exported as JSON.

## Business Value

The workflow builder is the heart of the platform. It is what differentiates Axis from a simple CRUD data tool.

## Use Cases

### Workflow definitions

| Use case | Summary |
|---|---|
| [Archive a workflow](archive-workflow/) | Archive a workflow so that it is disabled but its history is preserved. |
| [Create a workflow](create-workflow/) | Create a new workflow so that I can start designing an automated process. |
| [Delete a Draft workflow](delete-draft/) | Delete a Draft workflow so that I can permanently remove workflows I no longer need without having to publish them… |
| [Duplicate a workflow](duplicate-workflow/) | Duplicate an existing workflow so that I can use it as a starting point for a similar process. |
| [View workflows list](list-workflows/) | See all workflows so that I can find and manage them. |
| [Publish a workflow](publish-workflow/) | Publish a workflow so that it can be triggered and executed. |

### Canvas

| Use case | Summary |
|---|---|
| [Add a step to the canvas](add-canvas-step/) | Add a step to the workflow canvas so that I can build my process visually. |
| [Navigate and zoom the canvas](canvas-nav/) | Pan and zoom the workflow canvas so that I can work comfortably with large workflows. |
| [Undo and redo canvas actions](canvas-undo/) | Undo and redo changes on the canvas so that I can recover from mistakes easily. |
| [Connect steps with transitions](connect-steps/) | Draw connections between steps so that the workflow knows the execution order. |
| [Configure a step via side panel](step-side-panel/) | Click a step to open its configuration panel so that I can set it up without leaving the canvas. |

### Step types

| Use case | Summary |
|---|---|
| [Configure a Condition step](condition-step/) | Add a Condition step so that my workflow can take different paths based on data values. |
| [Configure a Form step](form-step/) | Configure a Form step with a specific form and assignee so that the right person receives the form during execution. |
| [Configure an HTTP Request step](http-step/) | Configure an HTTP Request step so that my workflow can integrate with external services. |
| [Configure a Notification step](notification-step/) | Add a Notification step so that stakeholders are informed when a workflow reaches a certain point. |
| [Configure a Script step](script-step/) | Write a small script step so that I can transform data that isn't possible with standard steps. |

### Triggers

| Use case | Summary |
|---|---|
| [Configure an Event trigger](event-trigger/) | Trigger a workflow automatically when a platform event occurs so that I don't need to start it manually. |
| [Configure a Manual trigger](manual-trigger/) | Configure a Manual trigger so that authorized users can start the workflow on demand. |
| [Configure a Schedule trigger](schedule-trigger/) | Schedule a workflow so that it runs automatically at defined intervals. |
| [Configure a Webhook trigger](webhook-trigger/) | Configure a webhook trigger so that an external system can start my workflow by sending an HTTP request. |

### Branching

| Use case | Summary |
|---|---|
| [Add an if/else branch](if-else-branch/) | Route my workflow down different paths based on a condition so that different scenarios are handled appropriately. |
| [Merge branches back to a single path](merge-branches/) | Diverged branches to merge back to a single step so that the workflow continues on a unified path after branching. |
| [Add a multi-branch condition](multi-branch/) | Add more than two branches from a Condition step so that I can handle multiple distinct cases. |

### Parallel execution

| Use case | Summary |
|---|---|
| [Configure fan-in (join) behavior](fan-in-join/) | Configure how the workflow continues after parallel steps complete so that I can handle different completion scenarios. |
| [Create a parallel step group](parallel-group/) | Configure multiple steps to run in parallel so that independent tasks don't block each other. |
| [Access results from parallel branches](parallel-results/) | Use the output of all parallel steps in subsequent steps so that I can combine results. |

### Import & export

| Use case | Summary |
|---|---|
| [Bulk export all workflows](bulk-export/) | Export all workflows as a ZIP archive so that I have a complete backup. |
| [Export a workflow as JSON](export-json/) | Export a workflow as a JSON file so that I can back it up or share it with another team. |
| [Import a workflow from JSON](import-json/) | Import a workflow from a JSON file so that I can quickly set up a workflow that someone else designed. |



---

## Diagrams

See [Workflow model](./create-workflow/README.md#workflow-model) (Mermaid).

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

## Acceptance Criteria (domain)

- [ ] Users can create a workflow with at least 3 connected steps on the visual canvas.
- [ ] All 5 step types can be added and configured without errors.
- [ ] All 4 trigger types can be configured and activated.
- [ ] A workflow with an if/else condition correctly routes to the appropriate branch.
- [ ] Parallel steps fan out and the workflow waits for all to complete before continuing.
- [ ] A workflow exported as JSON can be imported into another Workspace and runs correctly.

---

## Code style

Repo-wide C# conventions (explicit types, naming, Allman braces) are enforced via [`.editorconfig`](../../../.editorconfig). Run `dotnet format Axis.sln` before review ([CONTRIBUTING.md](../../../CONTRIBUTING.md)).

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `WorkflowDefinition`, `Step`, `Trigger` aggregates; all step types and domain events; ConfigureStep method; AddTrigger duplicate-type guard |
| Application | ✅ Done | All 15 handlers: CreateWorkflow, PublishWorkflow, ArchiveWorkflow, UnarchiveWorkflow, UpdateWorkflow, DuplicateWorkflow, AddStep, RemoveStep, ConfigureStep, AddTransition, RemoveTransition, AddTrigger, RemoveTrigger, ImportWorkflow, BulkExportWorkflows; GetWorkflows, GetWorkflow, ExportWorkflow queries |
| Infrastructure | ✅ Done | WorkflowBuilderDbContext, EF Core configuration (WorkflowDefinition with steps/transitions/triggers as JSONB), WorkflowRepository, integration tests (Testcontainers). `workflow_form_references` + `workflow_model_references` read models with `IWorkflowReferenceSync`; `ModelDeletedHandler` + `FormDeletedHandler` (Kafka); `WorkflowFormReferenceService` gRPC (server derives `workspace_id` from the caller's JWT `workspace_id` claim). Migration `AddWorkflowReferenceReadModels`. DbContext + UnitOfWork inlined per ADR-017. `WorkspaceVerifiedHandler` provisions workspace schema via `WorkspaceModuleProvisionAttempt` (reports `WorkspaceModuleProvisionReportEvent` to Identity; retries via `RetryWorkspaceModuleProvisionHandler` + shared `WorkspaceSchemaProvisioner`, workspace provisioning use case). Avro lifecycle publish ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). Publish blocked when broken refs; `GetWorkflow` returns `isBroken` on steps/triggers. |
| API | ✅ Done | 18 endpoints: workflow CRUD + publish/archive/unarchive/duplicate, step/transition/trigger management, JSON export, JSON import, ZIP bulk export. Create/duplicate/import/add-step return the shared `CreatedResponse` DTO (no anonymous `object`) |
| Frontend | ⏳ Pending | — |

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend** | ⚠️ mostly ✅ | CRUD/publish/import/export ✅; plan limits 402 ✅ (platform-foundation subscription plans). **Engine-owned:** triggers (cron/webhook/event), step execution, parallel/join — tracked in [workflow-engine](../workflow-engine/README.md#open-work-agents). **API polish:** list filters (last execution date), import transactional rollback — [import-json](./import-json/), [export-json](./export-json/). |
| **Frontend** | ⏳ | Visual canvas, step config panels, trigger UI — every Workspace-management–import-export US. |

Do not re-implement plan limits here; update stale “pending platform-foundation subscription plans” lines if you see them in feature callouts.

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)
- [Identity & Access](../identity-access/README.md)
- [Data Modeling](../data-modeling/README.md)
- [Form Builder](../form-builder/README.md) *(for Form step type)*

## Dependents

- [Workflow Engine](../workflow-engine/README.md)
