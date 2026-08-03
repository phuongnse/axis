# Authorize A Local MCP Client Evidence

> **Navigation**: [docs/use-cases/identity-access/authorize-local-mcp-client.md](./authorize-local-mcp-client.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-002 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-004 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx tests/auth-session-restore.test.ts` |
| AT-005 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-006 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-008 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/OpenIddictMcpClientTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpProtocolTests.cs` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx`; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~OpenIddictMcpClientTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj` |

## Current notes

- `request_id` is the OpenIddict 5.8 authorization-cache handle; the continuation URL carries no raw OAuth request fields.
- AT-003 is not run: the supported browser journey through the registered loopback callback and the app-managed MCP client boundary require runtime/client lifecycle evidence not available in this checkpoint.
- AT-007 is not run: cache/callback failure injection is not covered by the current fixture; no token-issuance claim is made for that path.
- MCP protocol, coverage, and safety gates are protocol-boundary evidence only. They do not substitute for a supported client reload, current tool registry, and authenticated `tools/call` read-back.
- Local browser execution is blocked by the existing `axis_api` container health failure: startup migration exits with PostgreSQL `42P07` because relation `OpenIddictApplications` already exists. The database and app-managed process were not modified.

## Verification summary

- API `SignInUserFlowTests`: 10 passed, including interactive handoff, silent `login_required`, invalid handle fail-closed behavior, and the registered `axis_mcp` loopback callback.
- Frontend auth tests: 39 passed across the pending sign-in continuation and session-restore suites.
- MCP contract tests: 17 passed; API coverage and tool-safety checks passed.
- Supported browser/client runtime: not run; the use case remains Partial.
