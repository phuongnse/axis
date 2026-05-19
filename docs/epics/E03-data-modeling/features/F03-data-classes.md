# F03 — Data Class Management

> **Wireframe**: [docs/epics/E03-data-modeling/wireframes/data-classes.excalidraw](../wireframes/data-classes.excalidraw) · [preview](../wireframes/data-classes.svg)

[← Back to E03](../README.md)

---

## Description

Data Classes are reusable, named object types composed of multiple fields. They are used as a field type within Models, allowing complex nested structures to be defined once and reused across many models.

---

## User Stories

### US-037 — Create a data class

**As an** Organization Member with `data_modeling:model:write`, **I want to** create a data class **so that** I can define a reusable nested object structure.

**Acceptance Criteria:**

*Happy path*
- [ ] Creation dialog collects: name (required) and description (optional).
- [ ] After creation, the data class opens in the field editor where the user adds fields.
- [ ] Data class fields use the same field type system as model fields, excluding `Relation`, `DataClass`, and `File` types (to prevent deep nesting and circular references in MVP).

*Validation & errors*
- [ ] Name: required, 2–100 characters, unique within the org (case-insensitive). Duplicate shows: "A data class named '{name}' already exists."
- [ ] Attempting to add a `DataClass` or `Relation` field type inside a data class is blocked by hiding those options from the type picker.

*Edge cases*
- [ ] Creating a data class with the same name as a model is allowed (they occupy different namespaces).

*Out of scope*
- Nested data classes (data class within a data class) — depth limited to 1 in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Decisions: DataClass fields stored as JSONB using the same FieldDefinitionConverter as DataModel; Relation/DataClass/File types blocked in domain by guard.

---

### US-038 — Use a data class as a field in a model

**As an** Organization Member, **I want to** use a data class as a field type in a model **so that** I can embed structured nested objects without duplicating field definitions.

**Acceptance Criteria:**

*Happy path*
- [ ] When adding a field of type `DataClass`, a searchable dropdown lists all data classes in the org.
- [ ] After selection, the field is saved referencing the data class by its `id`.
- [ ] In the record form, the data class field is rendered as a grouped sub-form with all its child fields.
- [ ] In the record list, the data class field is displayed as a summary (e.g., the first text field of the data class).

*Validation & errors*
- [ ] Selecting a data class field as `required` means all required fields within the data class must also be filled.
- [ ] If the referenced data class is deleted after the field is created, the field is flagged as "broken" in the model editor with a warning.

*Edge cases*
- [ ] A data class can be used as a field in multiple models simultaneously.
- [ ] Editing the data class definition (adding/removing fields) is reflected immediately in all models that use it, including the form rendering for those models.

*Out of scope*
- A field referencing a data class from another org — tenant-isolated, not possible.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: sub-form rendering and record-list summary display pending Frontend layer.

---

### US-039 — Edit a data class

**As an** Organization Member with `data_modeling:model:write`, **I want to** edit a data class **so that** I can add or remove fields as requirements change.

**Acceptance Criteria:**

*Happy path*
- [ ] User can add new fields, edit existing fields, and reorder fields.
- [ ] Changes are saved on explicit "Save" action.

*Validation & errors*
- [ ] Deleting a field from a data class shows a warning listing all models affected: "Removing '{field}' will affect N model(s): [Model A, Model B]. Existing record data for this field will be hidden but not deleted."
- [ ] Same field name uniqueness and format rules as model fields apply within a data class.

*Edge cases*
- [ ] Adding a required field to a data class that is already used in models with existing records: the field is added as optional automatically (with a warning), to avoid retroactively invalidating existing records. The user must consciously make it required.

*Out of scope*
- Version history of data class changes — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: "models affected" warning on field delete pending API/Frontend layer; auto-downgrade-to-optional for required fields on existing-record models pending API layer.

---

### US-040 — Delete a data class

**As an** Organization Member with `data_modeling:model:delete`, **I want to** delete a data class that is no longer needed.

**Acceptance Criteria:**

*Happy path*
- [ ] Confirmation dialog requires typing the data class name.
- [ ] After confirmation, the data class is soft-deleted.

*Validation & errors*
- [ ] A data class that is currently referenced by one or more model fields cannot be deleted. Error message lists which models reference it: "This data class is used by: [Model A (field: address), Model B (field: billing_address)]. Remove those fields first."
- [ ] Attempting to delete via API while still referenced returns HTTP 409 Conflict.

*Edge cases*
- [ ] Soft-deleted data classes are permanently purged after 30 days along with model soft-deletes.

*Out of scope*
- Merging two data classes into one — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: HTTP 409 on delete-while-referenced enforced in Application handler; reference check uses PostgreSQL JSONB `@>` containment query.
> Decisions: `IsReferencedByAnyModelAsync` uses raw SQL `fields @> {0}::jsonb` to query nested JSON without loading all models into memory.
