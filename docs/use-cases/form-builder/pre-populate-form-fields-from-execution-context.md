# Use case — Pre-populate form fields from execution context

> **Navigation**: [← Form Builder](./README.md)

## Purpose

Pre-populate form fields with values from the workflow context so that assignees don't re-enter data that's already known.

## Primary actor

- Organization Member

## Trigger

- User initiates: pre-populate form fields with values from the workflow context

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Forms are attached to Form steps in a workflow. The engine creates a Form Task and notifies the assignee when the step is reached.

## Acceptance Criteria

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
> **Gaps vs spec:** context expression input UI and expression evaluation at execution time pending Frontend + workflow-engine.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| forms | [source](./wireframes/forms.excalidraw) | [preview](./wireframes/forms.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
