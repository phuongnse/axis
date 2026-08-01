using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using ContractRuleInputMappingKind = Axis.Rules.Contracts.RuleInputMappingKind;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;

namespace Axis.Rules.Application.Tests;

internal static class RuleBindingHandlerTestData
{
    public static CreateRuleBindingRequest Request(string targetId) =>
        new(
            "field.required",
            1,
            "invoice-field",
            targetId,
            "record.validate",
            Mappings());

    public static Dictionary<string, RuleInputMappingDto> Mappings() =>
        new(StringComparer.Ordinal)
        {
            ["value"] = new(ContractRuleInputMappingKind.Literal, null, ["Approved"]),
        };

    public static RuleBinding Binding(string targetId = "field-1") =>
        RuleBinding.Create(
            RuleDefinitionHandlerTestContext.WorkspaceId,
            RuleDefinitionKey.Create("field.required").Value,
            1,
            "invoice-field",
            targetId,
            "record.validate",
            new Dictionary<string, RuleInputMapping>(StringComparer.Ordinal)
            {
                ["value"] = RuleInputMapping.FromLiteral(["Approved"]).Value,
            },
            0,
            true,
            DomainFailureBehavior.FailClosed,
            RuleDefinitionHandlerTestContext.UserId,
            DateTime.UtcNow).Value;
}
