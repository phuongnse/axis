# Use case — View and revoke active sessions

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See where I'm currently signed in so that I can revoke access from devices I no longer use.

## Primary actor

- user

## Trigger

- User initiates: see where I'm currently signed in

## Main flow

1. Signed-in user opens the active sessions page.
2. System lists active refresh-token sessions with device, approximate location, last activity, and current-session marker.
3. User revokes another session or signs out everywhere.

## Alternate / error flows

- Sessions load failure shows a retryable error.
- Revoking an already-ended session shows a terminal message.
- Missing IP geolocation renders as "Unknown."
- The current session cannot be revoked from this page; user must use Sign Out.

## Context

Allow users to reset forgotten passwords, change their current password, and manage active sessions.

## Acceptance Criteria

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
- Per-session device naming by the user.

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
>
> **Deferred follow-ups:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
