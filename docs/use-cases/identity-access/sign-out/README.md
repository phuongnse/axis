# Use case — Sign out

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Sign out so that my session is terminated and no one else can use my account from this device.

## Primary actor

- user

## Trigger

- User initiates: sign out

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

*Deferred capabilities*
- "Sign out of all devices" from this flow — covered in [[password-security](./README.md) Password & Security](./password-security.md).

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
