# Use case — Create a custom role

> **Navigation**: [← Identity Access](./README.md)

## Purpose

Create a custom role with specific permissions so that I can grant exactly the right level of access to a group of users.

## Primary actor

- Organization Admin

## Trigger

- User initiates: create a custom role with specific permissions

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Organization admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

## Acceptance Criteria

*Happy path*
- [ ] Admin provides a role name and optional description, then selects permissions from a grouped list (grouped by module).
- [ ] Role is created immediately and available for assignment to users.
- [ ] Success message confirms: "Role '{name}' created."

*Validation & errors*
- [ ] Role name: required, 2–50 characters, unique within the org (case-insensitive). Duplicate name shows: "A role with this name already exists."
- [ ] At least one permission must be selected. Submitting with no permissions shows: "A role must have at least one permission."
- [ ] A non-admin calling this API receives HTTP 403.

*Edge cases*
- [ ] Creating two roles with names that differ only in case (e.g., "Manager" and "manager") is blocked.
- [ ] The permissions list is complete and does not require a page reload to reflect newly added platform permissions.

*Out of scope*
- Copying permissions from an existing role as a starting point — not in MVP (user selects permissions manually).

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
> **Gaps vs spec:** 403 permission check requires JWT identity from API layer — pending. Case-insensitive name uniqueness check is done in handler against existing roles in org.
>
> **Decisions:** `Role.CreateCustom(name, orgId, permissions[])` factory method; minimum 1 permission enforced in domain.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-roles | [source](./wireframes/settings-roles.excalidraw) | [preview](./wireframes/settings-roles.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
