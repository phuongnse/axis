# Use case — Delete a Draft workflow

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

delete a Draft workflow so that I can permanently remove workflows I no longer need without having to publish them first.

## Primary actor

- Organization Member with `workflow:definition:write`

## Trigger

- User initiates: delete a Draft workflow

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

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
- [ ] Deleting a Draft workflow removes it from the list permanently (soft-deleted; not recoverable via the UI).
- [ ] Delete returns HTTP 204 No Content on success.

*Validation & errors*
- [ ] Only `Draft` workflows can be deleted. Attempting to delete an `Active` or `Archived` workflow returns HTTP 422: "Only draft workflows can be deleted."
- [ ] Attempting to delete a workflow that does not exist returns HTTP 404.

*Out of scope*
- Hard delete / permanent purge — not in MVP.
- Bulk delete — not in MVP.

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


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflows | [source](./wireframes/workflows.excalidraw) | [preview](./wireframes/workflows.svg) |

[← Back to Workflow Builder](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
