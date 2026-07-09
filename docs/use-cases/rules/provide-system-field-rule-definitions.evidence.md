# Provide System Field Rule Definitions Evidence

> **Navigation**: [docs/use-cases/rules/provide-system-field-rule-definitions.md](./provide-system-field-rule-definitions.md) · [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Modules/Rules/Axis.Rules.Domain.Tests/SystemFieldRuleCatalogTests.cs` | `python scripts/axis.py dotnet test -- --filter FullyQualifiedName~Axis.Rules.Domain.Tests` |
| AT-002 | `tests/Modules/Rules/Axis.Rules.Application.Tests/SystemFieldRuleDefinitionProviderTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/FieldRuleApplicationValidatorTests.cs` | `python scripts/axis.py dotnet test -- --filter FullyQualifiedName~Axis.Rules.Application.Tests` |
| AT-003 | `tests/Api/Axis.Api.Tests/Rules/FieldRuleDefinitionEndpointTests.cs`, `tests/Api/Axis.Api.Tests/Contracts/OpenApiDocumentTests.cs`, `openapi.json`, `frontend/src/lib/api-types.ts` | `python scripts/axis.py dotnet test -- --filter "FullyQualifiedName~Axis.Api.Tests.Rules&FullyQualifiedName!~tests/Api/"`, `python scripts/axis.py dotnet test -- --filter "FullyQualifiedName~Axis.Api.Tests.Contracts.OpenApiDocumentTests&FullyQualifiedName!~tests/Api/"`, `python scripts/axis.py generate api-contracts`, `python scripts/axis.py check frontend-api-contracts` |
| AT-004 | `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs`, `tests/Architecture/Axis.Architecture.Tests/ModuleContractTests.cs`, `tests/Architecture/Axis.Architecture.Tests/ModuleAssemblyDiscoveryTests.cs` | `python scripts/axis.py dotnet test -- --filter "FullyQualifiedName~Axis.Architecture.Tests&FullyQualifiedName!~tests/Architecture/"` |
| AT-005 | `frontend/tests/rules-page.test.tsx`, `frontend/tests/module-navigation.test.tsx`, `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test tests/rules-page.test.tsx tests/module-navigation.test.tsx tests/app-shell.test.tsx` |
