# Use case — Map form submission data into workflow context

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

The data submitted in a form to be available to subsequent steps so that the rest of the process can use it.

## Primary actor

- Workspace Member

## Trigger

- User initiates: the data submitted in a form to be available to subsequent steps

## Main flow

1. Actor starts the — Map form submission data into workflow context flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Forms are attached to Form steps in a workflow. The engine creates a Form Task and notifies the assignee when the step is reached.

## Acceptance Criteria

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
- Saving form submission data directly to a Data Model record automatically — a subsequent Script or HTTP step can do this.

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
> - Execution context stores all submitted field values under `{{context.{step_id}.{field_key}}}` automatically.
> - Subsequent step config panels show the Form step's output fields and types in the context variable picker.
>
> **Deferred follow-ups:**
> - WorkflowEngine context namespace population for submitted Form step values.
> - Frontend context variable picker entries for Form step output fields.
>
> **Decisions:** N/A - no implementation-specific decision recorded for this slice.
>

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
