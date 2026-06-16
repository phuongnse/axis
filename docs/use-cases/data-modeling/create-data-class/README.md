# Use case — Create a data class

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Create a data class so that I can define a reusable nested object structure.

## Primary actor

- Team account Member with `data_modeling:model:write`

## Trigger

- User initiates: create a data class

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
- [ ] Creation dialog collects: name (required) and description (optional).
- [ ] After creation, the data class opens in the field editor where the user adds fields.
- [ ] Data class fields use the same field type system as model fields, excluding `Relation`, `DataClass`, and `File` types (to prevent deep nesting and circular references).

*Validation & errors*
- [ ] Name: required, 2–100 characters, unique within the team account (case-insensitive). Duplicate shows: "A data class named '{name}' already exists."
- [ ] Attempting to add a `DataClass` or `Relation` field type inside a data class is blocked by hiding those options from the type picker.

*Edge cases*
- [ ] Creating a data class with the same name as a model is allowed (they occupy different namespaces).

*Out of scope*
- Nested data classes (data class within a data class) — depth limited to 1.

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
> **Decisions:**
> - DataClass fields stored as JSONB using the same FieldDefinitionConverter as DataModel
> - Relation/DataClass/File types blocked in domain by guard. DataClassDefinition reuses `FieldDefinition` directly — no separate DataClassField entity.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

