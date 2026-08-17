using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly RuleSubjectReference Actor = RuleSubjectReference.Human(UserId);
    private static readonly DateTime Now = new(2026, 7, 10, 3, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateVersion_WhenDraftChangesLater_PreservesImmutableSnapshotAndDraftEditability()
    {
        RuleDefinition definition = Draft();
        Configure(definition);

        RuleDefinitionVersion versionOne = definition.CreateVersion(definition.Revision, Actor, Now.AddMinutes(1)).Value;
        definition.Status.Should().Be(RuleLifecycleStatus.Inactive);
        definition.ActiveVersion.Should().BeNull();

        definition.SaveDraft(
                definition.Revision,
                "Renamed amount approval",
                definition.Description,
                Inputs(label: "Amount to approve"),
                Condition(),
                Actor,
                Now.AddMinutes(2))
            .IsSuccess.Should().BeTrue();
        RuleDefinitionVersion versionTwo = definition.CreateVersion(definition.Revision, Actor, Now.AddMinutes(3)).Value;

        versionOne.Version.Should().Be(1);
        versionTwo.Version.Should().Be(2);
        versionOne.Name.Should().Be("Amount approval");
        versionOne.Inputs.Single().Should().Match<RuleInputDefinition>(input =>
            input.Key == "amount" && input.Label == "Amount");
        versionTwo.Inputs.Single().Should().Match<RuleInputDefinition>(input =>
            input.Key == "amount" && input.Label == "Amount to approve");
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
            Actor,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        definition.Condition.Should().BeNull();
    }

    [Fact]
    public void Lifecycle_WhenActivatedThenDeactivated_ChangesOnlyExactActivation()
    {
        RuleDefinition definition = Draft();
        Configure(definition);
        RuleDefinitionVersion version = definition.CreateVersion(definition.Revision, Actor, Now).Value;

        definition.ActivateVersion(definition.Revision, version.Version, Actor, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        definition.Status.Should().Be(RuleLifecycleStatus.Active);
        definition.ActiveVersion.Should().Be(version.Version);

        definition.Deactivate(definition.Revision, Actor, Now.AddMinutes(2)).IsSuccess.Should().BeTrue();

        definition.Status.Should().Be(RuleLifecycleStatus.Inactive);
        definition.ActiveVersion.Should().BeNull();
        definition.FindVersion(1).Should().BeSameAs(version);
    }

    [Fact]
    public void Lifecycle_WhenArchived_ClearsActivationAndPreservesVersion()
    {
        RuleDefinition definition = Draft();
        Configure(definition);
        RuleDefinitionVersion version = definition.CreateVersion(definition.Revision, Actor, Now).Value;
        definition.ActivateVersion(definition.Revision, version.Version, Actor, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        definition.Archive(definition.Revision, Actor, Now.AddMinutes(2)).IsSuccess.Should().BeTrue();

        definition.Status.Should().Be(RuleLifecycleStatus.Archived);
        definition.ActiveVersion.Should().BeNull();
        definition.FindVersion(1).Should().BeSameAs(version);
    }

    [Fact]
    public void ActivateVersion_WhenRevisionIsStaleOrVersionUnknown_DoesNotChangeActivation()
    {
        RuleDefinition definition = Draft();
        Configure(definition);
        definition.CreateVersion(definition.Revision, Actor, Now).IsSuccess.Should().BeTrue();

        Result stale = definition.ActivateVersion(1, 1, Actor, Now.AddMinutes(1));
        Result unknown = definition.ActivateVersion(definition.Revision, 2, Actor, Now.AddMinutes(1));

        stale.ErrorCode.Should().Be(ErrorCodes.Conflict);
        unknown.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        definition.ActiveVersion.Should().BeNull();
    }

    [Fact]
    public void CreateDraft_WhenTypedKeysAreDefault_ReturnsFailure() =>
        RuleDefinition.CreateDraft(
            WorkspaceId,
            default,
            "Amount approval",
            "Requires approval for high-value records.",
            Actor,
            ActorSnapshot.User(Actor.Id, "Ada Lovelace"),
            Now).IsFailure.Should().BeTrue();

    [Fact]
    public void CreateBuiltIn_WhenTypedKeyIsDefault_ReturnsFailure()
    {
        RuleDefinition template = BuiltInRuleCatalog.Definitions[0];

        RuleDefinition.CreateBuiltIn(
            default,
            1,
            template.Name,
            template.Description,
            template.Documentation!,
            template.Inputs,
            template.Condition!,
            template.Output,
            Now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Input_WhenAllowedValuesAreEmpty_AllowsMultipleAcceptedTypes()
    {
        Result<RuleInputDefinition> result = RuleInputDefinition.CreateBuiltIn(
            "value",
            "Value",
            [RuleValueType.Integer, RuleValueType.Decimal],
            isRequired: false,
            allowMultiple: false,
            allowedValues: []);

        result.IsSuccess.Should().BeTrue();
        result.Value.AllowedValues.Should().BeEmpty();
    }

    [Fact]
    public void Input_WhenAllowedValuesExceedDomainBound_ReturnsFailure() =>
        RuleInputDefinition.CreateBuiltIn(
            "value",
            "Value",
            [RuleValueType.Text],
            isRequired: false,
            allowMultiple: true,
            allowedValues: Enumerable.Range(0, 1001)
                .Select(index => $"allowed-{index}")
                .ToArray()).IsFailure.Should().BeTrue();

    [Fact]
    public void Input_WhenLabelChanges_KeepsStableKey()
    {
        RuleInputDefinition original = RuleInputDefinition.Create(
            "input_0123456789abcdef0123456789abcdef",
            "Ngày bắt đầu",
            RuleValueType.Date,
            isRequired: true).Value;
        RuleInputDefinition renamed = RuleInputDefinition.Create(
            original.Key,
            "Start date",
            RuleValueType.Date,
            isRequired: true).Value;

        renamed.Key.Should().Be(original.Key);
        renamed.Label.Should().Be("Start date");
    }

    private static RuleDefinition Draft() => RuleDefinition.CreateDraft(
        WorkspaceId,
        RuleDefinitionKey.Create("amount_approval").Value,
        "Amount approval",
        "Requires approval for high-value records.",
        Actor,
        ActorSnapshot.User(Actor.Id, "Ada Lovelace"),
        Now).Value;

    private static void Configure(RuleDefinition definition) =>
        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                Inputs(),
                Condition(),
                Actor,
                Now)
            .IsSuccess.Should().BeTrue();

    private static IReadOnlyList<RuleInputDefinition> Inputs(string label = "Amount") =>
        [RuleInputDefinition.Create("amount", label, RuleValueType.Decimal, true).Value];

    private static RuleConditionNode Condition() => RulePredicateCondition.Create(
        "amount-check",
        RulePredicateOperator.GreaterThan,
        RuleOperand.Input("amount").Value,
        RuleOperand.LiteralValue(RuleValue.Create(RuleValueType.Decimal, ["1000"]).Value).Value).Value;
}
