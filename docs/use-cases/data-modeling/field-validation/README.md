# Use case — Configure field validation rules

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure validation rules on a field so that data quality is enforced at input time.

## Primary actor

- Workspace Member

## Trigger

- User initiates: configure validation rules on a field

## Main flow

1. Actor starts the — Configure field validation rules flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each field in a model has a type that determines what data it stores, how it's validated, and how it's rendered in forms and lists. The type system is the foundation of the data modeling module.

## Acceptance Criteria

*Happy path*
- [ ] Saving a field with validation rules persists them to the field's `config` JSONB column.
- [ ] Validation is enforced both client-side (form rendering) and server-side (record create/update API).

*Validation & errors*
- [ ] For Text fields: if `max_length < min_length`, the form shows: "Max length must be greater than min length."
- [ ] For Number fields: if `max < min`, similar error.
- [ ] For Date fields: if `max_date < min_date`, similar error.
- [ ] For File fields: `max_size_mb` must be between 0.1 and 100.
- [ ] API returns field-level validation errors in structured form: `{ "errors": { "field_name": ["error message"] } }` (HTTP 422).

*Edge cases*
- [ ] Tightening a validation rule on a field with existing records that would now violate it: the rule change is allowed, but existing records are not retroactively invalidated (rules only apply on create/update).
- [ ] A required field with a default value: the default is applied automatically on record creation if the field is not provided by the caller.

*Out of scope*
- Cross-field validation (e.g., "end_date must be after start_date").

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ⏳ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** server-side per-field validation on record create/update is in Application handlers but HTTP 422 structured errors pending.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
