# F02 — Form Field Configuration & Validation

[← Back to E05](../README.md)

---

## Description

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

---

## User Stories

### US-079 — Add a field to a form

**As an** Organization Member with `form:definition:write`, **I want to** add a field to my form **so that** I can collect the data I need.

**Acceptance Criteria:**

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
- [ ] Adding a field to a form that is live in an active workflow notifies the user via the warning banner (see US-077) but does not block the action.

*Out of scope*
- Conditional field visibility (show field only if another field has a certain value) — not in MVP.

---

### US-080 — Configure validation rules on a field

**As an** Organization Member, **I want to** set validation rules on each field **so that** users are guided to provide correct data.

**Acceptance Criteria:**

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

---

### US-081 — Reorder fields via drag-and-drop

**As an** Organization Member, **I want to** drag form fields to reorder them **so that** the form flows naturally.

**Acceptance Criteria:**

*Happy path*
- [ ] Each field in the editor has a drag handle (six-dot icon) on its left side.
- [ ] Dragging a field up or down reorders it; the live preview updates in real time during the drag.
- [ ] The new order is auto-saved immediately on drop.

*Validation & errors*
- [ ] If the reorder API call fails, the field snaps back to its original position and an error toast is shown.

*Edge cases*
- [ ] Section dividers can be reordered along with fields, maintaining their grouping relationship.
- [ ] Reordering a field from one section into another section is supported.

*Out of scope*
- Multi-column form layouts — not in MVP (single-column only).

---

### US-082 — Add a section divider

**As an** Organization Member, **I want to** group related fields under a section heading **so that** the form is easier to understand.

**Acceptance Criteria:**

*Happy path*
- [ ] "+ Add section" option in the field type picker adds a section element (distinct from field elements).
- [ ] Section element has: title (required) and description (optional).
- [ ] Fields placed below a section (in the editor) are visually grouped under it in the preview until the next section heading.

*Validation & errors*
- [ ] Section title: required, 1–100 characters.

*Edge cases*
- [ ] A form can have sections with no fields between them (empty section) — allowed but shows a warning in the preview: "This section has no fields."
- [ ] Deleting a section header does not delete the fields below it; they move up to the previous section (or become ungrouped if they were in the first section).

*Out of scope*
- Collapsible sections — not in MVP.
