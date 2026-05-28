# Use case — Reset forgotten password

> **Navigation**: [← Identity Access](./README.md)

## Purpose

reset my password via email so that I can regain access to my account if I forget it.

## Primary actor

- user

## Trigger

- User initiates: reset my password via email

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
- [ ] "Forgot password?" link on the sign-in page opens a form to enter an email address.
- [ ] Submitting shows: "If this email is registered, you'll receive a reset link shortly." (same message regardless of whether the email exists — no enumeration).
- [ ] Reset email arrives within 60 seconds with a link valid for 1 hour.
- [ ] Clicking the link opens a form to set a new password (with confirmation).
- [ ] After a successful reset, all existing refresh tokens for the user are revoked and the user is signed in automatically.

*Validation & errors*
- [ ] Empty email: inline validation error, form does not submit.
- [ ] Invalid email format: inline validation error.
- [ ] Expired reset link: "This reset link has expired. Please request a new one."
- [ ] Already-used reset link: "This link has already been used. Please sign in or request a new reset link."
- [ ] New password must meet the same rules as registration (min 8 chars, letter + number); violations shown inline.
- [ ] New password and confirmation must match; mismatch shown inline.

*Edge cases*
- [ ] Reset is rate-limited: max 3 reset requests per email per hour. Subsequent requests within the window show the same generic message (no indication of rate limit to prevent enumeration).
- [ ] Requesting a second reset link before the first expires invalidates the first link.
- [ ] A user resets their password while still signed in on another device: that device's session is invalidated.

*Out of scope*
- Security questions as a backup reset method — not in MVP.

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
> **Gaps vs spec:** rate limiting (max 3 requests/hour) is an API/Infrastructure concern — backend polish — see gaps below. Auto sign-in after reset backend polish — see gaps below.
>
> **Decisions:**
> - reset token is a cryptographically random 32-byte value
> - stored as SHA-256 hash in `password_reset_tokens` table
> - raw token sent by email. New request invalidates all prior tokens for the user. Token lifetime: 1 hour.

---

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
