# Use case — Delete a record

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Delete a record so that I can remove outdated or incorrect entries.

## Primary actor

- Workspace Member with `data_modeling:record:delete`

## Trigger

- User initiates: delete a record

## Main flow

1. Actor starts the — Delete a record flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the workspace schema.

## Acceptance Criteria

*Happy path*
- [ ] Confirmation dialog shows the record's primary display field so the user knows exactly what they are deleting.
- [ ] After confirmation, the record is soft-deleted and removed from the list immediately.
- [ ] API: `DELETE /models/{modelId}/records/{recordId}` returns HTTP 204.

*Validation & errors*
- [ ] If the record is referenced by another record's Relation field, a warning is shown: "This record is referenced by N other record(s). Deleting it will leave those relations broken." User must confirm to proceed.
- [ ] If the record is referenced by an in-progress workflow execution context, deletion is allowed but the workflow will encounter a missing record reference at that step.

*Edge cases*
- [ ] Soft-deleted records are permanently purged after 30 days.
- [ ] A soft-deleted record is not returned by list or search queries.

*Out of scope*
- Restoring a soft-deleted record.

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
> **Gaps vs spec:** Relation broken-reference warning pending workflow-builder integration; 30-day purge pending background job scheduler.
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

