# Use case — Silent token refresh

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

My session to stay active while I'm working so that I'm not interrupted by unexpected sign-out prompts.

## Primary actor

- user

## Trigger

- User initiates: my session to stay active while I'm working

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

*Deferred capabilities*
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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
