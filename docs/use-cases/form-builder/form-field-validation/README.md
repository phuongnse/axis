# Use case — Configure validation rules on a field

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Set validation rules on each field so that users are guided to provide correct data.

## Primary actor

- Organization Member

## Trigger

- User initiates: set validation rules on each field

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

## Acceptance Criteria

*Happy path*
- [ ] Validation rules shown in the config panel are specific to the field type (e.g., min/max length for Text, min/max for Number, allowed extensions for File).
- [ ] Each rule supports a custom error message (optional). If not provided, a default message is used.
- [ ] The live preview reflects validation rules (e.g., shows "Required" badge on required fields, helper text for min/max).

*Validation & errors*
- [ ] Max length must be greater than min length for Text fields; violation shows an inline error before saving.
- [ ] For Number fields: max must be greater than min.
- [ ] Server-side validation (API) enforces all rules at form submission time, returning HTTP 422 with structured errors per field key: `{ "errors": { "field_key": ["error message"] } }`.
- [ ] Client-side validation (React Hook Form + Zod) runs on blur and on submit, showing inline errors below each field.

*Edge cases*
- [ ] A required field with a default value expression (pre-population from context): if the expression resolves to a non-null value, the required validation passes even if the user doesn't interact with the field.
- [ ] Changing a validation rule (e.g., tightening max length) on a form that has existing submissions: the change only affects future submissions; existing submissions are not retroactively validated.

*Out of scope*
- Cross-field validation (e.g., "end date must be after start date") — not in MVP.

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
> - no `UpdateFieldValidationCommand` handler — field validation config is part of `AddFieldToForm` (set on creation only)
> - client-side validation (React Hook Form + Zod) pending Frontend
> - HTTP 422 structured errors backend polish — see gaps below.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-editor | [source](../wireframes/form-editor.excalidraw) | [preview](../wireframes/form-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
