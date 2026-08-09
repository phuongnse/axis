# Module Navigation Evidence

> **Navigation**: [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) · [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend test tests/app-shell.test.tsx` |
| AT-002, AT-003 | `tests/Api/Axis.Api.Tests/Navigation/ModuleNavigationEndpointTests.cs`, `frontend/tests/app-shell.test.tsx`, `frontend/tests/module-navigation.test.tsx` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj -- --no-restore --filter FullyQualifiedName~ModuleNavigationEndpointTests`; `python scripts/axis.py frontend test tests/app-shell.test.tsx tests/module-navigation.test.tsx` |
| AT-004 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
| AT-005 | `src/Axis.Api/Endpoints/ModuleNavigationEndpoints.cs`, `frontend/src/lib/module-navigation.ts`, `frontend/src/lib/module-navigation-api.ts`, `frontend/src/lib/module-navigation-registry.ts`, `frontend/src/components/shared/AppShell.tsx`, `frontend/src/components/shared/ModuleNavigation.tsx`, `frontend/src/features/memberships/navigation.ts`, `frontend/src/features/service-identities/navigation.ts`, `frontend/src/features/product-roles/navigation.ts`, `frontend/src/features/business-objects/navigation.ts`, `frontend/src/features/rules/navigation.ts`, `frontend/src/features/solutions/navigation.ts` | `python scripts/axis.py generate api-contracts`; `python scripts/axis.py frontend ci` |
