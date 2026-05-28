# Use case — Delete a model

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

delete a model so that I can clean up unused data structures.

## Primary actor

- Organization Member with `data_modeling:model:delete`

## Trigger

- User initiates: delete a model

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



**Acceptance Criteria:**

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
- Recovering a soft-deleted model — not in MVP.

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
> **Deferred (PR #N follow-up):** DataModeling relation fields on other models flagged broken when target model deleted. WorkflowBuilder `record.*` trigger broken flags shipped via `ModelDeletedHandler` (Kafka).


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-models | [source](./wireframes/data-models.excalidraw) | [preview](./wireframes/data-models.svg) |

[← Back to Data Modeling](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
