# F04 — Data Record CRUD

> **Wireframe**: [docs/wireframes/E03-data-modeling/records.excalidraw](../../../wireframes/E03-data-modeling/records.excalidraw) · [preview](../../../wireframes/E03-data-modeling/records.svg)

[← Back to E03](../README.md)

---

## Description

Users can create, read, update, and delete records against any model. Records are stored as JSONB in the tenant schema.

---

## User Stories

### US-041 — Create a record

**As an** Organization Member with `data_modeling:record:write`, **I want to** create a new record for a model **so that** I can store business data.

**Acceptance Criteria:**

*Happy path*
- [ ] Record form is auto-generated from the model's field definitions in the defined field order.
- [ ] On successful submission, the record appears in the records list and the form resets (or closes, depending on the chosen UX flow).
- [ ] API: `POST /models/{modelId}/records` returns the created record with HTTP 201 and a `Location` header.

*Validation & errors*
- [ ] Required fields that are left empty show inline errors on submit; the form does not close.
- [ ] Field-level validation errors (min/max, regex, etc.) are shown inline per field.
- [ ] API returns HTTP 422 with structured errors: `{ "errors": { "field_name": ["error"] } }`.
- [ ] If a Relation field references a record ID that does not exist or belongs to a different model, the API returns HTTP 422.

*Edge cases*
- [ ] Creating a record with no optional fields filled in (only required fields) is valid.
- [ ] A record with a `File` field attaches files via a pre-upload step; the file reference is stored in the record, not the file content itself.
- [ ] Concurrent creation of two records with unique-field constraints (if any) uses DB-level unique indexes to prevent duplicates.

*Out of scope*
- Record templates (pre-filled forms) — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: File field pre-upload step pending file storage service; Relation field existence check pending API layer.
> Decisions: record data stored as `Dictionary<string, object?>` serialized to JSONB column `_data`.

---

### US-042 — View records list

**As an** Organization Member with `data_modeling:record:read`, **I want to** see all records for a model **so that** I can browse and find the data I need.

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: Relation display-field resolution (showing target record's display_field value instead of raw UUID) pending API layer.

---

### US-043 — Filter and search records

**As an** Organization Member with `data_modeling:record:read`, **I want to** filter and search records **so that** I can find the specific data I need quickly.

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: filter-state URL persistence is a frontend concern (query params round-tripped via `?filter=field:op:value`); filter on a deleted field falls back gracefully (RecordFilter.TryParse validates field name format, unknown fields simply match no JSONB data). A filter on a deleted field currently returns 0 results rather than showing a warning toast — that warning is a frontend concern.
> Decisions: per-field filters encoded as repeated `?filter=field:op:value` query params (URL-shareable); ops supported: eq, contains, gt, lt, isEmpty, isNotEmpty; multiple filters combined with AND; sort via `?sortBy=field&sortDir=asc|desc`; unknown/unsafe field names in sort fall back to `created_at DESC`.

---

### US-044 — Edit a record

**As an** Organization Member with `data_modeling:record:write`, **I want to** edit an existing record **so that** I can update out-of-date information.

**Acceptance Criteria:**

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: HTTP 409 optimistic concurrency (updated_at comparison) pending API layer.

---

### US-045 — Delete a record

**As an** Organization Member with `data_modeling:record:delete`, **I want to** delete a record **so that** I can remove outdated or incorrect entries.

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: Relation broken-reference warning pending E04 integration; 30-day purge pending background job scheduler.

---

### US-046 — Bulk operations on records

**As an** Organization Member, **I want to** select multiple records and perform bulk actions **so that** I can manage large datasets efficiently.

**Acceptance Criteria:**

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: async export for >5,000 records deferred — pending Wolverine background job + in-app notification infrastructure; current sync CSV export has no size limit (streams in 500-record chunks). "Select all N records across all pages" for bulk delete is a frontend concern.
> Decisions: bulk delete via `POST /api/models/{id}/records/bulk-delete` with `{ "ids": [...] }` body; CSV export via `GET /api/models/{id}/records/export` (same filter/sort params as list); field names for export header taken from model's FieldDefinition labels; CSV uses RFC 4180 escaping.
