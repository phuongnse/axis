# Use case — Sign in to the workspace

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Sign in with my email and password — or with Microsoft, Google, or GitHub — so that I can access my Workspace's workspace.

## Primary actor

- user

## Trigger

- User initiates: sign in with my email and password

## Main flow

1. User opens the sign-in page.
2. User enters email/password or chooses a configured external identity provider.
3. System validates the credential or external provider callback and establishes an Axis session through OpenIddict.
4. SPA exchanges the authorization code with PKCE and stores the access token in memory.
5. User is redirected to the workspace dashboard for their active Workspace.

## Alternate / error flows

- Missing email/password: show inline field errors and do not submit.
- Invalid credential or unknown user: show a generic "Incorrect email or password" message.
- Unverified account: show the verify-email reminder and resend option.
- Deactivated account or missing Workspace access: reject sign-in with the documented account-state message.
- External provider failure or unlinked provider account: return to sign-in with a provider-specific error.

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
- [ ] Signing in to a deactivated account: "Your account has been deactivated. Contact your Workspace admin."
- [ ] Server error (5xx): "Something went wrong. Please try again." — the password field is cleared, email retained.

*External identity providers*
- [ ] Sign-in page offers **Microsoft**, **Google**, and **GitHub** buttons alongside the email/password form ([ADR-027](../../../TECH_STACK.md#adr-027-external-identity-providers-for-user-sign-in-and-registration)).
- [ ] Selecting a provider runs the OAuth2 Authorization Code + PKCE flow through OpenIddict; on return, Axis mints its own access + refresh tokens (the external token is never exposed to the client).
- [ ] A provider login whose verified email matches an existing user attaches to that account rather than creating a duplicate (account linking by verified email).
- [ ] A provider login with no matching account and no pending invitation/setup token is rejected with "No account found. Ask your Workspace admin for an invitation." Provider account setup belongs to a separate provider registration/linking flow.
- [ ] If the provider returns no verified email, sign-in is rejected with "Your <provider> account has no verified email; use email and password instead."
- [ ] A disabled provider (not configured for this deployment) is not shown.

*Edge cases*
- [ ] Signing in on a new browser while already signed in on another does not invalidate the existing session.
- [ ] Email lookup is case-insensitive (`User@Example.com` matches `user@example.com`).
- [ ] Pasting credentials from a password manager works correctly (no interference with autocomplete).
- [ ] Pressing Enter in the password field submits the form.

*Out of scope*
- 2FA / MFA (TOTP or WebAuthn).
- Enterprise SSO federation (SAML, SCIM provisioning, per-workspace IdP) — separate initiative, see [ADR-027](../../../TECH_STACK.md#adr-027-external-identity-providers-for-user-sign-in-and-registration).

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ⚠️ |
> | Infrastructure | ⚠️ |
> | API | ⚠️ |
> | Frontend | ⚠️ |
>
> **Gaps vs spec:** Email/password sign-in has login page + PKCE flow + app shell/dashboard scaffold partially implemented. **External identity providers (Microsoft/Google/GitHub, ADR-027) are spec'd but not yet implemented** — no provider registration in OpenIddict, no account-linking handler, no provider buttons on the sign-in page. BroadcastChannel multi-tab refresh, account lockout UI, and unverified-email screen polish also pending.
>
> **Deferred follow-ups:** 2FA/MFA (TOTP or WebAuthn); enterprise SAML/SCIM federation and per-workspace IdP (ADR-027 enterprise scope).
>
> **Decisions:**
> - OpenIddict 5.x serves as the in-process OAuth2/OIDC server. `AuthenticateUserCommand` validates credentials
> - `/connect/login` sets a 5-min httpOnly session cookie
> - `/connect/authorize` issues the authorization code
> - `/connect/token` exchanges it for access + refresh tokens. Refresh token stored as an opaque reference in DB (OpenIddict `OpenIddictTokens` table) and delivered as an httpOnly `Secure SameSite=Strict` cookie at `/connect` path via `ApplyRefreshTokenCookieHandler`.
>

## Wireframes

Current public sign-in wireframes show the implemented email/password path only. External identity providers remain documented in the use-case spec, but provider buttons should not appear here until ADR-027 is implemented for sign-in.

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| login | [source](./login.excalidraw) | [preview](./login.svg) |
| login-unverified | [source](./login-unverified.excalidraw) | [preview](./login-unverified.svg) |

## Diagrams

### auth-flow

```mermaid
%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%
sequenceDiagram
  actor User as User
  participant Web as Web App
  participant API as API Server (OpenIddict)
  participant Redis as Redis (blacklist)
  participant DB as PostgreSQL

  rect rgb(240, 249, 255)
    Note over User,DB: Sign in
    User->>Web: Enter email + password
    Web->>API: POST /auth/token
    API->>DB: Validate credentials
    API-->>Web: access_token + refresh_token
  end

  rect rgb(240, 249, 255)
    Note over User,DB: Authenticated request
    User->>Web: Navigate / perform action
    Web->>API: GET /resource (Bearer token)
    API->>API: Validate JWT + set workspace
    API-->>Web: 200 OK + data
  end

  rect rgb(240, 249, 255)
    Note over User,DB: Silent token refresh
    Web->>API: POST /auth/refresh (httpOnly cookie)
    API->>DB: Validate + rotate refresh token
    API-->>Web: new access_token + refresh_token
  end

  rect rgb(240, 249, 255)
    Note over User,DB: Sign out
    User->>Web: Click Sign Out
    Web->>API: POST /auth/signout
    API->>Redis: Blacklist access_token JTI
    API->>DB: Revoke refresh token
    API-->>Web: 200 OK
  end
```
