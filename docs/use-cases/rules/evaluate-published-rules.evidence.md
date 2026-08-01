# Evaluate Published Rules Evidence

> **Navigation**: [docs/use-cases/rules/evaluate-published-rules.md](./evaluate-published-rules.md) · [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `src/Modules/Rules/Axis.Rules.Domain/RuleEvaluation.cs`, `src/Modules/Rules/Axis.Rules.Domain/RuleCondition.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleConditionEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleValueTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Domain.Tests/Axis.Rules.Domain.Tests.csproj` |
| AT-002 | `src/Modules/Rules/Axis.Rules.Domain/RuleDefinitionValidator.cs`, `src/Modules/Rules/Axis.Rules.Domain/RuleOutputContract.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleConditionEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleDefinitionTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Domain.Tests/Axis.Rules.Domain.Tests.csproj` |
| AT-003 | `src/Modules/Rules/Axis.Rules.Application/RuleEvaluator.cs`, `src/Modules/Rules/Axis.Rules.Application/RuleApplicationValidator.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleApplicationValidatorTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj` |
| AT-004 | `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs`, `src/Axis.Api/Endpoints/RuleDefinitionEndpoints.cs`, `src/Modules/Rules/Axis.Rules.Contracts/RuleDefinitionDtos.cs`, `openapi.json`, `frontend/src/lib/api-generated/types.gen.ts` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`; `python scripts/axis.py check frontend-api-contracts` |
| AT-005 | `frontend/src/features/rules/components/RuleEditorDialog.tsx`, `frontend/src/features/rules/components/RuleBehaviorSummary.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend test tests/rules-page.test.tsx` |
| AT-006 | `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts -g "workspace rule authoring"` |
| AT-007 | `src/Modules/Rules/Axis.Rules.Application/RuleBindingEvaluator.cs`, `src/Modules/Rules/Axis.Rules.Contracts/RuleEvaluationContracts.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleBindingEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj` |
