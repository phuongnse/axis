# Provide Built-in Rule Definitions Evidence

> **Navigation**: [docs/use-cases/rules/provide-built-in-rule-definitions.md](./provide-built-in-rule-definitions.md) · [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `src/Modules/Rules/Axis.Rules.Domain/BuiltInRuleCatalog.cs`, `src/Modules/Rules/Axis.Rules.Domain/RuleDefinition.cs`, `src/Modules/Rules/Axis.Rules.Domain/RuleOutputContract.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/BuiltInRuleCatalogTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleDefinitionTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Domain.Tests/Axis.Rules.Domain.Tests.csproj` |
| AT-002 | `src/Modules/Rules/Axis.Rules.Application/RuleEvaluator.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/Queries/GetRuleDefinitionHandlerTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj` |
| AT-003 | `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs`, `src/Axis.Api/Endpoints/RuleDefinitionEndpoints.cs`, `openapi.json`, `frontend/src/lib/api-generated/types.gen.ts` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`; `python scripts/axis.py check frontend-api-contracts` |
| AT-004 | `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs`, `tests/Architecture/Axis.Architecture.Tests/ModuleContractTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj` |
| AT-005 | `frontend/src/features/rules/components/RuleBehaviorSummary.tsx`, `frontend/src/features/rules/components/RuleExpressionView.tsx`, `frontend/src/features/rules/components/RuleConditionComposer.tsx`, `frontend/src/features/rules/components/RulesPage.tsx`, `frontend/tests/rule-expression-view.test.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend test tests/rule-expression-view.test.tsx tests/rules-page.test.tsx` |
