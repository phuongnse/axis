# Use case — Use a data class as a field in a model

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

Use a data class as a field type in a model so that I can embed structured nested objects without duplicating field definitions.

## Primary actor

- Organization Member

## Trigger

- User initiates: use a data class as a field type in a model

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
- [ ] When adding a field of type `DataClass`, a searchable dropdown lists all data classes in the org.
- [ ] After selection, the field is saved referencing the data class by its `id`.
- [ ] In the record form, the data class field is rendered as a grouped sub-form with all its child fields.
- [ ] In the record list, the data class field is displayed as a summary (e.g., the first text field of the data class).

*Validation & errors*
- [ ] Selecting a data class field as `required` means all required fields within the data class must also be filled.
- [ ] If the referenced data class is deleted after the field is created, the field is flagged as "broken" in the model editor with a warning.

*Edge cases*
- [ ] A data class can be used as a field in multiple models simultaneously.
- [ ] Editing the data class definition (adding/removing fields) is reflected immediately in all models that use it, including the form rendering for those models.

*Out of scope*
- A field referencing a data class from another org — tenant-isolated, not possible.

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
> **Gaps vs spec:** sub-form rendering and record-list summary display pending Frontend layer.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-classes | [source](./wireframes/data-classes.excalidraw) | [preview](./wireframes/data-classes.svg) |
| data-models | [source](./wireframes/data-models.excalidraw) | [preview](./wireframes/data-models.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
