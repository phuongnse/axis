# Use case — Assign a role to a user

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Assign a role to a user so that they get the appropriate permissions.

## Primary actor

- Organization Admin

## Trigger

- User initiates: assign a role to a user

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
- Time-limited role assignments (e.g., "grant admin for 24 hours") — not in MVP.

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
