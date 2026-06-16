# Use case — Delete a form

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Delete a form so that I can clean up unused forms.

## Primary actor

- Tenant Member with `form:definition:write`

## Trigger

- User initiates: delete a form

## Main flow

1. Actor starts the — Delete a form flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

## Acceptance Criteria

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
- Recovering a soft-deleted form.

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
> **Gaps vs spec:** archived-workflow exception pending.
>
> **Done:** HTTP 409 on delete-while-referenced enforced via `IsReferencedByWorkflowAsync` JSONB query across `workflow_definitions.steps`.
>
> **Decisions:** `IsReferencedByWorkflowAsync` uses raw SQL `workflow_definitions.steps @> [{...}]::jsonb` — cross-module table query within the same tenant schema.
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

