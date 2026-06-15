# Use case — Delete a Draft workflow

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Delete a Draft workflow so that I can permanently remove workflows I no longer need without having to publish them first.

## Primary actor

- Organization Member with `workflow:definition:write`

## Trigger

- User initiates: delete a Draft workflow

## Main flow

1. Member opens a Draft workflow and chooses Delete.
2. System confirms the workflow is still in Draft status, soft-deletes it, and returns HTTP 204.
3. Member returns to the workflow list and the deleted draft no longer appears.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

## Acceptance Criteria

*Happy path*
- [ ] Deleting a Draft workflow removes it from the list permanently (soft-deleted; not recoverable via the UI).
- [ ] Delete returns HTTP 204 No Content on success.

*Validation & errors*
- [ ] Only `Draft` workflows can be deleted. Attempting to delete an `Active` or `Archived` workflow returns HTTP 422: "Only draft workflows can be deleted."
- [ ] Attempting to delete a workflow that does not exist returns HTTP 404.

*Out of scope*
- Hard delete / permanent purge.
- Bulk delete.

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
> **Gaps vs spec:** soft-delete only; restore-from-archive UI pending Frontend.
>
> **Deferred follow-ups:** Hard delete / permanent purge; bulk delete.
>
> **Decisions:** Draft deletion remains a soft-delete operation; no restore UI is exposed for deleted drafts.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
