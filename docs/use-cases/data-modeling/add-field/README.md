# Use case — Add a field to a model

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Add a field of any supported type to a model so that I can capture the data I need.

## Primary actor

- Workspace Member with `data_modeling:model:write`

## Trigger

- User initiates: add a field of any supported type to a model

## Main flow

1. Actor starts the — Add a field to a model flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each field in a model has a type that determines what data it stores, how it's validated, and how it's rendered in forms and lists. The type system is the foundation of the data modeling module.

## Acceptance Criteria

*Happy path*
- [ ] "Add field" opens a type picker showing all 9 field types with icons and descriptions.
- [ ] Selecting a type opens a config panel: field name (auto-generated from label, editable), label, help text, required toggle, and type-specific options.
- [ ] Saving the field adds it to the model and it is immediately available in the record form.

*Validation & errors*
- [ ] Field label: required, 1–100 characters.
- [ ] Field name: required, 1–64 characters, alphanumeric and underscores only, must start with a letter. Auto-generated from label with invalid chars stripped; user can override.
- [ ] Field name must be unique within the model (case-insensitive). Duplicate shows: "A field named '{name}' already exists in this model."
- [ ] System field names (`id`, `created_at`, `updated_at`) are reserved and cannot be used.
- [ ] For Relation fields: `target_model_id` is required. Selecting the model itself as the target is allowed (self-referential).
- [ ] For DataClass fields: `data_class_id` is required.
- [ ] For Enum fields: at least 2 options required. Each option value must be unique within the list.

*Edge cases*
- [ ] Field name auto-generation from a label with only special characters (e.g., "!!!") results in an empty name; the user is required to enter one manually.
- [ ] Adding a field with `allow_multiple: true` on a Relation field stores values as a JSON array in the record.

*Out of scope*
- Computed / formula fields (e.g., "full_name = first_name + last_name").

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
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:**
> - Frontend: "Add field" opens a type picker showing all 9 field types with icons and descriptions.
> - Frontend: Selecting a type opens a config panel: field name, label, help text, required toggle, and type-specific options.
> - Frontend: Saving the field adds it to the model and it is immediately available in the record form.
>
> **Deferred follow-ups:**
> - Frontend field-builder flow: type picker, type-specific config panel, and save-to-record-form availability.
>
> **Decisions:** all 9 field types serialized to JSONB via custom `FieldDefinitionConverter` — polymorphic FieldConfig deserialized using the `type` discriminator in the JSON object.
>
