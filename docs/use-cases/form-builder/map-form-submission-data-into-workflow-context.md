# Use case — Map form submission data into workflow context

> **Navigation**: [← Form Builder](./README.md)

## Purpose

the data submitted in a form to be available to subsequent steps so that the rest of the process can use it.

## Primary actor

- Organization Member

## Trigger

- User initiates: the data submitted in a form to be available to subsequent steps

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
> **Gaps vs spec:** context variable population after submission and context variable picker pending workflow-engine.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

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
