# Use case — Add a section divider

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Group related fields under a section heading so that the form is easier to understand.

## Primary actor

- Organization Member

## Trigger

- User initiates: group related fields under a section heading

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

## Acceptance Criteria

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-editor | [source](../wireframes/form-editor.excalidraw) | [preview](../wireframes/form-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
