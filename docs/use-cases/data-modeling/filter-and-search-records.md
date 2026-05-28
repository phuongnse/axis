# Use case — Filter and search records

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

filter and search records so that I can find the specific data I need quickly.

## Primary actor

- Organization Member with `data_modeling:record:read`

## Trigger

- User initiates: filter and search records

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
- [ ] A search bar performs a full-text search across all `Text` fields of the model.
- [ ] A filter panel allows adding filter conditions per field: equality, contains (text), greater than / less than (number, date), is empty / is not empty.
- [ ] Multiple filters are combined with AND logic.
- [ ] Sort can be set per-column by clicking column headers.

*Validation & errors*
- [ ] Filter values that violate the field type (e.g., entering text in a number filter) show an inline error before the filter is applied.
- [ ] If the filter query returns no results, an empty state is shown with "No records match your filters" and a "Clear filters" button.

*Edge cases*
- [ ] Filters and sort state are preserved in the URL so the user can share or bookmark a filtered view.
- [ ] A filter on a deleted field gracefully falls back (filter is removed with a warning toast).

*Out of scope*
- OR-logic between filters — not in MVP.
- Saved filters — not in MVP.

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
> - filter-state URL persistence is a frontend concern (query params round-tripped via `?filter=field:op:value`)
> - filter on a deleted field falls back gracefully (RecordFilter.TryParse validates field name format, unknown fields simply match no JSONB data). A filter on a deleted field currently returns 0 results rather than showing a warning toast — that warning is a frontend concern.
>
> **Decisions:**
> - per-field filters encoded as repeated `?filter=field:op:value` query params (URL-shareable)
> - ops supported: eq, contains, gt, lt, isEmpty, isNotEmpty
> - multiple filters combined with AND
> - sort via `?sortBy=field&sortDir=asc|desc`
> - unknown/unsafe field names in sort fall back to `created_at DESC`.

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
