# F01 — Model Definition

> **Wireframe**: [docs/epics/E03-data-modeling/wireframes/data-models.excalidraw](../wireframes/data-models.excalidraw) · [preview](../wireframes/data-models.svg)

[← Back to E03](../README.md)

---

## Description

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

---

## User Stories

### US-030 — Create a model

**As an** Organization Member with `data_modeling:model:write`, **I want to** create a new model **so that** I can start defining the data structure for my business objects.

**Acceptance Criteria:**

*Happy path*
- [ ] Creation dialog collects: name (required), description (optional), icon (optional, from a predefined icon set), and color (optional).
- [ ] The model is created immediately with auto-generated system fields: `id` (UUID), `created_at` (DateTime), `updated_at` (DateTime).
- [ ] After creation, the model opens in the field editor.

*Validation & errors*
- [ ] Name: required, 2–100 characters. Allows letters, numbers, spaces, and hyphens. Blocks special characters like `/ \ < > " ;`.
- [ ] Name must be unique within the org (case-insensitive). Duplicate name shows: "A model named '{name}' already exists."
- [ ] If the plan's model limit is reached, creation returns HTTP 402 with an upgrade prompt instead of a form error.

*Edge cases*
- [ ] Creating a model with a name that matches a soft-deleted model is allowed (they are different models).
- [ ] Model creation is atomic: if any part of the creation fails (e.g., inserting system fields), the entire model is rolled back and nothing is left in a partial state.

*Out of scope*
- Importing a model from another org or from a JSON file directly — covered in [E04 F07 Import/Export](../../E04-workflow-builder/features/F07-import-export.md).

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: model plan-limit check (HTTP 402) pending billing layer (E01 F04); name format validation enforced in Application handler.
> Decisions: system fields (id, created_at, updated_at) injected by domain factory; atomicity guaranteed by UnitOfWork.

---

### US-031 — View all models

**As an** Organization Member with `data_modeling:model:read`, **I want to** see all models in my organization **so that** I can understand the data available to me.

**Acceptance Criteria:**

*Happy path*
- [ ] Models list shows: icon, name, description (truncated), field count, record count, and last modified date.
- [ ] Default sort: alphabetical by name.
- [ ] Search bar filters by name in real time (client-side filter, no API call on each keystroke).

*Validation & errors*
- [ ] If the models list fails to load, an error state with a "Retry" button is shown instead of an empty list.
- [ ] Users without `data_modeling:model:read` who navigate to this URL are redirected to home with a permission error.

*Edge cases*
- [ ] If the org has no models yet, the list shows an empty state with a "Create your first model" CTA.
- [ ] Record count may be slightly behind real-time (cached with 1-minute TTL) to avoid expensive COUNT queries on every list load.

*Out of scope*
- Folders or categories for organizing models — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: record count column pending denormalized counter or API-layer aggregation; field count is derived from Fields.Count at query time.

---

### US-032 — Edit a model

**As an** Organization Member with `data_modeling:model:write`, **I want to** edit an existing model **so that** I can add, remove, or rename fields as requirements evolve.

**Acceptance Criteria:**

*Happy path*
- [ ] User can update: name, description, icon, and color from the model settings panel.
- [ ] User can add, edit, reorder, and delete fields from the field editor.
- [ ] All changes are saved on explicit "Save" action (not auto-save, to prevent accidental data loss).

*Validation & errors*
- [ ] Renaming a model follows the same uniqueness and format rules as creation.
- [ ] Deleting a field that contains data on existing records shows a confirmation dialog: "Deleting '{field_name}' will permanently remove this data from all {N} records. This cannot be undone."
- [ ] Adding a required field to a model that already has records requires the user to provide a default value; without it, submission is blocked.
- [ ] If the model is referenced by an active workflow step, editing it shows an informational warning (not a blocker): "This model is used in N workflow(s). Changes may affect their behavior."

*Edge cases*
- [ ] Two admins editing the same model simultaneously: last save wins. The first admin's changes are not silently overwritten without notice — on save, if the server detects a version conflict (via `updated_at` comparison), it returns HTTP 409 with: "This model was modified by someone else. Please refresh and reapply your changes."
- [ ] Renaming a field does not lose existing data; the underlying storage key is the field's immutable `id`, not its name.

*Out of scope*
- Undo history for field changes — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: HTTP 409 version-conflict check pending API layer (updated_at comparison); active-workflow warning pending E04 integration.

---

### US-033 — Delete a model

**As an** Organization Member with `data_modeling:model:delete`, **I want to** delete a model **so that** I can clean up unused data structures.

**Acceptance Criteria:**

*Happy path*
- [ ] Deletion confirmation dialog requires typing the model name exactly (case-sensitive).
- [ ] After confirmation, the model, all its field definitions, and all its records are soft-deleted immediately.
- [ ] User is redirected to the models list with a success toast.

*Validation & errors*
- [ ] If the model is actively referenced by a published workflow step or a form field, deletion is blocked: "This model is used by N workflow(s) and/or N form(s). Remove those references before deleting."
- [ ] Typing the wrong model name in the confirmation input keeps the delete button disabled.

*Edge cases*
- [ ] Soft-deleted models and their records are permanently purged after 30 days by a background job.
- [ ] Workflow steps and form fields that referenced the deleted model are flagged as "broken" in their respective editors after the model is deleted.
- [ ] Relation fields in other models that point to the deleted model are also flagged as broken.

*Out of scope*
- Recovering a soft-deleted model — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: workflow reference check pending E04; form Relation Picker refs blocked/flagged via FormBuilder `ModelDeletedEvent` consumer (US-033 partial); 30-day purge background job pending.
> **Deferred:** DataModeling relation fields on other models flagged broken when target model deleted. WorkflowBuilder `record.*` trigger broken flags shipped via `ModelDeletedHandler` (Kafka).
