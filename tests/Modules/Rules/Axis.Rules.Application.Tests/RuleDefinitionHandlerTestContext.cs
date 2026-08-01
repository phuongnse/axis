using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Search;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.Identity;
using FluentAssertions;
using NSubstitute;
using ContractOperandKind = Axis.Rules.Contracts.RuleOperandKind;
using ContractPredicateOperator = Axis.Rules.Contracts.RulePredicateOperator;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application.Tests;

internal sealed class RuleDefinitionHandlerTestContext
{
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public RuleDefinitionHandlerTestContext()
    {
        CurrentUser.UserId.Returns(UserId);
        CurrentUser.workspaceId.Returns(WorkspaceId);
    }

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public IRuleDefinitionRepository Repository { get; } = Substitute.For<IRuleDefinitionRepository>();
    public IRuleCatalogSearchProvider CatalogSearch { get; } = Substitute.For<IRuleCatalogSearchProvider>();
    public IRuleTextSearchProvider TextSearch { get; } = Substitute.For<IRuleTextSearchProvider>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public static RuleDefinition DraftDefinition(bool configured = false)
    {
        RuleDefinition definition = RuleDefinition.CreateDraft(
            WorkspaceId,
            RuleDefinitionKey.Create("credit_threshold").Value,
            "Credit threshold",
            "Flags high credit values.",
            UserId,
            DateTime.UtcNow).Value;

        if (configured)
            Configure(definition);

        return definition;
    }

    public static RuleDefinition PublishedDefinition()
    {
        RuleDefinition definition = DraftDefinition(configured: true);
        definition.Publish(definition.Revision, UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static void Configure(RuleDefinition definition)
    {
        RuleInputDefinition value = RuleInputDefinition.Create(
            "value", DomainValueType.Decimal, isRequired: true).Value;
        RuleInputDefinition threshold = RuleInputDefinition.Create(
            "threshold", DomainValueType.Decimal, isRequired: true).Value;
        RulePredicateCondition condition = RulePredicateCondition.Create(
            "threshold_check",
            DomainPredicateOperator.GreaterThan,
            RuleOperand.Input("value").Value,
            RuleOperand.Input("threshold").Value).Value;

        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                [value, threshold],
                condition,
                UserId,
                DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
    }

    public static IReadOnlyList<RuleDraftInputDefinitionDto> DraftInputsDto() =>
    [
        new("Value", [ContractValueType.Decimal], true, false, []),
        new("Threshold", [ContractValueType.Decimal], true, false, []),
    ];

    public static RuleConditionNodeDto ConditionDto() => new(
        "threshold_check",
        LogicalOperator: null,
        ContractPredicateOperator.GreaterThan,
        new RuleOperandDto(ContractOperandKind.Input, "Value", Literal: null),
        new RuleOperandDto(ContractOperandKind.Input, "Threshold", Literal: null),
        []);
}
