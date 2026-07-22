# Evaluate Published Rules Evidence

> **Navigation**: [docs/use-cases/rules/evaluate-published-rules.md](./evaluate-published-rules.md) · [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `src/Modules/Rules/Axis.Rules.Domain/RuleExpressionLanguage.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleConditionEvaluatorTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/SystemRuleCatalogTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleValueTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Domain.Tests/Axis.Rules.Domain.Tests.csproj` |
| AT-003, AT-004 | `tests/Modules/Rules/Axis.Rules.Application.Tests/Queries/GetRuleDefinitionHandlerTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj` |
| AT-005, AT-006, AT-007 | `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs`, `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs`, `openapi.json`, `frontend/src/lib/api-types.ts` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`, `python scripts/axis.py check frontend-api-contracts` |
| AT-008 | `tests/Architecture/Axis.Architecture.Tests/DomainPurityTests.cs`, `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj` |
| AT-009 | `frontend/src/features/rules/components/RuleEditorDialog.tsx`, `frontend/src/features/rules/components/RuleExpressionView.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- rules-page.test.tsx` |
| AT-010 | `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts` |
