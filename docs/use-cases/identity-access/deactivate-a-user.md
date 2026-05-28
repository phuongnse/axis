# Use case — Deactivate a user

> **Navigation**: [← Identity Access](./README.md)

## Purpose

deactivate a user so that they can no longer access the workspace without deleting their history.

## Primary actor

- Organization Admin

## Trigger

- User initiates: deactivate a user

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Organization admins can invite new members, manage their accounts, and deactivate users who should no longer have access.

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
- [ ] Admin clicks "Deactivate" on a user in the Users list and confirms in a dialog.
- [ ] The user's active sessions are invalidated within 60 seconds (refresh tokens revoked, access tokens blacklisted).
- [ ] Deactivated user appears in the Users list with a "Deactivated" badge.
- [ ] Admin can reactivate the user at any time with a "Reactivate" action.

*Validation & errors*
- [ ] An admin cannot deactivate themselves.
- [ ] Deactivating the last Admin-role user is blocked: "You cannot deactivate the last admin of the organization."
- [ ] A non-admin who calls the deactivate API endpoint receives HTTP 403.

*Edge cases*
- [ ] Deactivated user's created content (workflows, models, records) is preserved and attributed to them.
- [ ] A deactivated user who tries to sign in sees: "Your account has been deactivated. Contact your organization admin."
- [ ] A deactivated user with pending form tasks: those tasks are marked "Assignee deactivated" and the admin is notified.

*Out of scope*
- Transferring ownership of content from a deactivated user — not in MVP.

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
> **Gaps vs spec:** session revocation (refresh token revoke + access token blacklist) not implemented — auth infrastructure polish — see gaps below. Self-deactivation guard and 403 check require current user identity from JWT — backend polish — see gaps below. Deactivated-user sign-in message handled at auth layer (pending).
>
> **Decisions:** "last admin" check queries `CountAdminsAsync` in the repository before deactivating — domain enforces via `ApplicationException` if violated.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-users | [source](./wireframes/settings-users.excalidraw) | [preview](./wireframes/settings-users.svg) |
| accept-invitation | [source](./wireframes/accept-invitation.excalidraw) | [preview](./wireframes/accept-invitation.svg) |

[← Back to Identity & Access](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
