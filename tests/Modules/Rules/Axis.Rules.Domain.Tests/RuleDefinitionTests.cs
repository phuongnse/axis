using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 7, 10, 3, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Lifecycle_WhenValid_PreservesImmutablePublishedVersions()
    {
        RuleDefinition definition = Draft();
        Configure(definition);

        RuleDefinitionVersion versionOne = definition.Publish(definition.Revision, UserId, Now.AddMinutes(1)).Value;
        definition.StartNextDraft(definition.Revision, UserId, Now.AddMinutes(2)).IsSuccess.Should().BeTrue();
        Configure(definition);
        RuleDefinitionVersion versionTwo = definition.Publish(definition.Revision, UserId, Now.AddMinutes(3)).Value;

        versionOne.Version.Should().Be(1);
        versionTwo.Version.Should().Be(2);
        versionOne.Condition.Should().NotBeNull();
        versionOne.Output.Should().Be(RuleOutputContract.BooleanMatch);
        definition.Output.Should().Be(RuleOutputContract.BooleanMatch);
        definition.FindVersion(1).Should().BeSameAs(versionOne);
    }

    [Fact]
    public void SaveDraft_WhenRevisionIsStale_ReturnsConflictWithoutMutation()
    {
        RuleDefinition definition = Draft();

        Result result = definition.SaveDraft(
            expectedRevision: 0,
            definition.Name,
            definition.Description,
            Inputs(),
            Condition(),
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        definition.Condition.Should().BeNull();
    }

    [Fact]
    public void Archive_WhenPublished_PreservesVersionResolution()
    {
        RuleDefinition definition = Draft();
        Configure(definition);
        RuleDefinitionVersion published = definition.Publish(definition.Revision, UserId, Now).Value;

        definition.Archive(definition.Revision, UserId, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        definition.Status.Should().Be(RuleLifecycleStatus.Archived);
        definition.FindVersion(1).Should().BeSameAs(published);
    }

    [Fact]
    public void CreateDraft_WhenTypedKeysAreDefault_ReturnsFailure() =>
        RuleDefinition.CreateDraft(
            WorkspaceId,
            default,
            "Amount approval",
            "Requires approval for high-value records.",
            UserId,
            Now).IsFailure.Should().BeTrue();

    [Fact]
    public void Input_WhenCreatedFromBusinessLabel_DerivesStableTechnicalKey()
    {
        RuleInputDefinition input = RuleInputDefinition.CreateFromLabel(
            "Ngày bắt đầu",
            RuleValueType.Date,
            isRequired: true).Value;

        input.Label.Should().Be("Ngày bắt đầu");
        input.Key.Should().MatchRegex("^ngay_bat_dau_[a-f0-9]{8}$");
        RuleInputDefinition.CreateFromLabel("Ngày bắt đầu", RuleValueType.Date, true)
            .Value.Key.Should().Be(input.Key);
    }

    private static RuleDefinition Draft() => RuleDefinition.CreateDraft(
        WorkspaceId,
        RuleDefinitionKey.Create("amount_approval").Value,
        "Amount approval",
        "Requires approval for high-value records.",
        UserId,
        Now).Value;

    private static void Configure(RuleDefinition definition) =>
        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                Inputs(),
                Condition(),
                UserId,
                Now)
            .IsSuccess.Should().BeTrue();

    private static IReadOnlyList<RuleInputDefinition> Inputs() =>
        [RuleInputDefinition.Create("amount", RuleValueType.Decimal, true).Value];

    private static RuleConditionNode Condition() => RulePredicateCondition.Create(
        "amount-check",
        RulePredicateOperator.GreaterThan,
        RuleOperand.Input("amount").Value,
        RuleOperand.LiteralValue(RuleValue.Create(RuleValueType.Decimal, ["1000"]).Value).Value).Value;
}
