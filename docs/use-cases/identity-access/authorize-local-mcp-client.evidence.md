# Authorize A Local MCP Client Evidence

> **Navigation**: [docs/use-cases/identity-access/authorize-local-mcp-client.md](./authorize-local-mcp-client.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-002 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (SPA-carried public client ID and opaque request URI complete the registered callback) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-003 | `frontend/e2e/authorize-local-mcp-client.pw.ts` (real browser sign-in, PKCE/state, loopback callback, code exchange, and protected current-user read-back) | `python scripts/axis.py local-dev e2e -- e2e/authorize-local-mcp-client.pw.ts` |
| AT-004 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx tests/auth-session-restore.test.ts` |
| AT-005 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-006 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (missing or mismatched client identifiers and missing, tampered, expired, or replayed handles) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-007 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs`, `tests/Tools/Axis.Mcp.Tests/OAuthTokenProviderTests.cs` (expired request token, malformed callback, deadline cleanup, port reuse, and no failed-path token exchange) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj --filter FullyQualifiedName~OAuthTokenProviderTests` |
| AT-008 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/OpenIddictMcpClientTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpProtocolTests.cs` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx`; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~OpenIddictMcpClientTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj` |

## Current notes

- `request_uri` is the OpenIddict-owned opaque request-token reference. The continuation carries only that handle and its validated public `client_id`; redirect URI, state, PKCE challenge, scopes, and other raw OAuth request fields remain inside the cached request.
- API integration proves request-token creation, bounded SPA handoff, exact handle/client binding, missing and mismatched client rejection, five-minute configuration behavior, manager-owned expiry mutation for the failure scenario, registered callback completion, and replay rejection against the new initial PostgreSQL schema.
- AT-007 request-token expiry and MCP callback/deadline cleanup tests pass. The supported Codex client also rejected a mismatched-state callback, terminated the no-callback operation at its five-minute `tools/call` deadline, released the loopback port, and recovered through a fresh authenticated read on the same MCP process.
- MCP protocol tests, API coverage, and tool-safety gates pass on the current .NET 10 target.
- MCP protocol, coverage, and safety gates are protocol-boundary evidence only. They do not substitute for a supported client reload, current tool registry, and authenticated `tools/call` read-back.
- The supported Codex app client reloaded after the required `client_id` binding correction, completed browser authorization, exchanged the code, and returned an authenticated current-user tool result.

## Current verification

- API `SignInUserFlowTests`: 11 passed, including signed-out handoff/sign-in resume with `client_id` and `request_uri`, silent `login_required`, missing/mismatched-client and missing/tampered/expired/replayed request URI fail-closed behavior, and the registered `axis_mcp` loopback callback.
- Focused frontend auth tests: 21 passed across pending sign-in continuation and session restore; continuation generation uses exactly `client_id` and `request_uri`, and incomplete pairs remain recoverable without resuming OAuth.
- Frontend type-check and Biome CI passed after the binding correction.
- Compose E2E topology and local-dev checks passed; Playwright AT-003 passed once in 6.5 seconds through the real localhost origin and loopback callback.
- Focused `OAuthTokenProviderTests`: 4 passed, proving malformed callbacks skip token exchange, caller cancellation remains distinct, the configured deadline stops the listener, and port 48123 can bind again immediately.
- Supported app-managed client authorization passed after reload: the current write registry exposed 27 tools and `axis_get_current_user` completed through the corrected `client_id` plus `request_uri` resume boundary.
- MCP contract tests: 18 passed on `net10.0`.
- Identity Infrastructure: 55 passed, including public-client catalog deletion through `IOpenIddictApplicationManager` with fresh-scope read-back.
- Business Objects Infrastructure: 10 passed; Rules Infrastructure: 12 passed. Both applied their new initial migrations from an empty PostgreSQL database.
- Full API project: 52 passed on the new initial migrations.
- Supported Codex client failure/recovery proof passed on the read-only 14-tool registry: a live mismatched-state callback returned HTTP 400 and failed the tool; a second call with no callback remained pending until the 300-second client deadline; port 48123 was then absent from the listener table and accepted an immediate diagnostic bind; after the API returned healthy, `axis_get_current_user` succeeded on the same MCP process in 1.9 seconds. Because the supported-client and internal authorization deadlines are both five minutes, Codex surfaced its outer `tools/call` timeout; focused MCP tests separately prove the internal deadline path, caller-cancellation distinction, listener cleanup, and absence of token exchange on malformed callbacks.
