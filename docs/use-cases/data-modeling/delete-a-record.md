# Use case — Delete a record

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

delete a record so that I can remove outdated or incorrect entries.

## Primary actor

- Organization Member with `data_modeling:record:delete`

## Trigger

- User initiates: delete a record

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the tenant schema.

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
- Restoring a soft-deleted record — not in MVP.

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

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| records | [source](./wireframes/records.excalidraw) | [preview](./wireframes/records.svg) |

[← Back to Data Modeling](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
