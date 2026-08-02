using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

public sealed class RuleConditionProjectionService
{
    private static readonly RuleConditionDisplayCompiler DisplayCompiler = new();

    public Result<RuleConditionProjectionDto> Project(ProjectRuleConditionRequest request)
    {
        if (request.ExpressionLanguageVersion != RuleExpressionLanguage.Version)
        {
            return RuleDefinitionFailures.Invalid<RuleConditionProjectionDto>(
                "Rule expression language version is unavailable.");
        }

        Result<RuleDraftInput> draft = RuleDraftInputMapper.Map(request.Inputs, request.Condition);
        if (draft.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleConditionProjectionDto>(draft.Error);

        Result valid = RuleDefinitionValidator.Validate(
            draft.Value.Inputs,
            draft.Value.Condition,
            RuleOutputContract.BooleanMatch);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleConditionProjectionDto>(valid.Error);

        return new RuleConditionProjectionDto(
            RuleContractMapper.ToDto(draft.Value.Condition),
            DisplayCompiler.Compile(
                draft.Value.Condition,
                draft.Value.Inputs,
                NormalizeLanguage(request.Language)));
    }

    private static string NormalizeLanguage(string? language) =>
        language?.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true
            ? "vi"
            : "en";
}
