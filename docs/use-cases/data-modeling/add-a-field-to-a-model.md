# Use case — Add a field to a model

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

add a field of any supported type to a model so that I can capture the data I need.

## Primary actor

- Organization Member with `data_modeling:model:write`

## Trigger

- User initiates: add a field of any supported type to a model

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Each field in a model has a type that determines what data it stores, how it's validated, and how it's rendered in forms and lists. The type system is the foundation of the data modeling module.

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
- Computed / formula fields (e.g., "full_name = first_name + last_name") — not in MVP.

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
> **Decisions:** all 9 field types serialized to JSONB via custom `FieldDefinitionConverter` — polymorphic FieldConfig deserialized using the `type` discriminator in the JSON object.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-models | [source](./wireframes/data-models.excalidraw) | [preview](./wireframes/data-models.svg) |

[← Back to Data Modeling](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
