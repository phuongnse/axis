# Use Case Group — Authentication

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| login | [source](./wireframes/login.excalidraw) | [preview](./wireframes/login.svg) |
| login-unverified | [source](./wireframes/login-unverified.excalidraw) | [preview](./wireframes/login-unverified.svg) |
| register | [source](./wireframes/register.excalidraw) | [preview](./wireframes/register.svg) |

[← Back to Identity & Access](./README.md)

---

## Description

Secure sign-in and sign-out flows using JWT access tokens and opaque refresh tokens. Built on OpenIddict, fully self-hosted.

---

## Use Cases

### Use case — Sign in with email and password

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want to** sign in with my email and password **so that** I can access my organization's workspace.

**Acceptance Criteria:**

*Happy path*
- [ ] Sign-in form accepts email and password.
- [ ] On success, the client receives an access token (JWT, 15-min TTL) stored in memory and a refresh token (7-day TTL) stored in an `httpOnly`, `Secure`, `SameSite=Strict` cookie.
- [ ] User is redirected to their workspace dashboard.

*Validation & errors*
- [ ] Empty email or password: inline field errors, form does not submit.
- [ ] Invalid credentials: generic message "Incorrect email or password" (no indication of which field is wrong).
- [ ] After 5 consecutive failed sign-in attempts within 15 minutes, the account is temporarily locked for 15 minutes. Subsequent attempts show "Too many failed attempts. Try again after [time]."
- [ ] Signing in to an unverified account: "Please verify your email before signing in." with a resend link.
- [ ] Signing in to a deactivated account: "Your account has been deactivated. Contact your organization admin."
- [ ] Server error (5xx): "Something went wrong. Please try again." — the password field is cleared, email retained.

*Edge cases*
- [ ] Signing in on a new browser while already signed in on another does not invalidate the existing session.
- [ ] Email lookup is case-insensitive (`User@Example.com` matches `user@example.com`).
- [ ] Pasting credentials from a password manager works correctly (no interference with autocomplete).
- [ ] Pressing Enter in the password field submits the form.

*Out of scope*
- SSO / social login (Google, GitHub) — not in MVP.
- 2FA / MFA — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⚠️ |
>
> **Gaps vs spec:** Login page + PKCE flow + app shell/dashboard scaffold on PR #50 branch. BroadcastChannel multi-tab refresh, account lockout UI, and unverified-email screen polish pending.
>
> **Deferred (PR #50 follow-up):** none for this US.
>
> **Decisions:**
> - OpenIddict 5.x serves as the in-process OAuth2/OIDC server. `AuthenticateUserCommand` validates credentials
> - `/connect/login` sets a 5-min httpOnly session cookie
> - `/connect/authorize` issues the authorization code
> - `/connect/token` exchanges it for access + refresh tokens. Refresh token stored as an opaque reference in DB (OpenIddict `OpenIddictTokens` table) and delivered as an httpOnly `Secure SameSite=Strict` cookie at `/connect` path via `ApplyRefreshTokenCookieHandler`.

---

### Use case — Silent token refresh

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want** my session to stay active while I'm working **so that** I'm not interrupted by unexpected sign-out prompts.

**Acceptance Criteria:**

*Happy path*
- [ ] At 80% of the access token's TTL (~12 minutes), the client automatically calls `POST /auth/refresh` using the refresh token cookie.
- [ ] On success, the new access token replaces the old one in memory. The refresh token cookie is rotated (old invalidated, new set).
- [ ] The refresh happens in the background; the user experiences no interruption or UI change.

*Validation & errors*
- [ ] If the refresh token is expired: the user is redirected to the sign-in page with the message "Your session has expired. Please sign in again."
- [ ] If the refresh token has been revoked (e.g., sign-out from another tab): same redirect behavior.
- [ ] If the refresh API call fails due to a network error, the client retries up to 3 times with exponential backoff before giving up and redirecting to sign-in.

*Edge cases*
- [ ] Multiple browser tabs open simultaneously: only one tab fires the refresh at a time (coordinated via a BroadcastChannel lock). All tabs share the new token.
- [ ] A refresh token can only be used once (rotation). If the old refresh token is replayed (e.g., by an attacker who stole it), it is rejected and all sessions for that user are invalidated.

*Out of scope*
- Server-side session management (stateful sessions) — access is stateless JWT-based.

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
> **Gaps vs spec:** BroadcastChannel multi-tab coordination is Frontend-only. Endpoint is `POST /connect/token` (grant_type=refresh_token) instead of `/api/auth/refresh`.
>
> **Decisions:**
> - OpenIddict handles token rotation natively. `ExtractRefreshTokenFromCookieHandler` reads the refresh token from the httpOnly cookie into the OpenIddict request. `POST /connect/token` with grant_type=refresh_token validates the opaque reference token, loads fresh user+permissions, rotates the refresh token, returns a new access token in the JSON body and a new refresh token cookie. Replay detection: reference tokens are single-use
> - replaying revoked token returns `invalid_grant`.

---

### Use case — Sign out

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** user, **I want to** sign out **so that** my session is terminated and no one else can use my account from this device.

**Acceptance Criteria:**

*Happy path*
- [ ] Clicking "Sign out" calls `POST /auth/signout`, which revokes the refresh token server-side and adds the access token's JTI to a Redis blacklist (TTL = remaining access token lifetime).
- [ ] The refresh token cookie is cleared from the browser.
- [ ] The access token is cleared from client memory.
- [ ] User is redirected to the sign-in page.

*Validation & errors*
- [ ] If the sign-out API call fails (network error), the client still clears local tokens and redirects to sign-in. The refresh token will expire naturally.

*Edge cases*
- [ ] After sign-out, the browser Back button does not restore access to protected pages (protected routes check for valid token on every render).
- [ ] Using the old access token after sign-out returns HTTP 401 (blacklist check).
- [ ] Sign-out from one browser tab notifies other open tabs (via BroadcastChannel) to also clear their token and redirect.

*Out of scope*
- "Sign out of all devices" from this flow — covered in [F05 Password & Security](./password-security.md).

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
> **Decisions:** `POST /api/auth/signout` [Authorize] reads the opaque refresh token from the httpOnly cookie, calls `IOpenIddictTokenManager.FindByReferenceIdAsync` + `TryRevokeAsync` to revoke it in DB, blacklists the access token JTI in Redis via `IJtiBlacklist` (TTL = remaining access token lifetime), clears the refresh token cookie and the PKCE session cookie. No Application handler — pure API/Infrastructure concern.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
