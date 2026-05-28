# Use case — Delete a form

> **Navigation**: [← Form Builder](./README.md)

## Purpose

delete a form so that I can clean up unused forms.

## Primary actor

- Organization Member with `form:definition:write`

## Trigger

- User initiates: delete a form

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

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
- [ ] Confirmation dialog requires typing the form name.
- [ ] After confirmation, the form is soft-deleted and removed from the forms list.

*Validation & errors*
- [ ] A form referenced by any workflow step (Draft or Active) cannot be deleted. Error message lists which workflows reference it: "This form is used by: [Workflow A (step: Approval), Workflow B (step: Intake)]. Remove those references first."
- [ ] Attempting to delete via API while still referenced returns HTTP 409 Conflict.

*Edge cases*
- [ ] A form can be deleted if it is only referenced by Archived workflows (since archived workflows cannot be triggered).
- [ ] Soft-deleted forms are permanently purged after 30 days.

*Out of scope*
- Recovering a soft-deleted form — not in MVP.

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
> - HTTP 409 on delete-while-referenced enforced via `IsReferencedByWorkflowAsync` JSONB query across `workflow_definitions.steps`
> - archived-workflow exception backend polish — see gaps below.
>
> **Decisions:** `IsReferencedByWorkflowAsync` uses raw SQL `workflow_definitions.steps @> [{...}]::jsonb` — cross-module table query within the same tenant schema.


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
