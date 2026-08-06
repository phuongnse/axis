# Identity Access Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [Identity Access use cases](../use-cases/identity-access/README.md) · [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

This file owns durable Identity Access session and authorization realization. Use cases own account-owner goals and observable outcomes; [docs/TECH_STACK.md](../TECH_STACK.md) owns the approved providers and libraries.

## Browser session realization

- Axis browser authentication uses one same-origin opaque server session. Browser code receives no OAuth access token, refresh token, authorization code, session ticket identifier, or credential-bearing callback state.
- The session ticket is rotated at establishment and stored in the configured distributed server session store. The cookie contains only an opaque identifier and is Secure, HttpOnly, SameSite=Lax, host-only, path `/`, with validated idle and absolute lifetimes and no sticky-session dependence.
- Session bootstrap resolves authenticated identity, active Workspace context, and antiforgery material once per explicit application entry. Route intent preloads do not authenticate, refresh, or mutate session state.
- Cookie-authenticated unsafe requests require antiforgery evidence. Bearer-authenticated public API clients use their independent authorization boundary.
- Missing, expired, invalid, or unavailable session state fails closed, clears stale client identity when possible, and exposes only sign-in or a generic retryable unavailable outcome.
- Current-browser sign-out deletes only that server ticket, expires the exact cookie, and clears authenticated client state after deletion succeeds. Other browser, device, MCP, and confidential-product sessions are independent.

## Local MCP authorization realization

- The local MCP bridge is a public OAuth Authorization Code client with PKCE. It owns the verifier, callback listener, `state` validation, code exchange, in-memory access token, bounded authorization wait, and one authenticated-operation retry; it never owns browser credentials.
- The authorization server validates and caches the request behind a short-lived opaque `request_uri`, binds it to the registered public `client_id`, and owns redirect validation, scopes, callback construction, code issuance, and token exchange.
- The sign-in UI carries only the validated handle/client pair through recoverable credential states and resumes one fixed authorization endpoint. It never reconstructs raw OAuth parameters or accepts an arbitrary return target.
- The current implementation uses the approved authorization provider and a five-minute request lifetime aligned with the MCP wait window. Provider and package selection remain owned by [docs/TECH_STACK.md](../TECH_STACK.md).

## Threat model

| Area | Contract |
|---|---|
| Assets | Account credentials, browser session authority, OAuth request integrity, authorization codes, access tokens, Workspace context, and antiforgery state. |
| Entry points | Registration verification, sign-in, session bootstrap, sign-out, authorization request/resume, loopback callback, and token exchange. |
| Trust boundaries | Browser to API, API to session store, browser to local loopback callback, and local MCP process to API. |
| Abuse cases | Account enumeration, fixation, stolen cookie, CSRF, open redirect, request-handle substitution or replay, callback `state` mismatch, leaked browser token, and unavailable session store. |
| Mitigations | Generic credential outcomes, ticket rotation, exact cookie flags, server-side storage, antiforgery, registered redirects, handle/client binding, PKCE, state validation, bounded expiry, no browser token path, and fail-closed dependency behavior. |
| Evidence | Owning use-case AT rows cover successful lifecycle, invalid account/session state, replay, redirect binding, callback failure, dependency failure, token absence, and recovery. |
