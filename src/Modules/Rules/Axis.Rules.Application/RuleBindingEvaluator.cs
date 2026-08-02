using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainMappingKind = Axis.Rules.Domain.RuleInputMappingKind;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

public sealed class RuleBindingEvaluator(
    IRuleBindingRepository bindingRepository,
    IRuleEvaluator evaluator) : IRuleBindingEvaluator
{
    public async Task<RuleEvaluationResult> EvaluateBindingAsync(
        RuleBindingEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        string correlationId = NormalizeCorrelationId(request.CorrelationId);
        if (request.WorkspaceId == Guid.Empty || request.BindingId == Guid.Empty)
            return Failed(correlationId, "binding_invalid", "A workspace and binding are required.");

        RuleBinding? binding = await bindingRepository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(request.BindingId), request.WorkspaceId, cancellationToken);
        if (binding is null)
            return Failed(correlationId, "binding_not_found", "Rule binding was not found.");
        if (!binding.Enabled)
            return Failed(correlationId, "binding_disabled", "Rule binding is disabled.");

        RuleBindingRevision? bindingRevision = binding.FindRevision(request.BindingRevision);
        if (bindingRevision is null)
            return Failed(correlationId, "binding_revision_not_found", "Rule binding revision was not found.");
        if (!bindingRevision.Enabled)
            return Failed(correlationId, "binding_revision_disabled", "Rule binding revision is disabled.");

        Dictionary<string, IReadOnlyList<string>> inputs = new(StringComparer.Ordinal);
        Dictionary<string, ContractValueType> inputTypes = new(StringComparer.Ordinal);
        foreach ((string ruleInputKey, RuleInputMapping mapping) in bindingRevision.InputMappings)
        {
            if (mapping.Kind == DomainMappingKind.Literal)
            {
                inputs[ruleInputKey] = mapping.LiteralValues;
                continue;
            }

            if (request.Context?.Values is null || mapping.ContextKey is null ||
                !request.Context.Values.TryGetValue(mapping.ContextKey, out RuleContextValue? contextValue))
                continue;
            inputs[ruleInputKey] = contextValue.Values;
            inputTypes[ruleInputKey] = contextValue.Type;
        }

        RuleEvaluationResult result = await evaluator.EvaluateAsync(
            new RuleEvaluationRequest(
                request.WorkspaceId,
                [new RuleEvaluationReference(
                    bindingRevision.DefinitionKey.Value,
                    bindingRevision.DefinitionVersion,
                    inputs,
                    inputTypes)],
                correlationId),
            cancellationToken);
        return result;
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
