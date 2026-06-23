# Use case — Edit a custom role

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Edit an existing custom role so that I can adjust permissions as our needs change.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: edit an existing custom role

## Main flow

1. Actor starts the — Edit a custom role flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workspace admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

## Acceptance Criteria

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
- Permission change notifications to affected users.

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
> **Gaps vs spec:** 403 check pending.
>
> **Done:** system role guard (cannot edit Admin/Editor/Viewer/End User) implemented in domain via `IsSystem` flag.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
