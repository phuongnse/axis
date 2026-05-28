# Use case — Edit a form

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit an existing form so that I can update its fields as requirements change.

## Primary actor

- Organization Member with `form:definition:write`

## Trigger

- User initiates: edit an existing form

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

## Acceptance Criteria

*Happy path*
- [ ] Form editor shows the field list on the left and a live preview on the right.
- [ ] All changes are saved automatically (auto-save with 1-second debounce).

*Validation & errors*
- [ ] Editing a form that is used in one or more published (Active) workflows shows a persistent warning banner: "This form is live in N active workflow(s). Changes take effect immediately for new form task instances."
- [ ] The warning does not block editing — it is informational only.

*Edge cases*
- [ ] Form tasks that are already in-progress (status: PENDING or WAITING) when the form is edited: they use the form definition as it was when the task was created (definition is snapshotted at task creation time).
- [ ] Conflict resolution: if two users edit the same form simultaneously, last save wins on a per-field basis. No conflict detection is required for form field ordering changes.

*Deferred capabilities*
- Form versioning (publishing a new "version" of a form).; edits are live immediately.

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
> **Gaps vs spec:** live-workflow warning banner (informational) and definition snapshot for in-progress tasks pending API + workflow-engine.
>
> **Decisions:**
> - `UpdateFormHandler` checks name uniqueness via `NameExistsAsync(name, orgId, excludeId)` before calling `form.Update()`
> - TOCTOU race requires a unique DB index on `(name, org_id)` at Infrastructure layer.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
