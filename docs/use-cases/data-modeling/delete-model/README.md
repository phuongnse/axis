# Use case — Delete a model

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Delete a model so that I can clean up unused data structures.

## Primary actor

- Tenant Member with `data_modeling:model:delete`

## Trigger

- User initiates: delete a model

## Main flow

1. Actor starts the — Delete a model flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create custom data models within their Tenant. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

## Acceptance Criteria

*Happy path*
- [ ] Deletion confirmation dialog requires typing the model name exactly (case-sensitive).
- [ ] After confirmation, the model, all its field definitions, and all its records are soft-deleted immediately.
- [ ] User is redirected to the models list with a success toast.

*Validation & errors*
- [ ] If the model is actively referenced by a published workflow step or a form field, deletion is blocked: "This model is used by N workflow(s) and/or N form(s). Remove those references before deleting."
- [ ] Typing the wrong model name in the confirmation input keeps the delete button disabled.

*Edge cases*
- [ ] Soft-deleted models and their records are permanently purged after 30 days by a background job.
- [ ] Workflow steps and form fields that referenced the deleted model are flagged as "broken" in their respective editors after the model is deleted.
- [ ] Relation fields in other models that point to the deleted model are also flagged as broken.

*Out of scope*
- Recovering a soft-deleted model.

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
> **Gaps vs spec:**
> - workflow reference check pending workflow-builder
> - form Relation Picker refs blocked/flagged via FormBuilder `ModelDeletedEvent` consumer ([model deletion guard](./README.md) (partial))
> - 30-day purge background job pending.
>
> **Deferred follow-ups:** DataModeling relation fields on other models flagged broken when target model deleted. WorkflowBuilder `record.*` trigger broken flags shipped via `ModelDeletedHandler` (Kafka).
>
> **Decisions:** Model deletion remains soft-delete first; cross-module broken-reference handling is event-driven through module consumers.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
