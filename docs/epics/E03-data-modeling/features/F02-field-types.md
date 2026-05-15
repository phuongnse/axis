# F02 — Field Type System

[← Back to E03](../README.md)

---

## Description

Each field in a model has a type that determines what data it stores, how it's validated, and how it's rendered in forms and lists. The type system is the foundation of the data modeling module.

---

## Field Types

| Type | Config options |
|---|---|
| `Text` | `min_length`, `max_length`, `multiline`, `default_value` |
| `Number` | `min`, `max`, `decimal_places`, `default_value` |
| `Boolean` | `default_value` |
| `Date` | `include_time`, `min_date`, `max_date`, `default_value` (`static` or `now`) |
| `Enum` | `options[]` (value + label), `allow_multiple`, `default_value` |
| `Relation` | `target_model_id`, `allow_multiple`, `display_field` |
| `DataClass` | `data_class_id` |
| `File` | `allowed_extensions[]`, `max_size_mb`, `max_files` |
| `JSON` | *(no additional config)* |

---

## User Stories

### US-034 — Add a field to a model

**As an** Organization Member with `data_modeling:model:write`, **I want to** add a field of any supported type to a model **so that** I can capture the data I need.

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Decisions: all 9 field types serialized to JSONB via custom `FieldDefinitionConverter` — polymorphic FieldConfig deserialized using the `type` discriminator in the JSON object.

---

### US-035 — Configure field validation rules

**As an** Organization Member, **I want to** configure validation rules on a field **so that** data quality is enforced at input time.

**Acceptance Criteria:**

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
- Cross-field validation (e.g., "end_date must be after start_date") — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: server-side per-field validation on record create/update is in Application handlers but HTTP 422 structured errors pending API layer.

---

### US-036 — Reorder fields

**As an** Organization Member, **I want to** reorder fields in a model **so that** the display order matches our team's mental model.

**Acceptance Criteria:**

*Happy path*
- [ ] Fields can be dragged up and down via a drag handle in the field editor.
- [ ] The new order is saved on drop (immediate API call, no separate Save button needed).
- [ ] The field order is reflected in: the auto-generated record list columns, the default form field order, and API responses.

*Validation & errors*
- [ ] If the reorder API call fails, the field snaps back to its original position and shows an error toast.

*Edge cases*
- [ ] System fields (`id`, `created_at`, `updated_at`) are always pinned to the end and cannot be reordered.
- [ ] Two users reordering fields simultaneously: last write wins; no conflict detection required for ordering.

*Out of scope*
- Hiding fields from the default list view per user — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: drag-drop reorder UX and immediate-save endpoint pending Frontend layer; `displayOrder` persisted in JSONB field list.
