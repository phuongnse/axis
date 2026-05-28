# Use case — Edit a custom role

> **Navigation**: [← Identity Access](./README.md)

## Purpose

edit an existing custom role so that I can adjust permissions as our needs change.

## Primary actor

- Organization Admin

## Trigger

- User initiates: edit an existing custom role

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
- [ ] Admin can modify: name, description, and permissions of any custom role.
- [ ] Changes are saved immediately.
- [ ] Permission changes take effect for affected users on their next API request (their JWT is re-validated against current role permissions).

*Validation & errors*
- [ ] Same name validation rules as create apply.
- [ ] System roles (Admin, Editor, Viewer, End User) cannot be edited; the edit action is not available for them.
- [ ] If the API call fails, the form retains unsaved changes and shows an error.

*Edge cases*
- [ ] Removing a permission from a role affects all users currently holding that role simultaneously.
- [ ] A user whose active session loses a permission they were relying on will receive HTTP 403 on their next request for that resource; the UI re-fetches their permissions and updates accordingly.

*Out of scope*
- Permission change notifications to affected users — not in MVP.

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
> **Gaps vs spec:** system role guard (cannot edit Admin/Editor/Viewer/End User) implemented in domain via `IsSystem` flag. 403 check backend polish — see gaps below.

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
