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
        Configure(definition, "Amount exceeds approval threshold");

        RuleDefinitionVersion versionOne = definition.Publish(
            definition.Revision,
            UserId,
            Now.AddMinutes(1)).Value;
        definition.StartNextDraft(definition.Revision, UserId, Now.AddMinutes(2)).IsSuccess.Should().BeTrue();
        Configure(definition, "Amount requires executive approval");
        RuleDefinitionVersion versionTwo = definition.Publish(
            definition.Revision,
            UserId,
            Now.AddMinutes(3)).Value;

        versionOne.Version.Should().Be(1);
        definition.ExpressionLanguageVersion.Should().Be(RuleExpressionLanguage.Version);
        versionOne.ExpressionLanguageVersion.Should().Be(RuleExpressionLanguage.Version);
        versionTwo.ExpressionLanguageVersion.Should().Be(RuleExpressionLanguage.Version);
        versionOne.Outcome.Should().BeOfType<RuleValidationOutcome>()
            .Which.Message.Should().Be("Amount exceeds approval threshold");
        versionTwo.Version.Should().Be(2);
        versionTwo.Outcome.Should().BeOfType<RuleValidationOutcome>()
            .Which.Message.Should().Be("Amount requires executive approval");
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
            definition.Scope,
            definition.ContextKey,
            definition.ContextSchemaVersion,
            definition.OutcomeKind,
            [],
            Condition(),
            Outcome("Should not save"),
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
        Configure(definition, "Approval required");
        RuleDefinitionVersion published = definition.Publish(definition.Revision, UserId, Now).Value;

        definition.Archive(definition.Revision, UserId, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        definition.Status.Should().Be(RuleLifecycleStatus.Archived);
        definition.FindVersion(1).Should().BeSameAs(published);
    }

    [Fact]
    public void CreateDraft_WhenTypedKeysAreDefault_ReturnsFailure()
    {
        Result<RuleDefinition> result = RuleDefinition.CreateDraft(
            WorkspaceId,
            default,
            "Amount approval",
            "Requires approval for high-value records.",
            RuleScope.Record,
            default,
            contextSchemaVersion: 1,
            RuleOutcomeKind.Validation,
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SaveDraft_WhenContextKeyIsDefault_ReturnsFailureWithoutMutation()
    {
        RuleDefinition definition = Draft();

        Result result = definition.SaveDraft(
            definition.Revision,
            definition.Name,
            definition.Description,
            definition.Scope,
            default,
            definition.ContextSchemaVersion,
            definition.OutcomeKind,
            [],
            Condition(),
            Outcome("Should not save"),
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
        definition.Condition.Should().BeNull();
    }

    private static RuleDefinition Draft() => RuleDefinition.CreateDraft(
        WorkspaceId,
        RuleDefinitionKey.Create("amount_approval").Value,
        "Amount approval",
        "Requires approval for high-value records.",
        RuleScope.Record,
        RuleContextKey.Create("objects.record").Value,
        contextSchemaVersion: 1,
        RuleOutcomeKind.Validation,
        UserId,
        Now).Value;

    private static void Configure(RuleDefinition definition, string message)
    {
        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                definition.Scope,
                definition.ContextKey,
                definition.ContextSchemaVersion,
                definition.OutcomeKind,
                [],
                Condition(),
                Outcome(message),
                UserId,
                Now)
            .IsSuccess.Should().BeTrue();
    }

    private static RuleConditionNode Condition() => RulePredicateCondition.Create(
        "amount-check",
        RulePredicateOperator.GreaterThan,
        RuleOperand.Context("record.amount").Value,
        RuleOperand.LiteralValue(RuleValue.Create(RuleValueType.Decimal, ["1000"]).Value).Value).Value;

    private static RuleOutcome Outcome(string message) =>
        RuleValidationOutcome.Create("record.amount_approval", RuleSeverity.Error, message).Value;
}
