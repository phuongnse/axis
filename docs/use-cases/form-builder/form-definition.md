# Use Case Group — Form Definition Management

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| forms | [source](./wireframes/forms.excalidraw) | [preview](./wireframes/forms.svg) |

[← Back to Form Builder](./README.md)

---

## Description

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

---

## Use Cases

### Use case — Create a form

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `form:definition:write`, **I want to** create a new form **so that** I can design a data collection interface.

**Acceptance Criteria:**

*Happy path*
- [ ] Creation dialog collects: name (required), description (optional).
- [ ] New form is created immediately with no fields and opens in the form editor.
- [ ] A live preview panel on the right of the editor shows the form as it would appear to a user filling it in.

*Validation & errors*
- [ ] Name: required, 2–200 characters, unique within the org (case-insensitive). Duplicate shows: "A form named '{name}' already exists."

*Edge cases*
- [ ] Creating a form and immediately navigating away without adding fields: the empty form is saved and visible in the forms list.

*Out of scope*
- Form templates / starter library — not in MVP.

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
> **Gaps vs spec:** live preview panel and form editor pending Frontend.
>
> **Decisions:** all form fields stored as JSONB via custom FormFieldConverter using FormFieldType as polymorphic discriminator.

---

### Use case — View all forms

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `form:definition:read`, **I want to** see all forms in my organization **so that** I can find existing forms to reuse.

**Acceptance Criteria:**

*Happy path*
- [ ] Forms list shows: name, field count, last modified date, and the number of workflow steps currently using the form ("Used in N workflow(s)").
- [ ] Search by name (real-time, client-side).
- [ ] Clicking a form opens it in the form editor (read-only for users without write permission).

*Validation & errors*
- [ ] Empty state: "No forms yet. Create your first form."
- [ ] Users without `form:definition:read` who navigate to this URL are redirected to home.

*Edge cases*
- [ ] "Used in N workflow(s)" count includes both Draft and Active workflows, as both can reference forms.

*Out of scope*
- Folders/categories for organizing forms — not in MVP.

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
> - "Used in N workflow(s)" count pending cross-module query — not supported at Application layer without inter-module dependency
> - deferred to API/Frontend aggregation.
>
> **Decisions:** `GetFormsHandler` paginates in-memory (GetAllAsync + LINQ Skip/Take). This is an accepted trade-off for MVP: adding a `GetPagedAsync` repository method would push sorting/paging logic into Infrastructure without additional correctness benefit at this scale. `Page` and `PageSize` are clamped to ≥ 1 and ≤ 100 in the handler.

---

### Use case — Edit a form

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `form:definition:write`, **I want to** edit an existing form **so that** I can update its fields as requirements change.

**Acceptance Criteria:**

*Happy path*
- [ ] Form editor shows the field list on the left and a live preview on the right.
- [ ] All changes are saved automatically (auto-save with 1-second debounce).

*Validation & errors*
- [ ] Editing a form that is used in one or more published (Active) workflows shows a persistent warning banner: "This form is live in N active workflow(s). Changes take effect immediately for new form task instances."
- [ ] The warning does not block editing — it is informational only.

*Edge cases*
- [ ] Form tasks that are already in-progress (status: PENDING or WAITING) when the form is edited: they use the form definition as it was when the task was created (definition is snapshotted at task creation time).
- [ ] Conflict resolution: if two users edit the same form simultaneously, last save wins on a per-field basis. No conflict detection is required for form field ordering changes.

*Out of scope*
- Form versioning (publishing a new "version" of a form) — not in MVP; edits are live immediately.

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
> **Gaps vs spec:** live-workflow warning banner (informational) and definition snapshot for in-progress tasks pending API + workflow-engine.
>
> **Decisions:**
> - `UpdateFormHandler` checks name uniqueness via `NameExistsAsync(name, orgId, excludeId)` before calling `form.Update()`
> - TOCTOU race requires a unique DB index on `(name, org_id)` at Infrastructure layer.

---

### Use case — Delete a form

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `form:definition:write`, **I want to** delete a form **so that** I can clean up unused forms.

**Acceptance Criteria:**

*Happy path*
- [ ] Confirmation dialog requires typing the form name.
- [ ] After confirmation, the form is soft-deleted and removed from the forms list.

*Validation & errors*
- [ ] A form referenced by any workflow step (Draft or Active) cannot be deleted. Error message lists which workflows reference it: "This form is used by: [Workflow A (step: Approval), Workflow B (step: Intake)]. Remove those references first."
- [ ] Attempting to delete via API while still referenced returns HTTP 409 Conflict.

*Edge cases*
- [ ] A form can be deleted if it is only referenced by Archived workflows (since archived workflows cannot be triggered).
- [ ] Soft-deleted forms are permanently purged after 30 days.

*Out of scope*
- Recovering a soft-deleted form — not in MVP.

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
> - HTTP 409 on delete-while-referenced enforced via `IsReferencedByWorkflowAsync` JSONB query across `workflow_definitions.steps`
> - archived-workflow exception backend polish — see gaps below.
>
> **Decisions:** `IsReferencedByWorkflowAsync` uses raw SQL `workflow_definitions.steps @> [{...}]::jsonb` — cross-module table query within the same tenant schema.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
