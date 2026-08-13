using Axis.Identity.Contracts;
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
        CurrentSubject.Subject.Returns(SubjectReference.Human(UserId));
        Authorization.AuthorizeAsync(
                Arg.Any<Guid>(),
                Arg.Any<SubjectReference>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceProductBuilderDecision.Allowed);
    }

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public ICurrentSubject CurrentSubject { get; } = Substitute.For<ICurrentSubject>();
    public IWorkspaceProductBuilderAuthorization Authorization { get; } =
        Substitute.For<IWorkspaceProductBuilderAuthorization>();
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
            RuleSubjectReference.Human(UserId),
            DateTime.UtcNow).Value;

        if (configured)
            Configure(definition);

        return definition;
    }

    public static RuleDefinition VersionedDefinition()
    {
        RuleDefinition definition = DraftDefinition(configured: true);
        definition.CreateVersion(definition.Revision, RuleSubjectReference.Human(UserId), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static RuleDefinition ActiveDefinition()
    {
        RuleDefinition definition = VersionedDefinition();
        definition.ActivateVersion(definition.Revision, 1, RuleSubjectReference.Human(UserId), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static RuleDefinition ConfiguredDraft() => DraftDefinition(configured: true);

    public static void Configure(RuleDefinition definition)
    {
        RuleInputDefinition value = RuleInputDefinition.Create(
            "value", "Value", DomainValueType.Decimal, isRequired: true).Value;
        RuleInputDefinition threshold = RuleInputDefinition.Create(
            "threshold", "Threshold", DomainValueType.Decimal, isRequired: true).Value;
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
                RuleSubjectReference.Human(UserId),
                DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
    }

    public static IReadOnlyList<RuleDraftInputDefinitionDto> DraftInputsDto() =>
    [
        new("value", "Value", [ContractValueType.Decimal], true, false, []),
        new("threshold", "Threshold", [ContractValueType.Decimal], true, false, []),
    ];

    public static RuleConditionNodeDto ConditionDto() => new(
        "threshold_check",
        LogicalOperator: null,
        ContractPredicateOperator.GreaterThan,
        new RuleOperandDto(ContractOperandKind.Input, "value", Literal: null),
        new RuleOperandDto(ContractOperandKind.Input, "threshold", Literal: null),
        []);
}
