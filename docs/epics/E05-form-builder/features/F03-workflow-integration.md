# F03 — Workflow Step Integration

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| forms | [source](../wireframes/forms.excalidraw) | [preview](../wireframes/forms.svg) |

[← Back to E05](../README.md)

---

## Description

Forms are attached to Form steps in a workflow. The engine creates a Form Task and notifies the assignee when the step is reached.

---

## User Stories

### US-083 — Link a form to a workflow Form step

**As an** Organization Member, **I want to** select a form when configuring a Form step **so that** the right form is presented to the assignee during execution.

**Acceptance Criteria:**

*Happy path*
- [ ] Form step config panel shows a searchable dropdown of all forms in the org.
- [ ] Selecting a form shows a compact preview of its fields in the panel (field names and types listed).
- [ ] The step node on the canvas shows the selected form name as a summary.

*Validation & errors*
- [ ] Saving the step without selecting a form shows: "A form is required for a Form step."
- [ ] If the selected form is deleted after the step is configured, the step node shows a broken indicator (red outline + warning icon) and publishing is blocked until the form is replaced or the step is removed.

*Edge cases*
- [ ] A form that has 0 fields can be selected, but the canvas shows a warning: "The selected form has no fields."
- [ ] The same form can be used in multiple Form steps within the same workflow (e.g., a multi-stage approval process using the same form at each stage).

*Out of scope*
- Creating a new form from within the workflow canvas (must go to Form Builder) — not in MVP.

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
> **Gaps vs spec:** broken-step indicator pending Frontend + API.
>
> **Decisions:** `GetFormPickerQuery` returns all forms for the org as a flat list (Id, Name, FieldCount) ordered by name — used by the API form-step picker dropdown. `IsReferencedByWorkflowAsync` query supports the reference check.

---

### US-084 — Pre-populate form fields from execution context

**As an** Organization Member, **I want to** pre-populate form fields with values from the workflow context **so that** assignees don't re-enter data that's already known.

**Acceptance Criteria:**

*Happy path*
- [ ] Each field in the Form step config has an optional "Default value" input accepting static values or `{{context.step_id.field}}` expressions.
- [ ] Expressions are validated for syntax at save time.
- [ ] At execution time, resolved defaults are shown as pre-filled values in the form; the assignee can change them before submitting.

*Validation & errors*
- [ ] Invalid expression syntax (mismatched braces, invalid identifiers) shows: "Invalid expression: {expression}" at save time.
- [ ] An expression that resolves to a value incompatible with the field type (e.g., text into a number field) is coerced if possible, or left empty with a warning in the execution log.

*Edge cases*
- [ ] An expression that references a context variable that does not exist at execution time (e.g., a step that was skipped) resolves to `null` and leaves the field empty — this is not an execution error.
- [ ] A pre-populated required field that the assignee clears before submitting triggers the required validation error.

*Out of scope*
- Hiding fields from the assignee while keeping them pre-populated (hidden fields) — not in MVP.

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
> **Gaps vs spec:** context expression input UI and expression evaluation at execution time pending Frontend + E06.

---

### US-085 — Map form submission data into workflow context

**As an** Organization Member, **I want** the data submitted in a form to be available to subsequent steps **so that** the rest of the process can use it.

**Acceptance Criteria:**

*Happy path*
- [ ] All submitted field values are stored in the execution context under the step's namespace automatically: `{{context.{step_id}.{field_key}}}`.
- [ ] No manual mapping configuration is needed; all fields are available.
- [ ] The context variable picker in subsequent steps' config panels shows the Form step's output fields with their types.

*Validation & errors*
- [ ] There are no errors to handle here at design time — mapping is automatic. At execution time, if a form submission is missing an expected field (e.g., optional field not filled), the context value is `null`.

*Edge cases*
- [ ] File Upload fields in forms: the context value is a file reference object `{ "id": "...", "name": "...", "url": "..." }`, not the raw file content.
- [ ] Multi-select and multi-relation fields store their values as arrays in context.
- [ ] If a Form step is skipped (e.g., because an OR-join condition was met before this branch ran), its context namespace is not present in the context (not `null`, entirely absent).

*Out of scope*
- Saving form submission data directly to a Data Model record automatically — not in MVP (a subsequent Script or HTTP step can do this).

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
> **Gaps vs spec:** context variable population after submission and context variable picker pending E06.
