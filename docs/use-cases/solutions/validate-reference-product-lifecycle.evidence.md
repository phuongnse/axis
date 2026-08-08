# Validate The Reference Product Solution Lifecycle Evidence

> **Navigation**: [docs/use-cases/solutions/validate-reference-product-lifecycle.md](./validate-reference-product-lifecycle.md) · [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Modules/Solutions/Axis.Solutions.Application.Tests/SolutionPackageVerifierTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Solutions/Axis.Solutions.Application.Tests/Axis.Solutions.Application.Tests.csproj -- --no-restore` |
| AT-002 | `tests/Api/Axis.Api.Tests/Solutions/SignedSolutionInstallationTests.cs`, `tests/Modules/Solutions/Axis.Solutions.Application.Tests/SolutionOrchestratorTests.cs`, `tests/Modules/Solutions/Axis.Solutions.Infrastructure.Tests/SolutionsPersistenceTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj -- --no-restore`; `python scripts/axis.py dotnet test tests/Modules/Solutions/Axis.Solutions.Application.Tests/Axis.Solutions.Application.Tests.csproj -- --no-restore`; `python scripts/axis.py dotnet test tests/Modules/Solutions/Axis.Solutions.Infrastructure.Tests/Axis.Solutions.Infrastructure.Tests.csproj -- --no-restore` |
| AT-003 | `frontend/e2e/product-delivery-governance.pw.ts` | `python scripts/axis.py local-dev e2e -- frontend/e2e/product-delivery-governance.pw.ts` |
| AT-004 | `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs`, `tests/Architecture/Axis.Architecture.Tests/GatewayBoundaryTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj -- --no-restore`; `python scripts/axis.py verify` |

## Immutable Checkpoint

- External source commit: `595d4cf7bbf371deab11162382c3720a16ede3f5`.
- Draft signed solution identity: `reference_application` version `0.1.1`; build ID `reference-product-0.1.1`; publisher/key `axis_reference_product/release`.
- Signed payload `sourceRevision`: `595d4cf7bbf371deab11162382c3720a16ede3f5`.
- Axis OpenAPI SHA-256 in both the signed payload and external source: `09a2eb4f03605b6e7751f10fd13cafdffefad480dca9be2607d9ddfb8da0de5c`.
- The private release key remains ignored, preserved, and mode `0600`; it is not repository evidence and was not rotated.

## Current Verification

- Axis `python scripts/axis.py verify` passes the 447-path scope, including API 115, Architecture 321, frontend 228, MCP 36, all module suites, scripts/policy 452, vulnerabilities, format, generated contracts, docs, and repository skills.
- Axis browser governance passes 3/3 for service-key recovery, product-role recovery, and signed Solution publish/install resume behavior.
- External `npm run check` and `npm run test:unit` pass with zero dependency-audit findings; the generated client and source OpenAPI carry the same current Axis digest.
- External `npm run test:e2e` passes 1/1 after recreating the current Axis/reference-product overlay: the administrator publishes and installs the signed release before the Applicant creates, saves, submits, and canonically reads back the product record through the BFF.
