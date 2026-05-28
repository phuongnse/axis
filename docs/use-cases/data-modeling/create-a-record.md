# Use case — Create a record

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

Create a new record for a model so that I can store business data.

## Primary actor

- Organization Member with `data_modeling:record:write`

## Trigger

- User initiates: create a new record for a model

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
> - File field pre-upload step pending file storage service
> - Relation field existence check backend polish — see gaps below.
>
> Diagram pending: entity name `Record` → `DataRecord` in data-model diagram (`dataModelDiagram()` in `generate-diagrams.mjs`) — `Record` is a C# keyword and conflicts with the language reserved word.
>
> **Decisions:** record data stored as `Dictionary<string, object?>` serialized to JSONB column `_data`.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| records | [source](./wireframes/records.excalidraw) | [preview](./wireframes/records.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
