# Use case — Edit a model

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit an existing model so that I can add, remove, or rename fields as requirements evolve.

## Primary actor

- Organization Member with `data_modeling:model:write`

## Trigger

- User initiates: edit an existing model

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

## Acceptance Criteria

*Happy path*
- [ ] User can update: name, description, icon, and color from the model settings panel.
- [ ] User can add, edit, reorder, and delete fields from the field editor.
- [ ] All changes are saved on explicit "Save" action (not auto-save, to prevent accidental data loss).

*Validation & errors*
- [ ] Renaming a model follows the same uniqueness and format rules as creation.
- [ ] Deleting a field that contains data on existing records shows a confirmation dialog: "Deleting '{field_name}' will permanently remove this data from all {N} records. This cannot be undone."
- [ ] Adding a required field to a model that already has records requires the user to provide a default value; without it, submission is blocked.
- [ ] If the model is referenced by an active workflow step, editing it shows an informational warning (not a blocker): "This model is used in N workflow(s). Changes may affect their behavior."

*Edge cases*
- [ ] Two admins editing the same model simultaneously: last save wins. The first admin's changes are not silently overwritten without notice — on save, if the server detects a version conflict (via `updated_at` comparison), it returns HTTP 409 with: "This model was modified by someone else. Please refresh and reapply your changes."
- [ ] Renaming a field does not lose existing data; the underlying storage key is the field's immutable `id`, not its name.

*Deferred capabilities*
- Undo history for field changes.

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
> - HTTP 409 version-conflict check pending (updated_at comparison)
> - active-workflow warning pending workflow-builder integration.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
