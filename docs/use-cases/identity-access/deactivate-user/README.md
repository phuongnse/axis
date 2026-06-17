# Use case — Deactivate a user

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Deactivate a user so that they can no longer access the workspace without deleting their history.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: deactivate a user

## Main flow

1. Actor starts the — Deactivate a user flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workspace admins can invite new members, manage their accounts, and deactivate users who should no longer have access.

## Acceptance Criteria

*Happy path*
- [ ] Admin clicks "Deactivate" on a user in the Users list and confirms in a dialog.
- [ ] The user's active sessions are invalidated within 60 seconds (refresh tokens revoked, access tokens blacklisted).
- [ ] Deactivated user appears in the Users list with a "Deactivated" badge.
- [ ] Admin can reactivate the user at any time with a "Reactivate" action.

*Validation & errors*
- [ ] An admin cannot deactivate themselves.
- [ ] Deactivating the last Admin-role user is blocked: "You cannot deactivate the last admin of the Workspace."
- [ ] A non-admin who calls the deactivate API endpoint receives HTTP 403.

*Edge cases*
- [ ] Deactivated user's created content (workflows, models, records) is preserved and attributed to them.
- [ ] A deactivated user who tries to sign in sees: "Your account has been deactivated. Contact your Workspace admin."
- [ ] A deactivated user with pending form tasks: those tasks are marked "Assignee deactivated" and the admin is notified.

*Out of scope*
- Transferring ownership of content from a deactivated user.

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
> **Gaps vs spec:** session revocation (refresh token revoke + access token blacklist) not implemented — auth infrastructure polish pending. Self-deactivation guard and 403 check require current user identity from JWT — pending. Deactivated-user sign-in message handled at auth layer (pending).
>
> **Decisions:** "last admin" check queries `CountAdminsAsync` in the repository before deactivating — domain enforces via `ApplicationException` if violated.
>
> **Deferred follow-ups:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

