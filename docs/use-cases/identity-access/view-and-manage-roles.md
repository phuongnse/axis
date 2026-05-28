# Use case — View and manage roles

> **Navigation**: [← Identity Access](./README.md)

## Purpose

see all roles in my organization so that I can understand who has what level of access.

## Primary actor

- Organization Admin

## Trigger

- User initiates: see all roles in my organization

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Organization admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

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
- [ ] Roles page lists all roles (system + custom) with: name, description, member count, and permission count.
- [ ] System roles are shown with a "System" badge and no edit/delete actions.
- [ ] Clicking a role opens a detail view showing all assigned permissions grouped by module.

*Validation & errors*
- [ ] Users without `roles:read` permission who navigate to this URL are redirected to the home page with a permission error.

*Edge cases*
- [ ] If an org has no custom roles yet, the list shows only the 4 default system roles with a prompt to create a custom role.

*Out of scope*
- Role hierarchy / role inheritance — not in MVP (flat role model only).

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
> **Gaps vs spec:** member count per role requires a JOIN query — not implemented yet (query projection polish — see gaps below). UI badges and detail view are frontend concerns.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-roles | [source](./wireframes/settings-roles.excalidraw) | [preview](./wireframes/settings-roles.svg) |

[← Back to Identity & Access](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
