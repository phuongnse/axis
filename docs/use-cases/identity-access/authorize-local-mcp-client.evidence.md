# Authorize A Local MCP Client Evidence

> **Navigation**: [docs/use-cases/identity-access/authorize-local-mcp-client.md](./authorize-local-mcp-client.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-002 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (SPA-carried public client ID and opaque request URI complete the registered callback) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-004 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx tests/auth-session-restore.test.ts` |
| AT-005 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-006 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (missing or mismatched client identifiers and missing, tampered, expired, or replayed handles) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-007 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (expired request token does not produce callback code; client callback failure remains not run) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-008 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/OpenIddictMcpClientTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpProtocolTests.cs` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx`; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~OpenIddictMcpClientTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj` |

## Current notes

- `request_uri` is the OpenIddict-owned opaque request-token reference. The continuation carries only that handle and its validated public `client_id`; redirect URI, state, PKCE challenge, scopes, and other raw OAuth request fields remain inside the cached request.
- API integration proves request-token creation, bounded SPA handoff, exact handle/client binding, missing and mismatched client rejection, five-minute configuration behavior, manager-owned expiry mutation for the failure scenario, registered callback completion, and replay rejection against the new initial PostgreSQL schema.
- AT-007 request-token-expiry evidence passes; supported client callback-failure/timeout evidence remains not run.
- MCP protocol tests, API coverage, and tool-safety gates pass on the current .NET 10 target.
- MCP protocol, coverage, and safety gates are protocol-boundary evidence only. They do not substitute for a supported client reload, current tool registry, and authenticated `tools/call` read-back.
- The supported Codex app client reloaded after the required `client_id` binding correction, completed browser authorization, exchanged the code, and returned an authenticated current-user tool result. Durable browser automation remains not run.

## Current verification

- API `SignInUserFlowTests`: 11 passed, including signed-out handoff/sign-in resume with `client_id` and `request_uri`, silent `login_required`, missing/mismatched-client and missing/tampered/expired/replayed request URI fail-closed behavior, and the registered `axis_mcp` loopback callback.
- Focused frontend auth tests: 21 passed across pending sign-in continuation and session restore; continuation generation uses exactly `client_id` and `request_uri`, and incomplete pairs remain recoverable without resuming OAuth.
- Frontend type-check and Biome CI passed after the binding correction.
- Supported app-managed client authorization passed after reload: the current write registry exposed 27 tools and `axis_get_current_user` completed through the corrected `client_id` plus `request_uri` resume boundary.
- MCP contract tests: 18 passed on `net10.0`.
- Identity Infrastructure: 55 passed, including public-client catalog deletion through `IOpenIddictApplicationManager` with fresh-scope read-back.
- Business Objects Infrastructure: 10 passed; Rules Infrastructure: 12 passed. Both applied their new initial migrations from an empty PostgreSQL database.
- Full API project: 52 passed on the new initial migrations.
- Browser automation and callback-failure/timeout evidence remain not run, so the use case remains Partial.
