# Provision A Reference Solution Evidence

> **Navigation**: [docs/use-cases/solutions/provision-reference-solution.md](./provision-reference-solution.md) · [docs/use-cases/solutions/README.md](./README.md) · [docs/use-cases/README.md](../README.md)

The independently versioned consumer evidence uses `external://<repository>@<40-character-commit>/<path-or-command>`. Every external path and command in one acceptance row binds the same immutable repository checkpoint; it is not a local Axis path or command. Only currently resolvable, compatible checkpoints count as evidence.

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/manifest.json`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/openapi.json`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/src/manifest.test.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:unit` |
| AT-002 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/src/planner.test.ts`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/tests/product.pw.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:unit`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:e2e` |
| AT-003 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/tests/product.pw.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:e2e` |
| AT-004 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/src/planner.test.ts`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/tests/product.pw.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:unit`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:e2e` |
| AT-005 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/server/Axis.ReferenceProduct.Bff.Tests/SecurityBoundaryTests.cs`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/tests/product.pw.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run check`, `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:e2e` |
| AT-006 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/src/App.test.tsx` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:unit` |
| AT-007 | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/tests/product.pw.ts` | `external://axis-reference-product@2b32616923ff9bb33b50013700b92ace1c5be15e/npm run test:e2e` |
| AT-008 | `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj` |
| AT-009 | `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-010 | `frontend/tests/app-shell.test.tsx`, `frontend/tests/business-objects-page.test.tsx` | `python scripts/axis.py frontend test tests/app-shell.test.tsx tests/business-objects-page.test.tsx` |
