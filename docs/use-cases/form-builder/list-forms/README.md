# Use case — View all forms

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See all forms in my Workspace so that I can find existing forms to reuse.

## Primary actor

- Workspace Member with `form:definition:read`

## Trigger

- User initiates: see all forms in my Workspace

## Main flow

1. Actor starts the — View all forms flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

## Acceptance Criteria

*Happy path*
- [ ] Forms list shows: name, field count, last modified date, and the number of workflow steps currently using the form ("Used in N workflow(s)").
- [ ] Search by name (real-time, client-side).
- [ ] Clicking a form opens it in the form editor (read-only for users without write permission).

*Validation & errors*
- [ ] Empty state: "No forms yet. Create your first form."
- [ ] Users without `form:definition:read` who navigate to this URL are redirected to home.

*Edge cases*
- [ ] "Used in N workflow(s)" count includes both Draft and Active workflows, as both can reference forms.

*Out of scope*
- Folders/categories for arranging forms.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
> - "Used in N workflow(s)" count pending cross-module query — not supported at Application layer without inter-module dependency
> - deferred to API/Frontend aggregation.
>
> **Decisions:** `GetFormsHandler` paginates in-memory (GetAllAsync + LINQ Skip/Take). This is an accepted trade-off at this scale: adding a `GetPagedAsync` repository method would push sorting/paging logic into Infrastructure without additional correctness benefit at this scale. `Page` and `PageSize` are clamped to ≥ 1 and ≤ 100 in the handler.
>
> **Deferred follow-ups:**
> - N/A
