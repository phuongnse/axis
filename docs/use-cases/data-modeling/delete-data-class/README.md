# Use case — Delete a data class

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

_(One sentence about user value.)_.

## Primary actor

- _(Actor)_

## Trigger

- _(What starts the use case.)_

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Data Classes are reusable, named object types composed of multiple fields. They are used as a field type within Models, allowing complex nested structures to be defined once and reused across many models.

## Acceptance Criteria

**As an** Organization Member with `data_modeling:model:delete`, **I want to** delete a data class that is no longer needed.

*Happy path*
- [ ] Confirmation dialog requires typing the data class name.
- [ ] After confirmation, the data class is soft-deleted.

*Validation & errors*
- [ ] A data class that is currently referenced by one or more model fields cannot be deleted. Error message lists which models reference it: "This data class is used by: [Model A (field: address), Model B (field: billing_address)]. Remove those fields first."
- [ ] Attempting to delete via API while still referenced returns HTTP 409 Conflict.

*Edge cases*
- [ ] Soft-deleted data classes are permanently purged after 30 days along with model soft-deletes.

*Out of scope*
- Merging two data classes into one — not in MVP.

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
> - HTTP 409 on delete-while-referenced enforced in Application handler
> - reference check uses PostgreSQL JSONB `@>` containment query.
>
> **Decisions:** `IsReferencedByAnyModelAsync` uses raw SQL `fields @> {0}::jsonb` to query nested JSON without loading all models into memory.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-classes | [source](../wireframes/data-classes.excalidraw) | [preview](../wireframes/data-classes.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
