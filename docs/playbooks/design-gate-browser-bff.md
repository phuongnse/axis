# Design Gate: Enterprise Browser BFF

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/design-gate-enterprise-production-baseline.md](./design-gate-enterprise-production-baseline.md) · [docs/use-cases/solutions/provision-reference-solution.md](../use-cases/solutions/provision-reference-solution.md) · [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

## Status

**Ready — approved 2026-08-04.** The complete browser-auth and tech-stack decision below is authorized for implementation; its clean-cutover and evidence boundaries remain mandatory.

## Trigger and owners

- Trigger: both the reference product and Axis SPA use browser-public PKCE clients; the product persists an Axis bearer token in browser session storage and Axis keeps one in memory. Neither is the supported enterprise browser trust boundary.
- Product/use-case owner: `docs/use-cases/solutions/provision-reference-solution.md`.
- Axis Identity owner: OAuth/OIDC server endpoints, client registration profiles, token grants, and current-browser authorization session.
- Axis Web owner: its same-origin server session, CSRF boundary, current-browser sign-in/out, deployment, and runtime evidence.
- Product owner: its BFF runtime, session, CSRF boundary, exact forwarded operations, product logout, deployment secrets, and runtime evidence.
- Standards basis: [RFC 10017](https://www.rfc-editor.org/rfc/rfc10017), [RFC 9700](https://www.rfc-editor.org/rfc/rfc9700), [Microsoft .NET 10 OIDC/BFF guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0), and [OpenIddict PAR guidance](https://documentation.openiddict.com/configuration/pushed-authorization-requests).

## Approved decision

1. No enterprise browser surface is an OAuth public client. The Axis SPA uses Axis's same-origin server session directly; each independently deployed product owns a same-origin BFF. BFF is not an Axis domain module and `IdPConnection` remains the separate Axis-to-enterprise-IdP federation boundary.
2. The reference product adds an ASP.NET Core .NET 10 host that serves its built React/Vite assets and owns `/bff/*`, OIDC callbacks, and a finite `/api/*` forward surface. Browser code never receives an Axis access token, refresh token, authorization code after callback processing, or client secret.
3. The BFF is an OpenIddict confidential web client using Authorization Code + PKCE, mandatory PAR, exact HTTPS sign-in/sign-out callback URIs, and `openid profile email offline_access`. The client secret is a deployment secret, never source, image, log, browser configuration, or product package content.
4. Axis cleanly replaces `PublicClientCatalog` with a deployment-owned typed client catalog. Profiles are `NativePublic` and `WebBffConfidential`; both `axis_spa` and the product browser-public registration are removed, not retained as fallbacks. Confidential profile permissions are fixed by code rather than deployment-authored grant lists.
5. Axis adds standards endpoints for PAR, refresh-token exchange, revocation, and RP-initiated end-session. Refresh tokens are encrypted, stored and rotation-validated by OpenIddict; access tokens remain short-lived. Product logout deletes the BFF session, attempts refresh-token revocation, and ends the current Axis browser authorization session through the registered end-session flow.
6. The BFF uses ASP.NET Core cookie/OIDC middleware, YARP direct forwarding, and Apache-2.0 `Duende.AccessTokenManagement.OpenIdConnect`. Exact supported versions are pinned in the product manifest and recorded in `TECH_STACK.md` during implementation.
7. Product access/refresh tokens and both browser authentication tickets live only in owner-namespaced Redis-backed `ITicketStore` instances; each `__Host-` cookie contains only an opaque session identifier and is Secure, HttpOnly, SameSite=Lax, host-only, and path `/`. Data Protection keys use owner-specific shared persistent protected key rings. Redis loss fails closed by invalidating sessions; it never falls back to browser or in-memory tokens.
8. Session idle and absolute lifetimes are validated deployment policy bounded by Axis refresh-token lifetime. Refresh is automatic before expiry and serialized across BFF instances by an expiring Redis lock keyed by opaque session ID; no sticky-session requirement is permitted.
9. `GET /bff/session` returns only minimal display identity plus a CSRF token; `GET /bff/login` validates a local return URL; `POST /bff/logout` and every unsafe forwarded method require antiforgery validation. OIDC state, nonce, correlation, issuer, audience, signature, and exact redirect validation remain framework-owned and fail closed.
10. YARP forwards only the documented reference-product route-template and HTTP-method allowlist to the configured Axis API. It strips browser authorization/cookie/hop-by-hop headers, attaches the managed user token server-side, preserves Axis status/problem bodies, applies request-size/time limits, and cannot select an arbitrary host or path.
11. A configured HTTPS backchannel address may route discovery, PAR, token, refresh, revocation, JWKS, and API traffic over service DNS while preserving the public issuer and front-channel authority. This is a deployment adapter with full TLS/issuer validation, not an alternate auth path.
12. The Axis SPA stops calling `/connect/token`: same-origin sign-in establishes its opaque Axis server session, session bootstrap returns minimal identity plus CSRF material, cookie-authenticated unsafe API calls require antiforgery, and bearer-authenticated public API clients remain CSRF-independent. Authentication-scheme selection is explicit and cannot accept a caller-supplied cookie as bearer authority or vice versa.

## Enterprise-production fitness

| Concern | Required invariant and evidence |
|---|---|
| Security/privacy | No credential reaches JavaScript, HTML, URL history, logs, telemetry, or client storage; CSRF, open-redirect, proxy-confusion, session-fixation, replay, and secret scans pass. |
| Auth/isolation | Axis remains authoritative for user/workspace claims and every forwarded operation; BFF never elevates or synthesizes them; unauthenticated and cross-workspace requests fail. |
| Data/migration | OpenIddict schema already owns client/token data; catalog cutover is reconciler-owned. Product Redis contains disposable sessions only and requires no business-data migration. |
| Failure/recovery/concurrency | Redis/Axis/token refresh failure fails closed with recoverable `401/503`; distributed refresh serialization, restart, instance loss, stale session, replay, and logout degradation are tested. |
| Deployment/config/secrets | Exact issuer, backchannel/API targets, callbacks, secret references, Redis TLS/auth, key protection, proxy headers, and rotation runbook are deployment-owned and startup-validated. |
| Observability/support | Structured redacted logs, traces, refresh/revocation failure metrics, authenticated dependency health, correlation IDs, and operator diagnostics contain no auth artifacts or PII by default. |
| Performance/capacity | Streaming proxy, bounded bodies/timeouts, pooled upstream connections, Redis latency budget, refresh stampede protection, and multi-instance load evidence are required. |
| Accessibility/localization | Sign-in expiry/recovery/logout states preserve intent, focus, keyboard access, and product-owned localized copy; redirects do not strand assistive-technology users. |
| Supply chain | Exact packages/locks, Apache-2.0 license evidence, vulnerability audit, supported .NET combination, pinned images, and Renovate ownership are required. |
| Compatibility/rollback | Clean cutover removes both browser PKCE/token paths, browser token storage, Vite auth proxies, browser-public client config, and their tests/docs. Rollback is a deployment revision rollback, never a dual auth path. |

## Verification and completion boundary

- Component/API tests prove catalog profiles, explicit cookie/bearer selection, secret redaction, PAR/PKCE, refresh/revocation/end-session, cookie flags, CSRF, return URLs, allowlist enforcement, exact status/body forwarding, and session failure behavior.
- Multi-instance integration proves shared tickets/key ring and one refresh under concurrent requests; restart/Redis loss/Axis outage evidence proves fail-closed recovery without sticky sessions.
- Browser acceptance proves the Axis SPA through its real same-origin server session, then starts the product blank, signs in through the real BFF, provisions and reads back through allowlisted public contracts, survives access-token refresh, signs out, and cannot reuse either ended browser session.
- Identifier sweep finds no browser-public client registration, browser PKCE/token persistence, direct product-browser-to-Axis API/auth calls, fallback, or compatibility guidance.
- The use-case/AT matrix and `TECH_STACK.md` must carry this decision before source implementation; completion still requires every verification item above.
