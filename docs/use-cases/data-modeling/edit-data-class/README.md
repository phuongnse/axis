# Use case — Edit a data class

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit a data class so that I can add or remove fields as requirements change.

## Primary actor

- Organization Member with `data_modeling:model:write`

## Trigger

- User initiates: edit a data class

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Data Classes are reusable, named object types composed of multiple fields. They are used as a field type within Models, allowing complex nested structures to be defined once and reused across many models.

## Acceptance Criteria

*Happy path*
- [ ] User can add new fields, edit existing fields, and reorder fields.
- [ ] Changes are saved on explicit "Save" action.

*Validation & errors*
- [ ] Deleting a field from a data class shows a warning listing all models affected: "Removing '{field}' will affect N model(s): [Model A, Model B]. Existing record data for this field will be hidden but not deleted."
- [ ] Same field name uniqueness and format rules as model fields apply within a data class.

*Edge cases*
- [ ] Adding a required field to a data class that is already used in models with existing records: the field is added as optional automatically (with a warning), to avoid retroactively invalidating existing records. The user must consciously make it required.

*Deferred capabilities*
- Version history of data class changes.

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
> - "models affected" warning on field delete pending API/Frontend layer
> - auto-downgrade-to-optional for required fields on existing-record models pending.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
