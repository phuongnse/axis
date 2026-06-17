# Use case — Add a field to a form

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Add a field to my form so that I can collect the data I need.

## Primary actor

- Workspace Member with `form:definition:write`

## Trigger

- User initiates: add a field to my form

## Main flow

1. Actor starts the — Add a field to a form flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

## Acceptance Criteria

*Happy path*
- [ ] "+ Add field" button opens a type picker showing all supported form field types with icons.
- [ ] Selecting a type opens a config panel: label (required), field key (auto-generated from label, editable), help text (optional), required toggle, and type-specific options.
- [ ] The live preview updates immediately to show the new field.

*Validation & errors*
- [ ] Label: required, 1–200 characters.
- [ ] Field key: required, 1–64 characters, alphanumeric and underscores, unique within the form. Auto-generated from label; user can override.
- [ ] For Dropdown and Multi-select: at least 2 options must be defined. Duplicate option values are blocked.
- [ ] For Relation Picker: target model selection is required.
- [ ] For File Upload: `allowed_extensions` must be a valid list (e.g., `pdf,jpg,png`); invalid extensions like `exe` are rejected.

*Edge cases*
- [ ] Field key auto-generation from a label of only special characters (e.g., "???") yields an empty key; user must enter one manually.
- [ ] Adding a field to a form that is live in an active workflow notifies the user via the warning banner (see [live-workflow warning](./README.md)) but does not block the action.

*Out of scope*
- Conditional field visibility (show field only if another field has a certain value).

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
> **Gaps vs spec:** type picker UI, live preview update, and extension validation for File Upload pending Frontend + API layers.
>
> **Decisions:** `AddFieldToFormHandler` catches both `ArgumentException` (invalid key format) and `InvalidOperationException` (duplicate key) from the domain and returns `ErrorCodes.BusinessRule`. Field config polymorphism handled by FormFieldConverter using FormFieldType enum as discriminator.
>
> **Deferred follow-ups:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

