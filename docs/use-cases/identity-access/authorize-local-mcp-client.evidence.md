# Authorize A Local MCP Client Evidence

> **Navigation**: [docs/use-cases/identity-access/authorize-local-mcp-client.md](./authorize-local-mcp-client.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-002 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-004 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx tests/auth-session-restore.test.ts` |
| AT-005 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-006 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (missing, tampered, expired, and replayed handles) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-007 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (expired distributed-cache request does not produce callback code; client callback failure remains not run) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-008 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/OpenIddictMcpClientTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpProtocolTests.cs` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx`; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~OpenIddictMcpClientTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj` |

## Current notes

- `request_id` is the OpenIddict 5.8 authorization-cache handle; the continuation URL carries no raw OAuth request fields.
- The supported client runtime journey covered by AT-003 passed manually: Codex loaded the current 13-tool registry, the browser completed the registered loopback flow, `axis_get_current_user` returned the authenticated user/workspace context, and `axis_list_rules` plus `axis_get_rule` agreed on the selected rule key and published version. AT-003 acceptance evidence remains incomplete because the required committed browser automation is not run.
- AT-007 cache-failure evidence is limited to an expired distributed-cache request returning `400` without a callback code; supported client callback-failure/timeout evidence is not run.
- MCP gates were run through the Axis routes: `python scripts/axis.py check mcp-api-coverage`, `python scripts/axis.py check mcp-contracts`, and `python scripts/axis.py check mcp-tool-safety` all passed.
- MCP protocol, coverage, and safety gates are protocol-boundary evidence only. They do not substitute for a supported client reload, current tool registry, and authenticated `tools/call` read-back.
- Local authorization uses the canonical OpenIddict issuer `https://localhost:5281`; browser transport stays same-origin through `/connect`, and the supported client flow completed without weakening request-handle, state, PKCE, callback, or token validation.

## Verification summary

- API `SignInUserFlowTests`: 11 passed, including the signed-out handoff/sign-in resume, silent `login_required`, missing/tampered/expired/replayed handle fail-closed behavior, and the registered `axis_mcp` loopback callback.
- Frontend auth tests: 41 passed across the pending sign-in continuation, session-restore, and callback suites.
- Compose browser regression: the focused `sign-in-user` AT-001 journey passed and verified `/connect/authorize` stayed on the browser-facing web origin before the dashboard handoff.
- MCP contract tests: 17 passed; API coverage and tool-safety checks passed.
- Supported browser/client runtime: passed against the app-managed Codex client; MCP loopback browser automation and callback-failure/timeout evidence remain not run, so the use case remains Partial.
