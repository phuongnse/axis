# Use case — Sign in with email and password

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Sign in with my email and password so that I can access my organization's workspace.

## Primary actor

- user

## Trigger

- User initiates: sign in with my email and password

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Secure sign-in and sign-out flows using JWT access tokens and opaque refresh tokens. Built on OpenIddict, fully self-hosted.

## Acceptance Criteria

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| auth-flow | [source](./auth-flow.excalidraw) | [preview](./auth-flow.svg) |
