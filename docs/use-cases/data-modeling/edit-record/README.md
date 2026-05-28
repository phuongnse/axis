# Use case — Edit a record

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit an existing record so that I can update out-of-date information.

## Primary actor

- Organization Member with `data_modeling:record:write`

## Trigger

- User initiates: edit an existing record

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the tenant schema.

## Acceptance Criteria

*Happy path*
- [ ] Edit form is pre-populated with the current values of all fields.
- [ ] `updated_at` is automatically updated to the current time on save.
- [ ] API: `PATCH /models/{modelId}/records/{recordId}` supports partial updates (only provided fields are updated).

*Validation & errors*
- [ ] Same field-level validation rules as creation apply.
- [ ] If the record was deleted by another user since the edit form was opened, saving returns HTTP 404: "This record no longer exists."

*Edge cases*
- [ ] Optimistic concurrency: if the record's `updated_at` has changed since the edit form was opened, the API returns HTTP 409: "This record was modified by someone else. Please refresh and reapply your changes."
- [ ] Editing a record changes only the specified fields; fields not included in the PATCH body retain their existing values.

*Out of scope*
- Edit history / audit trail per record — not in MVP.

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
> **Gaps vs spec:** HTTP 409 optimistic concurrency (updated_at comparison) backend polish — see gaps below.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| records | [source](./records.excalidraw) | [preview](./records.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
