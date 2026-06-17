# Use case — Edit a record

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit an existing record so that I can update out-of-date information.

## Primary actor

- Workspace Member with `data_modeling:record:write`

## Trigger

- User initiates: edit an existing record

## Main flow

1. Actor starts the — Edit a record flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the workspace schema.

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
- Edit history / audit trail per record.

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
> **Gaps vs spec:** HTTP 409 optimistic concurrency (updated_at comparison) pending.
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

