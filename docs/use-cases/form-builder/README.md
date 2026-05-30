# Form Builder

[← Back to Use Cases](../README.md)

---

## Overview

Enable users to design interactive forms that can be embedded as steps within workflows. Forms collect structured data from users, validate it, and store submissions that can flow through the rest of the workflow as context data.

## Business Value

Forms are the primary mechanism for human interaction within a workflow. Without forms, workflows can only be fully automated — with forms, they can support approval processes, data entry, and human-in-the-loop automation.

## Use Cases

### Form definitions

| Use case | Summary |
|---|---|
| [Add a field to a form](add-form-field/) | Add a field to my form so that I can collect the data I need. |
| [Create a form](create-form/) | Create a new form so that I can design a data collection interface. |
| [Delete a form](delete-form/) | Delete a form so that I can clean up unused forms. |
| [Edit a form](edit-form/) | Edit an existing form so that I can update its fields as requirements change. |
| [Configure validation rules on a field](form-field-validation/) | Set validation rules on each field so that users are guided to provide correct data. |
| [View all forms](list-forms/) | See all forms in my organization so that I can find existing forms to reuse. |
| [Reorder fields via drag-and-drop](reorder-form-fields/) | Drag form fields to reorder them so that the form flows naturally. |
| [Add a section divider](section-divider/) | Group related fields under a section heading so that the form is easier to understand. |

### Workflow integration

| Use case | Summary |
|---|---|
| [Link a form to a workflow Form step](link-form-step/) | Select a form when configuring a Form step so that the right form is presented to the assignee during execution. |
| [Map form submission data into workflow context](map-submission-context/) | The data submitted in a form to be available to subsequent steps so that the rest of the process can use it. |
| [Pre-populate form fields from execution context](prepopulate-fields/) | Pre-populate form fields with values from the workflow context so that assignees don't re-enter data that's already… |

### Submission & tasks

| Use case | Summary |
|---|---|
| [Receive form assignment notification](assignment-notify/) | Be notified when a form is waiting for my input so that I know I have an action to take. |
| [Handle form step timeout](form-timeout/) | Configure a timeout on a Form step so that the workflow doesn't wait indefinitely. |
| [View pending form tasks](pending-tasks/) | See a list of all form tasks assigned to me so that I don't miss any pending actions. |
| [Open and submit an assigned form](submit-assigned-form/) | Open the form link and submit my responses so that the workflow can continue. |



---

## Diagrams

See [Form model](./create-form/README.md#form-model) (Mermaid).

---

## Form Field Types

| Type | Description |
|---|---|
| `Text Input` | Single-line text |
| `Textarea` | Multi-line text |
| `Number` | Numeric input |
| `Date Picker` | Date or datetime selection |
| `Dropdown` | Select from a list of options |
| `Checkbox` | Boolean toggle |
| `File Upload` | Attach one or more files |
| `Relation Picker` | Search and select a record from a Model |

---

## Form Lifecycle in a Workflow

```
Workflow reaches Form step
    → Assignee receives notification
        → Assignee opens form URL
            → Submits form
                → Submission stored
                    → Workflow continues with form data in context
```

---

## Acceptance Criteria (domain)

- [ ] Users can create a form with at least 5 different field types.
- [ ] Each field can have required validation, min/max length, and custom error messages.
- [ ] A form can be linked to a Form step in a workflow.
- [ ] When workflow reaches the Form step, the assignee receives a notification with the form link.
- [ ] Submitted form data is available as context variables in subsequent workflow steps.
- [ ] Submitting an invalid form shows inline validation errors without page reload.

---

## Code style

Repo-wide C# conventions (explicit types, naming, Allman braces) are enforced via [`.editorconfig`](../../../.editorconfig). Run `dotnet format Axis.sln` before push ([CONTRIBUTING.md](../../../CONTRIBUTING.md)).

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `FormDefinition`, `FormField`, `FormSubmission` aggregates; field types and form-task domain events |
| Application | ⚠️ Partial | Form definition CRUD + fields; subscription-plans: `SubmitFormByToken`, `GetFormTaskByToken`, `GetMyFormTasks`, `ExpireFormSubmissionHandler`. Notifications and role-based assignee resolution pending |
| Infrastructure | ✅ Done | `form_model_references` read model + `ModelDeletedHandler` (DataModeling Kafka) flags broken Relation Picker fields; delete-model guard via `IFormModelReferenceRepository`. Delete-form guard: `FormWorkflowDeletionGuard` calls WorkflowBuilder gRPC `CountBlockingFormReferences` (server scopes by JWT `org_id`); `FormDeletedEvent` Avro published on delete for WorkflowBuilder `FormDeletedHandler`. Database `axis_formbuilder` ([ADR-011](../../TECH_STACK.md#adr-011-per-module-database-with-schema-per-tenant-inside)); EF `InitialCreate` migration (regenerated via `dotnet ef`); tests/fixtures use `MigrateAsync` ([ADR-023](../../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)). `FormSubmission` + expiry scheduling via Wolverine. DbContext + UnitOfWork inlined per ADR-017. `FormBuilderEventMapper` translates domain events to Avro at `SaveChangesAsync` and publishes via outbox → Kafka ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). `OrganizationVerifiedHandler` provisions tenant schema via `TenantModuleProvisionAttempt` (reports `TenantModuleProvisionReportEvent` to Identity; retries via `RetryTenantModuleProvisionHandler` + shared `TenantSchemaProvisioner`, tenant provisioning use case). `FormStepReachedHandler` consumes WorkflowEngine's `FormStepReachedEvent` from Kafka (Contracts only — no Domain reference). Consumes WorkflowBuilder lifecycle events from `Axis.WorkflowBuilder.Contracts`. |
| Contracts | ✅ Done | `Axis.FormBuilder.Contracts` — Avro schemas `FormTaskSubmittedEvent` + `FormTaskExpiredEvent` (the form-task lifecycle events WorkflowEngine reacts to). Hand-written `ISpecificRecord` generated code + `FormBuilderKafkaTopics` + `FormBuilderEventExtensions` (typed GUID accessors + `SubmittedData()` JSON round-trip helper since Avro lacks a native any-type). |
| API | ✅ Done | `FormEndpoints` (definitions) + `FormTaskEndpoints` (token submit, my tasks). `submittedBy` resolved via `ICurrentUser` in Application |
| Frontend | ⏳ Pending | — |

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend** | ⚠️ | [submit-assigned-form](./submit-assigned-form/), [assignment-notify](./assignment-notify/), [pending-tasks](./pending-tasks/): notification on assign; expiry → execution failure (workflow-engine); role-based My Tasks aggregation. Token submit + My Tasks API ✅. |
| **Frontend** | ⏳ | Form editor, field picker, standalone submit page, My Tasks — all tenant-registration–subscription-plans US. |
| **Cross-module** | workflow-engine | Form step execution, context expressions, `FormStepReached` consumer path — coordinate with workflow-engine. **platform-foundation [delete-org](../platform-foundation/delete-org/):** `OrganizationFormTaskCanceller` cancels pending form tasks before org hard-delete (Identity-owned job). |

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)
- [Identity & Access](../identity-access/README.md)
- [Data Modeling](../data-modeling/README.md) *(for Relation Picker fields)*

## Dependents

- [Workflow Builder](../workflow-builder/README.md) *(Form step type)*
- [Workflow Engine](../workflow-engine/README.md)
