# Use case — View and revoke active sessions

> **Navigation**: [← Identity Access](./README.md)

## Purpose

see where I'm currently signed in so that I can revoke access from devices I no longer use.

## Primary actor

- user

## Trigger

- User initiates: see where I'm currently signed in

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Allow users to reset forgotten passwords, change their current password, and manage active sessions.

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
- [ ] Sessions page lists active sessions with: device type (inferred from User-Agent), approximate location (IP-based country), last activity time, and a "This device" indicator for the current session.
- [ ] User can click "Revoke" on any session except the current one to invalidate it immediately.
- [ ] "Sign out everywhere" revokes all sessions including the current one and redirects to the sign-in page.

*Validation & errors*
- [ ] If the sessions list fails to load, the page shows an error with a retry button.
- [ ] Revoking a session that has already expired shows: "This session has already ended."

*Edge cases*
- [ ] Sessions are shown in reverse-chronological order by last activity.
- [ ] A session with no recent activity (> 7 days) is still shown if the refresh token hasn't expired.
- [ ] IP geolocation may be unavailable for some IPs; in that case location shows "Unknown."
- [ ] The current session cannot be revoked from this page (use Sign Out instead); the Revoke button is replaced with "Current session."

*Out of scope*
- Per-session device naming by the user — not in MVP.

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
> - `ISessionStore` interface defined in Application
> - implementation pending Infrastructure (OpenIddict token manager wrapper). Session list UI, revoke button, and "sign out everywhere" pending Frontend + API.
>
> **Decisions:**
> - sessions are modelled as OpenIddict refresh tokens
> - `ISessionStore` wraps `IOpenIddictTokenManager` at Infrastructure layer. `RevokeSessionCommand(sessionId: null)` triggers "revoke all" via `RevokeAllAsync`.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-security | [source](./wireframes/settings-security.excalidraw) | [preview](./wireframes/settings-security.svg) |
| forgot-password | [source](./wireframes/forgot-password.excalidraw) | [preview](./wireframes/forgot-password.svg) |
| change-password | [source](./wireframes/change-password.excalidraw) | [preview](./wireframes/change-password.svg) |

[← Back to Identity & Access](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
