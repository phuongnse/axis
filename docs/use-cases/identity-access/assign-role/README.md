# Use case — Assign a role to a user

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Assign a role to a user so that they get the appropriate permissions.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: assign a role to a user

## Main flow

1. Actor starts the — Assign a role to a user flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workspace admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

## Acceptance Criteria

*Happy path*
- [ ] On the user's profile page, admin can add one or more roles and remove existing ones.
- [ ] Changes take effect on the user's next token refresh (within 15 minutes of the access token TTL).

*Validation & errors*
- [ ] A user must always have at least one role; removing the last role is blocked: "A user must have at least one role."
- [ ] The last user with the Admin role cannot have it removed: "This is the last admin. Assign admin to another user first."
- [ ] A non-admin calling the assign-role API receives HTTP 403.

*Edge cases*
- [ ] If the affected user is currently online, they see a subtle "Your permissions have been updated" banner on their next API response that includes a 403 or on next navigation.
- [ ] A user can hold multiple roles simultaneously; their effective permissions are the union of all role permissions.

*Out of scope*
- Time-limited role assignments (e.g., "grant admin for 24 hours").

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
> **Done:** "At least one role" guard and "last admin" guard both implemented in handler.
>
> **Decisions:** roles stored as `List<Guid>` (`_roleIds`) on `User` aggregate — effective permissions are the union of all assigned roles' permission lists, computed at token issuance time (pending auth layer).
>
> **Deferred follow-ups:**
> - N/A
