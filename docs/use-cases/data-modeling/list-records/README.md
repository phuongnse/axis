# Use case — View records list

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See all records for a model so that I can browse and find the data I need.

## Primary actor

- Workspace Member with `data_modeling:record:read`

## Trigger

- User initiates: see all records for a model

## Main flow

1. Actor starts the — View records list flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the workspace schema.

## Acceptance Criteria

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
- Saved views / custom column configurations.
- Inline editing in the list.

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
> **Gaps vs spec:** Relation display-field resolution (showing target record's display_field value instead of raw UUID) pending.
>
> **Deferred follow-ups:** Relation field columns display the target record's `display_field` value, not the raw UUID; if the target record was deleted, show "[Deleted record]".
>
> **Decisions:** N/A - no implementation-specific decision recorded for this slice.
>

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
