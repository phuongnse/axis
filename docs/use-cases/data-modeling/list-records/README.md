# Use case — View records list

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See all records for a model so that I can browse and find the data I need.

## Primary actor

- Organization Member with `data_modeling:record:read`

## Trigger

- User initiates: see all records for a model

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
