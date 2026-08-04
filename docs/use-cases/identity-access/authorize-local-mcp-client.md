# Authorize A Local MCP Client

> **Navigation**: [docs/use-cases/identity-access/README.md](./README.md) · [sign-in-user.md](./sign-in-user.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a local Axis MCP client obtain an access token through the existing browser sign-in session and then operate through the authenticated loopback API boundary.

## Primary actor

- Local Axis MCP client and the account owner completing browser sign-in

## Trigger

- An MCP tool call starts without a valid in-memory access token.

## Main flow

1. MCP creates an OAuth Authorization Code + PKCE request and opens the configured Axis authorization endpoint.
2. OpenIddict validates and stores the request in the distributed cache, exposing only an opaque `request_id` handle.
3. If the browser session is absent, Axis redirects to the configured SPA `/sign-in` route with that opaque handle.
4. The account owner signs in through the existing email/password form; recoverable credential errors retain the pending authorization request.
5. After the browser session is established, the SPA resumes only the fixed `/connect/authorize?request_id=...` endpoint.
6. Axis restores and revalidates the cached request, then sends the authorization code and original OAuth `state` to MCP's registered loopback callback.
7. MCP validates `state`, exchanges the code with its PKCE verifier, and retries the original authenticated operation once through the API.

## Alternate / error flows

- `prompt=none` with no browser session never opens the sign-in screen and returns `login_required` to the registered callback.
- Invalid, expired, tampered, or replayed authorization handles fail closed at the authorization endpoint and never become an arbitrary redirect.
- Invalid credentials, an unverified account, an unavailable workspace, rate limiting, or a transient sign-in failure keeps the pending handle while showing the existing recoverable sign-in state.
- A cache, callback, state, code, or token-exchange failure ends the authorization attempt without issuing an access token; the MCP call reports the bounded authorization failure.
- Normal SPA sign-in without a pending MCP request keeps the existing dashboard handoff.

## Acceptance Criteria

### Happy path

- **AC-001** An unauthenticated interactive MCP authorization request redirects to the configured SPA sign-in route instead of ending at a raw `401` page.
- **AC-002** The browser handoff carries only an opaque, short-lived authorization-request handle; the raw OAuth request is not copied into the SPA URL.
- **AC-003** After successful sign-in, the SPA resumes the fixed API authorization endpoint and the exact registered MCP loopback callback receives the authorization code and original state.
- **AC-004** MCP validates the callback state, exchanges the code with its original PKCE verifier, and the original authenticated tool operation can continue.

### Validation & errors

- **AC-005** `prompt=none` remains non-interactive and returns `login_required` when the browser session is absent.
- **AC-006** Credential and account-readiness failures preserve the pending authorization handle and keep the existing sign-in recovery behavior.
- **AC-007** Missing, expired, tampered, or replayed authorization handles fail closed without an open redirect, token issuance, or arbitrary external navigation.
- **AC-008** Distributed-cache or OAuth callback failures do not issue an access token and remain bounded by the existing MCP authorization timeout.

### Edge cases

- **AC-009** A normal SPA sign-in with no pending MCP request still completes the existing browser PKCE/dashboard handoff and does not use the MCP continuation path.
- **AC-010** The MCP bridge does not expose password, email-verification, or arbitrary OAuth-request tools; browser authorization remains the credential boundary.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | API boundary | Interactive unauthenticated authorization redirects to configured sign-in with an opaque handle | AC-001, AC-002 | API integration test | Yes |
| AT-002 | API boundary | Cached request resumes to the exact registered callback after a browser session is established | AC-003 | API integration test | Yes |
| AT-003 | Browser journey | Local MCP authorization uses the existing sign-in screen and reaches the loopback callback | AC-001, AC-003, AC-004 | Browser automation | Yes |
| AT-004 | UI component | Pending authorization survives recoverable sign-in failures and resumes without a second SPA PKCE flow | AC-003, AC-006 | UI component test | Yes |
| AT-005 | API boundary | Silent authorization returns `login_required` without sign-in UI | AC-005 | API integration test | Yes |
| AT-006 | API boundary | Invalid, expired, tampered, and replayed handles fail closed | AC-007 | API integration test | Yes |
| AT-007 | Infrastructure boundary | Cache or callback failure does not issue a token and remains bounded | AC-008 | API integration test | Yes |
| AT-008 | UI/API boundaries | Normal sign-in remains the dashboard flow and MCP credential tools are absent | AC-009, AC-010 | UI component test+API integration test | Yes |

## Out Of Scope

- Adding password, email-verification, or arbitrary OAuth-request tools to the MCP bridge.
- Changing the registered client scopes, fixed loopback callback, token lifetime, or `prompt=login` semantics.
- Global device sign-out or revocation of already issued short-lived access tokens.

## Screen flow

| Screen | Required contract |
|---|---|
| `/sign-in?authorization_request=...` | Reuse the existing sign-in form. Keep the opaque authorization request across recoverable errors. On successful sign-in, continue the fixed authorization endpoint without rendering a separate callback screen or accepting an arbitrary return URL. |

## Required UI quality

The existing sign-in labels, validation, focus, keyboard, localization, loading, and recovery behavior remain in force. The pending MCP authorization must not be exposed as raw OAuth parameters or a user-editable destination, and a successful technical handoff must not flash a dashboard or callback screen before the authorization endpoint resumes.

## Decisions

- The API/OpenIddict server owns request validation, request caching, redirect-URI validation, PKCE, state, scopes, and callback construction.
- The SPA owns only the sign-in screen and a bounded opaque continuation value; it never reconstructs OAuth parameters.
- MCP owns the PKCE verifier, callback listener, state check, token exchange, and one authentication refresh; it does not own browser credentials.
- OpenIddict's built-in distributed authorization-request cache uses the existing Redis infrastructure with an absolute five-minute policy, matching the local MCP authorization wait window.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | Partial |
> | API | Done |
> | Frontend | Partial |
> | MCP | Partial |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Durable browser automation remains unverified. |
> | GAP-002 | Supported client callback-failure and timeout evidence remain unverified. |
>
> **Deferred follow-ups:** Reauthentication semantics for `prompt=login`, replacing the fixed MCP loopback callback port with an ephemeral port, and OpenIddict package lifecycle review remain outside this checkpoint.
>
> **Verification:** See [authorize-local-mcp-client.evidence.md](./authorize-local-mcp-client.evidence.md). Focused API, frontend, MCP contract, and safety checks pass; the supported Codex client completed browser authorization and authenticated list/read-back.
>
> **Decisions:** OpenIddict owns request validation and distributed caching; the SPA carries only the opaque `request_id`; MCP remains the credential-free PKCE client and does not expose account or OAuth-request tools.
