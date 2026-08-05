# Design Gate: MCP Browser Authorization Handoff

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [authorize-local-mcp-client.md](../use-cases/identity-access/authorize-local-mcp-client.md) · [mcp.md](./mcp.md) · [docs/README.md](../README.md)

## Risk and scope
This is a full Design Gate for the local MCP browser-authorization handoff. The slice is high-risk because it changes the authorization endpoint's unauthenticated behavior, crosses the API/OpenIddict/SPA/MCP runtime boundary, and carries an OAuth authorization request across a sign-in journey.

The slice fixes the interactive MCP bootstrap path only. It does not change the MCP client registration, loopback port, OAuth scopes, `prompt=login` behavior, token lifetime, or account/browser credential tools.

## Governing rules
- Product behavior follows [authorize-local-mcp-client.md](../use-cases/identity-access/authorize-local-mcp-client.md) and the existing [sign-in-user.md](../use-cases/identity-access/sign-in-user.md) contract.
- Repository lifecycle gates follow [AGENTS.md](../../AGENTS.md#critical-rules), [reference.md](../../.agents/skills/reference.md#universal-gates), and [agent-checklist.md](./agent-checklist.md#review-verification).
- OAuth server behavior and browser session ownership remain in [ConnectEndpoints.cs](../../src/Axis.Api/Endpoints/ConnectEndpoints.cs) and the OpenIddict server configuration; the SPA never validates or reconstructs the OAuth request.
- MCP authentication and supported-client runtime evidence follow [axis-mcp-integration](../../.agents/skills/axis-mcp-integration/SKILL.md#workflow) and [mcp.md](./mcp.md#runtime-lifecycle-and-blocker-protocol).
- Client route, URL state, recovery, and interaction behavior follow [axis-frontend-feature](../../.agents/skills/axis-frontend-feature/SKILL.md#workflow) and [frontend.md](./frontend.md#component-design).
- Durable documentation ownership follows [axis-doc-hygiene](../../.agents/skills/axis-doc-hygiene/SKILL.md#workflow).

## Blast radius
The implementation and review sweep covers:

```text
src/Axis.Api/Endpoints/ConnectEndpoints.cs
src/Axis.Api/Extensions/AxisApiServiceExtensions.cs
src/Axis.Api/appsettings.json
src/Axis.Api/Axis.Api.csproj
src/Axis.Mcp/Authentication/OAuthTokenProvider.cs
Directory.Packages.props
docker-compose.yml
scripts/check-local-dev-docs.py
scripts/tests/test_policy_gates.py
tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs
tests/Api/Axis.Api.Tests/Helpers/ApiTestFixture.cs
tests/Tools/Axis.Mcp.Tests/OAuthTokenProviderTests.cs
tests/Tools/Axis.Mcp.Tests/LoopbackAuthorizationListenerTests.cs
frontend/src/features/auth/api.ts
frontend/src/features/auth/hooks/useSignIn.ts
frontend/src/features/auth/route-guards.ts
frontend/src/routes/_guest/sign-in.lazy.tsx
frontend/vite.config.ts
frontend/tests/auth-session-restore.test.ts
frontend/tests/sign-in-page.test.tsx
frontend/e2e/authorize-local-mcp-client.pw.ts
frontend/e2e/sign-in-user.pw.ts
docs/use-cases/identity-access/authorize-local-mcp-client.md
docs/use-cases/identity-access/authorize-local-mcp-client.evidence.md
docs/use-cases/identity-access/README.md
docs/playbooks/mcp.md
docs/playbooks/local-dev.md
docs/TECH_STACK.md
```

## Contract and invariant decisions
- OpenIddict's built-in authorization-request caching stores the validated request as a request token in its token store and exposes its opaque OAuth `request_uri` together with the regular public `client_id` required by the protocol.
- Request tokens use an absolute five-minute lifetime, matching the MCP authorization wait window. Missing, expired, tampered, or already-redeemed request URIs fail closed through the authorization server.
- An unauthenticated interactive authorization request redirects only to the configured `App:BaseUrl` `/sign-in` route with the opaque handle and server-validated public client identifier. Redirect URI, state, code challenge, scopes, and arbitrary return URLs are not copied into SPA state.
- `prompt=none` remains a non-interactive flow and returns `login_required` to the registered callback when the browser session is absent.
- After successful sign-in, the SPA navigates only to the fixed API `/connect/authorize?client_id=...&request_uri=...` endpoint. It does not infer or default either value, begin a second SPA PKCE transaction, or accept an arbitrary redirect target. OpenIddict verifies that the regular client identifier matches the cached request before restoring it.
- OpenIddict uses the required canonical `OpenIddict:Issuer`; local development sets it to the host-reachable API origin `https://localhost:5281`. Request-cache validation therefore does not depend on whether transport reached the API directly or through the SPA proxy.
- SPA authorization and token requests stay on `window.location.origin` and use the `/connect` proxy, which preserves the browser-facing Host when forwarding so OpenIddict redirects remain on that origin. Host and Compose browsers use the same client behavior; no browser-context override, forwarded-host alias, or E2E-only path is supported.
- Compose browser evidence runs Playwright and its real loopback callback listener in the `web` network namespace. The test browser therefore reaches the configured `https://localhost:3000` origin and `127.0.0.1:48123` callback without rewriting redirects, weakening TLS, or changing host local-development behavior.
- The MCP client keeps its existing PKCE verifier, state validation, loopback listener, token exchange, and five-minute production timeout. One authorization-scoped cancellation boundary stops the listener and releases the fixed port on timeout or caller cancellation; malformed callbacks never reach token exchange. No credential or auth tool is added.

## Enterprise production fitness
| Concern | Decision and evidence owner |
|---|---|
| Security, privacy, authorization, isolation | OpenIddict continues to validate the public client identifier, cached request, registered redirect, PKCE, state, and workspace-scoped token. The SPA carries no secret or reconstructable raw request, and malformed callbacks fail before token exchange. |
| Data lifecycle, migration | OpenIddict's migration-backed request-token store and five-minute expiry remain unchanged; no application schema changes. |
| Failure, recovery, concurrency | The single local authorization attempt has one deadline and linked caller cancellation. Every terminal path stops the listener and releases port 48123 so a fresh attempt can bind without a stale process path. |
| Deployment, configuration, secrets | Production and host-local origins, certificates, ports, and client catalog remain unchanged. Compose E2E only shares the existing web network namespace so the configured localhost origin and callback are exercised faithfully. No secret enters source or browser state. |
| Observability, support | Existing OpenIddict and API logs retain redaction; deterministic MCP errors distinguish timeout, caller cancellation, malformed callback, bind failure, and token-exchange failure without logging codes or tokens. |
| Performance, capacity | N/A because this is one interactive local authorization listener with an existing bounded lifetime, not a shared throughput surface. |
| Accessibility, localization | The existing sign-in screen and its current accessibility/localization contract are reused unchanged; the browser journey verifies the silent technical handoff rather than introducing UI. |
| Supply chain | No package, runtime, framework, or tool version changes. Standard-library networking, cancellation, crypto, and the installed Playwright stack are reused. |
| Compatibility, rollback | Clean replacement is safe because no supported flow depends on the invalid request-URI-only resume, stale listener behavior, or the unreachable Compose browser origin. Reverting the checkpoint restores the prior behavior without data migration. |

## Retirement and compatibility
Clean cutover for interactive unauthenticated authorization: replace the current direct `401` response with the configured sign-in handoff. Retire the corresponding API test assertion and do not keep a fallback that forwards the raw authorization query.

Retire `VITE_CONNECT_URL` in the same cutover. The authorization server issuer is an API protocol contract, not a browser transport selection; no compatibility alias or fallback remains.

Preserve `prompt=none` callback behavior and the normal SPA sign-in/dashboard handoff. Replace the invalid request-URI-only continuation, listener-without-deadline cleanup, and Compose-only `https://web:3000` browser path cleanly; no shim, inferred client identifier, validation bypass, alternate E2E proxy, public REST DTO, OpenAPI operation, generated API type, or MCP product-tool shape is introduced.

Post-edit sweep:
```text
rg -n "Results\.Unauthorized\(\)|returnUrl|returnTo|window\.location.*authorization|VITE_CONNECT_URL" src/Axis.Api frontend/src tests/Api frontend/tests docker-compose.yml docs scripts/check-local-dev-docs.py
```

The sweep must show only the intentional fail-closed API branch, fixed authorization continuation, and current tests/guidance.

## Verification plan
- API integration tests for interactive redirect target, bounded handle/client query, prompt-none behavior, exact handle/client binding, missing or mismatched client rejection, direct-to-proxy issuer-stable resume, invalid/expired/replayed handles, and unchanged sign-in/sign-out behavior.
- Frontend component/session tests for pending handle/client retention through credential errors, fixed continuation after sign-in, no inferred/default client, no second SPA PKCE transaction, invalid continuation recovery, and normal dashboard sign-in.
- Browser journey for MCP authorization through the real sign-in form to the loopback callback, plus the existing sign-in journey with same-origin `/connect` proof.
- MCP authentication tests for missing code, mismatched state, deadline cleanup, caller cancellation, no failed-path token exchange, and immediate fixed-port reuse without changing the production five-minute timeout.
- PostgreSQL-backed API fixture evidence for request-token creation, expiry, redemption, and replay rejection through OpenIddict.
- MCP coverage, contract, and safety checks; supported client reload/reconnect and authenticated `tools/call` read-back remain required runtime evidence.
- Before publication: `$axis-review-readiness`, completed configured independent implementation review, and a fresh supported-client runtime result. A running or timed-out reviewer is not a verdict.

## Acceptance and status boundary
The use case remains incomplete until the API, SPA, browser, and supported live MCP boundaries all have current evidence. Unit, API, or protocol-harness evidence cannot substitute for the app-managed client authorization and authenticated call/read-back boundary.
