# Use case — Delete a data class

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Delete a data class that is no longer used by any model so that I can keep the type catalogue tidy.

## Primary actor

- Tenant member with `data_modeling:model:delete`

## Trigger

- User initiates: delete a data class from the data class list or detail page.

## Main flow

1. Actor starts the — Delete a data class flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Data Classes are reusable, named object types composed of multiple fields. They are used as a field type within Models, allowing complex nested structures to be defined once and reused across many models.

## Acceptance Criteria

**As an** Tenant Member with `data_modeling:model:delete`, **I want to** delete a data class that is no longer needed.

*Happy path*
- [ ] Confirmation dialog requires typing the data class name.
- [ ] After confirmation, the data class is soft-deleted.

*Validation & errors*
- [ ] A data class that is currently referenced by one or more model fields cannot be deleted. Error message lists which models reference it: "This data class is used by: [Model A (field: address), Model B (field: billing_address)]. Remove those fields first."
- [ ] Attempting to delete via API while still referenced returns HTTP 409 Conflict.

*Edge cases*
- [ ] Soft-deleted data classes are permanently purged after 30 days along with model soft-deletes.

*Out of scope*
- Merging two data classes into one.

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
> **Gaps vs spec:** none for backend on this use case.
>
> **Done:**
> - HTTP 409 on delete-while-referenced enforced in Application handler.
> - Reference check uses PostgreSQL JSONB `@>` containment query.
>
> **Decisions:** `IsReferencedByAnyModelAsync` uses raw SQL `fields @> {0}::jsonb` to query nested JSON without loading all models into memory.
>
> **Gaps vs spec:**
> - N/A
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

