# Manage Workspace Rule Definitions Evidence

> **Navigation**: [docs/use-cases/rules/manage-workspace-rule-definitions.md](./manage-workspace-rule-definitions.md) · [docs/use-cases/rules/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleDefinitionTests.cs`, `tests/Modules/Rules/Axis.Rules.Domain.Tests/RuleConditionEvaluatorTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Domain.Tests/Axis.Rules.Domain.Tests.csproj` |
| AT-003, AT-004 | `tests/Modules/Rules/Axis.Rules.Application.Tests/Commands/CreateRuleDefinitionHandlerTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/Commands/SaveRuleDefinitionDraftHandlerTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/Commands/PublishRuleDefinitionHandlerTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/Queries/SimulateRuleDefinitionHandlerTests.cs`, `tests/Modules/Rules/Axis.Rules.Application.Tests/RuleEvaluatorTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj` |
| AT-005 | `tests/Modules/Rules/Axis.Rules.Infrastructure.Tests/Repositories/RuleDefinitionRepositoryTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Infrastructure.Tests/Axis.Rules.Infrastructure.Tests.csproj` |
| AT-006 | `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs`, `openapi.json`, `frontend/src/lib/api-types.ts` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`, `python scripts/axis.py check frontend-api-contracts` |
| AT-007 | `tests/Modules/Rules/Axis.Rules.Application.Tests/Commands/SaveRuleDefinitionDraftHandlerTests.cs`, `tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-008 | `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs`, `tests/Architecture/Axis.Architecture.Tests/ModuleContractTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj` |
| AT-009 | `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- rules-page.test.tsx` |
| AT-010 | `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py frontend script test:e2e -- manage-rules.pw.ts` |
