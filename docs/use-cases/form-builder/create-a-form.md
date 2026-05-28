# Use case — Create a form

> **Navigation**: [← Form Builder](./README.md)

## Purpose

create a new form so that I can design a data collection interface.

## Primary actor

- Organization Member with `form:definition:write`

## Trigger

- User initiates: create a new form

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

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
