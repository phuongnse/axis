# Use case — Link a form to a workflow Form step

> **Navigation**: [← Form Builder](./README.md)

## Purpose

select a form when configuring a Form step so that the right form is presented to the assignee during execution.

## Primary actor

- Organization Member

## Trigger

- User initiates: select a form when configuring a Form step

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Forms are attached to Form steps in a workflow. The engine creates a Form Task and notifies the assignee when the step is reached.

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| forms | [source](./wireframes/forms.excalidraw) | [preview](./wireframes/forms.svg) |

[← Back to Form Builder](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
