# Use Case Group — Password & Security Management

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-security | [source](../../epics/E02-identity-access/wireframes/settings-security.excalidraw) | [preview](../../epics/E02-identity-access/wireframes/settings-security.svg) |
| forgot-password | [source](../../epics/E02-identity-access/wireframes/forgot-password.excalidraw) | [preview](../../epics/E02-identity-access/wireframes/forgot-password.svg) |
| change-password | [source](../../epics/E02-identity-access/wireframes/change-password.excalidraw) | [preview](../../epics/E02-identity-access/wireframes/change-password.svg) |

[← Back to E02-identity-access](../../epics/E02-identity-access/README.md)

---

## Description

Allow users to reset forgotten passwords, change their current password, and manage active sessions.

---

## Use Cases

### Use case — Reset forgotten password

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want to** reset my password via email **so that** I can regain access to my account if I forget it.

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

### Use case — Change password while signed in

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want to** change my password while signed in **so that** I can keep my account secure.

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
> **Decisions:** notification email failure is swallowed at handler level and logged separately at Infrastructure — password change still succeeds per US-028 AC.

---

### Use case — View and revoke active sessions

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want to** see where I'm currently signed in **so that** I can revoke access from devices I no longer use.

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
