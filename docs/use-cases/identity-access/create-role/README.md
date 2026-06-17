# Use case — Create a custom role

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Create a custom role with specific permissions so that I can grant exactly the right level of access to a group of users.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: create a custom role with specific permissions

## Main flow

1. Actor starts the — Create a custom role flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workspace admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

## Acceptance Criteria

*Happy path*
- [ ] Admin provides a role name and optional description, then selects permissions from a grouped list (grouped by module).
- [ ] Role is created immediately and available for assignment to users.
- [ ] Success message confirms: "Role '{name}' created."

*Validation & errors*
- [ ] Role name: required, 2–50 characters, unique within the workspace (case-insensitive). Duplicate name shows: "A role with this name already exists."
- [ ] At least one permission must be selected. Submitting with no permissions shows: "A role must have at least one permission."
- [ ] A non-admin calling this API receives HTTP 403.

*Edge cases*
- [ ] Creating two roles with names that differ only in case (e.g., "Manager" and "manager") is blocked.
- [ ] The permissions list is complete and does not require a page reload to reflect newly added platform permissions.

*Out of scope*
- Copying permissions from an existing role as a starting point — user selects permissions manually.

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
> **Gaps vs spec:** 403 permission check requires JWT identity from API layer — pending. Case-insensitive name uniqueness check is done in handler against existing roles in workspace.
>
> **Decisions:** `Role.CreateCustom(name, WorkspaceId, permissions[])` factory method; minimum 1 permission enforced in domain.
>
> **Deferred follow-ups:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

