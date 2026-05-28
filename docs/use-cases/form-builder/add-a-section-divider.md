# Use case — Add a section divider

> **Navigation**: [← Form Builder](./README.md)

## Purpose

group related fields under a section heading so that the form is easier to understand.

## Primary actor

- Organization Member

## Trigger

- User initiates: group related fields under a section heading

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

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
> **Gaps vs spec:** section grouping visual rendering pending Frontend.
>
> **Decisions:** sections use `FormFieldType.Section` + `SectionFieldConfig` and flow through the same `AddFieldToFormCommand` as regular fields.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-editor | [source](./wireframes/form-editor.excalidraw) | [preview](./wireframes/form-editor.svg) |

[← Back to Form Builder](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
