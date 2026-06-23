# Use case — Change password while signed in

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Change my password while signed in so that I can keep my account secure.

## Primary actor

- user

## Trigger

- User initiates: change my password while signed in

## Main flow

1. Signed-in user opens Security settings and submits current password, new password, and confirmation.
2. System validates the current password and new password policy.
3. System changes the password, revokes other active refresh tokens, keeps the current session active, and sends a confirmation email.

## Alternate / error flows

- Incorrect current password highlights the current-password field and clears new-password fields.
- Repeated current-password failures lock the change-password form temporarily.
- Confirmation email failure is logged but does not roll back the password change.
- A stale form in another tab fails because the current password has already changed.

## Context

Allow users to reset forgotten passwords, change their current password, and manage active sessions.

## Acceptance Criteria

*Happy path*
- [ ] Change password form in Security settings requires: current password, new password, and new password confirmation.
- [ ] After a successful change, all other active refresh tokens (other devices) are revoked. The current session remains active.
- [ ] A confirmation email is sent notifying: "Your password was changed. If this wasn't you, contact support immediately."

*Validation & errors*
- [ ] Incorrect current password: "Current password is incorrect." (current password field highlighted, new password fields cleared).
- [ ] New password same as current password: "New password must be different from your current password."
- [ ] New password violates rules or confirmation doesn't match: inline errors per field.
- [ ] After 3 failed current-password attempts within 10 minutes, the change-password form is locked for 15 minutes.

*Edge cases*
- [ ] If the confirmation email fails to send, the password change still succeeds and is logged; the email failure is logged separately.
- [ ] Changing password while another tab has the change-password form open: the other tab's form submission will fail with "Current password is incorrect" (since the password was already changed).

*Out of scope*
- Password history check (cannot reuse last N passwords).

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| change-password | [source](./change-password.excalidraw) | [preview](./change-password.svg) |

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
> **Gaps vs spec:** failed-attempt lockout for change-password form (3 attempts / 15 min) pending. Revoking other-device sessions after change pending (OpenIddict token revocation).
>
> **Decisions:** notification email failure is swallowed at handler level and logged separately at Infrastructure — password change still succeeds per [password-security](../README.md) acceptance criteria.
>
> **Deferred follow-ups:**
> - N/A
