# Use case — Add a section divider

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Group related fields under a section heading so that the form is easier to understand.

## Primary actor

- Tenant Member

## Trigger

- User initiates: group related fields under a section heading

## Main flow

1. Actor starts the — Add a section divider flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

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
- Collapsible sections.

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
>
> **Gaps vs spec:**
> - N/A
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

