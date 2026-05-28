# Use case — Change password while signed in

> **Navigation**: [← Identity Access](./README.md)

## Purpose

change my password while signed in so that I can keep my account secure.

## Primary actor

- user

## Trigger

- User initiates: change my password while signed in

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
- Password history check (cannot reuse last N passwords) — not in MVP.

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
> **Gaps vs spec:** failed-attempt lockout for change-password form (3 attempts / 15 min) backend polish — see gaps below. Revoking other-device sessions after change backend polish — see gaps below (OpenIddict token revocation).
>
> **Decisions:** notification email failure is swallowed at handler level and logged separately at Infrastructure — password change still succeeds per [password-security](../identity-access/README.md) acceptance criteria.

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
