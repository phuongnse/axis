# Use case — Reset forgotten password

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Reset my password via email so that I can regain access to my account if I forget it.

## Primary actor

- user

## Trigger

- User initiates: reset my password via email

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow users to reset forgotten passwords, change their current password, and manage active sessions.

## Acceptance Criteria

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

*Deferred capabilities*
- Security questions as a backup reset method.

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
> **Gaps vs spec:** rate limiting (max 3 requests/hour) is an API/Infrastructure concern — pending. Auto sign-in after reset pending.
>
> **Decisions:**
> - reset token is a cryptographically random 32-byte value
> - stored as SHA-256 hash in `password_reset_tokens` table
> - raw token sent by email. New request invalidates all prior tokens for the user. Token lifetime: 1 hour.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| forgot-password | [source](./forgot-password.excalidraw) | [preview](./forgot-password.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
