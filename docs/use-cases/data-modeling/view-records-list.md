# Use case — View records list

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

see all records for a model so that I can browse and find the data I need.

## Primary actor

- Organization Member with `data_modeling:record:read`

## Trigger

- User initiates: see all records for a model

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
- [ ] Records list displays columns for the first 5 fields of the model (configurable by the user — see Out of scope).
- [ ] Default sort: `created_at` descending (newest first).
- [ ] Pagination: 25 records per page with next/previous controls and a page count.
- [ ] Clicking a record opens its detail view.

*Validation & errors*
- [ ] If the records API call fails, the list shows an error state with a Retry button.
- [ ] Sorting by a field that has been deleted gracefully falls back to the default sort.

*Edge cases*
- [ ] An empty model (no records) shows an empty state with a "Create first record" CTA.
- [ ] A model with 0 fields (misconfigured) shows a warning in the list: "This model has no fields. Add fields to the model definition first."
- [ ] Relation field columns display the target record's `display_field` value, not the raw UUID. If the target record was deleted, it shows "[Deleted record]".

*Out of scope*
- Saved views / custom column configurations — not in MVP.
- Inline editing in the list — not in MVP.

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
> **Gaps vs spec:** Relation display-field resolution (showing target record's display_field value instead of raw UUID) backend polish — see gaps below.

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
