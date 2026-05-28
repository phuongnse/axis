# Use case — Bulk operations on records

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Select multiple records and perform bulk actions so that I can manage large datasets efficiently.

## Primary actor

- Organization Member

## Trigger

- User initiates: select multiple records and perform bulk actions

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
- [ ] Records list has a checkbox column; "Select all" selects all records on the current page.
- [ ] Bulk delete: confirmation dialog shows the count of selected records. On confirm, all are soft-deleted.
- [ ] Bulk export: downloads a CSV file named `{model-slug}-records-{date}.csv` with all field values for selected records.

*Validation & errors*
- [ ] Selecting 0 records and clicking a bulk action shows: "Please select at least one record."
- [ ] If any record in a bulk delete fails (e.g., locked by a running execution), the others still delete and a summary shows which failed and why.

*Edge cases*
- [ ] "Select all" selects only the current page (25 records), not all records in the model. A "Select all {N} records" option extends the selection across all pages.
- [ ] Bulk export of more than 5,000 records is processed asynchronously; the user receives a download link via email or in-app notification when ready.

*Out of scope*
- Bulk edit (updating multiple records at once) — not in MVP.

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
> - async export for >5,000 records deferred — pending Wolverine background job + in-app notification infrastructure
> - current sync CSV export has no size limit (streams in 500-record chunks). "Select all N records across all pages" for bulk delete is a frontend concern.
>
> **Decisions:**
> - bulk delete via `POST /api/models/{id}/records/bulk-delete` with `{ "ids": [...] }` body
> - CSV export via `GET /api/models/{id}/records/export` (same filter/sort params as list)
> - field names for export header taken from model's FieldDefinition labels
> - CSV uses RFC 4180 escaping.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
