# E05 — Form Builder

[← Back to Epics](../README.md)

---

## Overview

Enable users to design interactive forms that can be embedded as steps within workflows. Forms collect structured data from users, validate it, and store submissions that can flow through the rest of the workflow as context data.

## Business Value

Forms are the primary mechanism for human interaction within a workflow. Without forms, workflows can only be fully automated — with forms, they can support approval processes, data entry, and human-in-the-loop automation.

## Phase

**MVP**

---

## Features

| ID | Feature | Description |
|---|---|---|
| [F01](./features/F01-form-definition.md) | Form Definition Management | Create, edit, delete form definitions |
| [F02](./features/F02-form-fields.md) | Form Field Configuration & Validation | Add fields with types, labels, placeholders, validation rules |
| [F03](./features/F03-workflow-integration.md) | Workflow Step Integration | Attach a form to a Form step in a workflow |
| [F04](./features/F04-form-submission.md) | Form Submission Handling | Render form to assignee, capture submission, continue workflow |

---

## Diagrams

![Form Model](./diagrams/form-model.svg)

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

## Acceptance Criteria (Epic Level)

- [ ] Users can create a form with at least 5 different field types.
- [ ] Each field can have required validation, min/max length, and custom error messages.
- [ ] A form can be linked to a Form step in a workflow.
- [ ] When workflow reaches the Form step, the assignee receives a notification with the form link.
- [ ] Submitted form data is available as context variables in subsequent workflow steps.
- [ ] Submitting an invalid form shows inline validation errors without page reload.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `FormDefinition`, `FormField`, `FormSubmission` aggregates; field types and form-task domain events |
| Application | ⚠️ Partial | Form definition CRUD + fields; F04: `SubmitFormByToken`, `GetFormTaskByToken`, `GetMyFormTasks`, `ExpireFormSubmissionHandler`. Notifications and role-based assignee resolution pending |
| Infrastructure | ✅ Done | `form_model_references` read model + `ModelDeletedHandler` (DataModeling Kafka) flags broken Relation Picker fields; delete-model guard via `IFormModelReferenceRepository`. Delete-form guard: `FormWorkflowDeletionGuard` calls WorkflowBuilder gRPC `CountBlockingFormReferences`; `FormDeletedEvent` Avro published on delete for WorkflowBuilder `FormDeletedHandler`. Database `axis_formbuilder` ([ADR-011](../../TECH_STACK.md#adr-011-per-module-database-with-schema-per-tenant-inside)); EF `InitialCreate` migration (regenerated via `dotnet ef`); tests/fixtures use `MigrateAsync` ([ADR-023](../../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)). `FormSubmission` + expiry scheduling via Wolverine. DbContext + UnitOfWork inlined per ADR-017. `FormBuilderEventMapper` translates domain events to Avro at `SaveChangesAsync` and publishes via outbox → Kafka ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). `OrganizationVerifiedHandler` provisions tenant schema via `TenantModuleProvisionAttempt` (reports `TenantModuleProvisionReportEvent` to Identity; retries via `RetryTenantModuleProvisionHandler` + shared `TenantSchemaProvisioner`, E01 US-003). `FormStepReachedHandler` consumes WorkflowEngine's `FormStepReachedEvent` from Kafka (Contracts only — no Domain reference). Consumes WorkflowBuilder lifecycle events from `Axis.WorkflowBuilder.Contracts`. |
| Contracts | ✅ Done | `Axis.FormBuilder.Contracts` — Avro schemas `FormTaskSubmittedEvent` + `FormTaskExpiredEvent` (the form-task lifecycle events WorkflowEngine reacts to). Hand-written `ISpecificRecord` generated code + `FormBuilderKafkaTopics` + `FormBuilderEventExtensions` (typed GUID accessors + `SubmittedData()` JSON round-trip helper since Avro lacks a native any-type). |
| API | ✅ Done | `FormEndpoints` (definitions) + `FormTaskEndpoints` (token submit, my tasks). `submittedBy` resolved via `ICurrentUser` in Application |
| Frontend | ⏳ Pending | — |

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend** | ⚠️ | [F04](./features/F04-form-submission.md): notification on assign; expiry → execution failure (E06); role-based My Tasks aggregation. Token submit + My Tasks API ✅. |
| **Frontend** | ⏳ | Form editor, field picker, standalone submit page, My Tasks — all F01–F04 US. |
| **Cross-module** | E06 | Form step execution, context expressions, `FormStepReached` consumer path — coordinate with E06. **E01 F02 US-007:** `OrganizationFormTaskCanceller` cancels pending form tasks before org hard-delete (Identity-owned job). |

---

## Dependencies

- [E01 — Platform Foundation](../E01-platform-foundation/README.md)
- [E02 — Identity & Access Management](../E02-identity-access/README.md)
- [E03 — Data Modeling](../E03-data-modeling/README.md) *(for Relation Picker fields)*

## Dependents

- [E04 — Workflow Builder](../E04-workflow-builder/README.md) *(Form step type)*
- [E06 — Workflow Execution Engine](../E06-workflow-engine/README.md)
