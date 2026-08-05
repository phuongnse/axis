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
| AT-007 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` (expired request token does not produce callback code; client callback failure remains not run) | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~SignInUserFlowTests` |
| AT-008 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/OpenIddictMcpClientTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpProtocolTests.cs` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx`; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj --filter FullyQualifiedName~OpenIddictMcpClientTests`; `python scripts/axis.py dotnet test tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj` |

## Current notes

- `request_uri` is the OpenIddict-owned opaque request-token reference; the continuation URL carries no raw OAuth request fields.
- API integration proves request-token creation, opaque SPA handoff, five-minute configuration behavior, manager-owned expiry mutation for the failure scenario, registered callback completion, and replay rejection against the new initial PostgreSQL schema.
- AT-007 request-token-expiry evidence passes; supported client callback-failure/timeout evidence remains not run.
- MCP protocol tests, API coverage, and tool-safety gates pass on the current .NET 10 target.
- MCP protocol, coverage, and safety gates are protocol-boundary evidence only. They do not substitute for a supported client reload, current tool registry, and authenticated `tools/call` read-back.
- Browser automation and the supported app-managed client flow have not run after the `request_uri` cutover.

## Current verification

- API `SignInUserFlowTests`: 11 passed, including signed-out handoff/sign-in resume, silent `login_required`, missing/tampered/expired/replayed request URI fail-closed behavior, and the registered `axis_mcp` loopback callback.
- Frontend auth tests: 39 passed across pending sign-in continuation and session restore; continuation generation uses only `request_uri`.
- MCP contract tests: 18 passed on `net10.0`.
- Identity Infrastructure: 55 passed, including public-client catalog deletion through `IOpenIddictApplicationManager` with fresh-scope read-back.
- Business Objects Infrastructure: 10 passed; Rules Infrastructure: 12 passed. Both applied their new initial migrations from an empty PostgreSQL database.
- Full API project: 52 passed on the new initial migrations.
- Browser automation, supported client reload/read-back, and callback-failure/timeout evidence remain not run, so the use case remains Partial.
