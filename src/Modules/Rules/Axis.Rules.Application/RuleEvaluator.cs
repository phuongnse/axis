using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

public sealed class RuleEvaluator(IRuleDefinitionRepository repository) : IRuleEvaluator
{
    public async Task<RuleEvaluationResult> EvaluateAsync(
        RuleEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string correlationId = NormalizeCorrelationId(request.CorrelationId);
        if (request.WorkspaceId == Guid.Empty)
            return Failed(correlationId, "workspace_required", "Workspace scope is required.");

        List<RuleEvaluationItemDto> items = [];
        foreach (RuleEvaluationReference reference in request.Rules ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result<RuleDefinitionVersion> version = await ResolveVersionAsync(
                request.WorkspaceId,
                reference,
                cancellationToken);
            if (version.IsFailure)
                return Failed(correlationId, version.ErrorCode ?? "definition_invalid", version.Error);

            Result<IReadOnlyDictionary<string, RuleValue>> inputs = RuleInputValidator.ValidateRaw(
                version.Value.Inputs,
                reference.Inputs,
                reference.InputTypes?.ToDictionary(
                    pair => pair.Key,
                    pair => (DomainValueType)pair.Value,
                    StringComparer.Ordinal));
            if (inputs.IsFailure)
                return Failed(correlationId, "input_invalid", inputs.Error);

            Result valid = RuleDefinitionValidator.Validate(version.Value.Inputs, version.Value.Condition, version.Value.Output);
            if (valid.IsFailure)
                return Failed(correlationId, "definition_invalid", valid.Error);

            Result<RuleConditionEvaluation> evaluation = RuleConditionEvaluator.Evaluate(
                version.Value.Condition,
                inputs.Value);
            if (evaluation.IsFailure)
                return Failed(correlationId, "evaluation_failed", evaluation.Error);

            items.Add(new RuleEvaluationItemDto(
                reference.DefinitionKey,
                reference.DefinitionVersion,
                evaluation.Value.IsMatch,
                evaluation.Value.Diagnostics
                    .Select(diagnostic => new RuleNodeDiagnosticDto(diagnostic.NodeId, diagnostic.IsMatch))
                    .ToArray()));
        }

        return new RuleEvaluationResult(true, items, correlationId, null, null);
    }

    private async Task<Result<RuleDefinitionVersion>> ResolveVersionAsync(
        Guid workspaceId,
        RuleEvaluationReference reference,
        CancellationToken cancellationToken)
    {
        RuleDefinition? system = SystemRuleCatalog.Find(reference.DefinitionKey, reference.DefinitionVersion);
        if (system is not null)
            return system.FindVersion(reference.DefinitionVersion)!;

        if (SystemRuleCatalog.Definitions.Any(definition =>
                definition.Key.Value.Equals(reference.DefinitionKey, StringComparison.Ordinal)))
            return Result.Failure<RuleDefinitionVersion>("version_not_found", "Published rule version was not found.");

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(reference.DefinitionKey);
        if (key.IsFailure)
            return Result.Failure<RuleDefinitionVersion>("definition_not_found", "Rule definition was not found.");

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        if (definition is null || definition.Status == DomainLifecycleStatus.Archived)
            return Result.Failure<RuleDefinitionVersion>("definition_not_found", "Rule definition was not found.");

        RuleDefinitionVersion? version = definition.FindVersion(reference.DefinitionVersion);
        return version is null
            ? Result.Failure<RuleDefinitionVersion>("version_not_found", "Published rule version was not found.")
            : version;
    }

    private static RuleEvaluationResult Failed(string correlationId, string code, string error) =>
        new(false, [], correlationId, code, error);

    private static string NormalizeCorrelationId(string? correlationId)
    {
        string normalized = correlationId?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? Guid.NewGuid().ToString("N")
            : normalized[..Math.Min(normalized.Length, 120)];
    }
}
