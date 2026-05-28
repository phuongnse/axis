# Use case — View all forms

> **Navigation**: [← Form Builder](./README.md)

## Purpose

see all forms in my organization so that I can find existing forms to reuse.

## Primary actor

- Organization Member with `form:definition:read`

## Trigger

- User initiates: see all forms in my organization

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
- [ ] Forms list shows: name, field count, last modified date, and the number of workflow steps currently using the form ("Used in N workflow(s)").
- [ ] Search by name (real-time, client-side).
- [ ] Clicking a form opens it in the form editor (read-only for users without write permission).

*Validation & errors*
- [ ] Empty state: "No forms yet. Create your first form."
- [ ] Users without `form:definition:read` who navigate to this URL are redirected to home.

*Edge cases*
- [ ] "Used in N workflow(s)" count includes both Draft and Active workflows, as both can reference forms.

*Out of scope*
- Folders/categories for organizing forms — not in MVP.

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
> **Decisions:** `GetFormsHandler` paginates in-memory (GetAllAsync + LINQ Skip/Take). This is an accepted trade-off for MVP: adding a `GetPagedAsync` repository method would push sorting/paging logic into Infrastructure without additional correctness benefit at this scale. `Page` and `PageSize` are clamped to ≥ 1 and ≤ 100 in the handler.

---

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
