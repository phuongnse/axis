using Axis.Rules.Application.Commands.PublishRuleDefinition;
using Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleDefinitionHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task SaveDraft_WhenDatabaseConcurrencyWins_ReturnsConflictProblem()
    {
        RuleDefinition definition = DraftDefinition();
        ICurrentUser currentUser = CurrentUser(UserId);
        IRuleDefinitionRepository repository = Substitute.For<IRuleDefinitionRepository>();
        repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new ConcurrencyException());
        SaveRuleDefinitionDraftHandler sut = new(
            currentUser,
            ContextRegistry(),
            repository,
            unitOfWork);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            SaveCommand(definition.Key.Value),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionConflict);
    }

    [Fact]
    public async Task Publish_WhenCurrentUserIsMissing_ReturnsUserScopeProblem()
    {
        ICurrentUser currentUser = CurrentUser(userId: null);
        PublishRuleDefinitionHandler sut = new(
            currentUser,
            ContextRegistry(),
            Substitute.For<IRuleDefinitionRepository>(),
            Substitute.For<IUnitOfWork>());

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new PublishRuleDefinitionCommand("credit_threshold", ExpectedRevision: 2),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(RulesProblemCodes.UserScopeRequired);
    }

    private static RuleDefinition DraftDefinition()
    {
        Result<RuleDefinition> result = RuleDefinition.CreateDraft(
            WorkspaceId,
            RuleDefinitionKey.Create("credit_threshold").Value,
            "Credit threshold",
            "Flags high credit values.",
            Axis.Rules.Domain.RuleScope.Field,
            RuleContextKey.Create("business_objects.field.decimal").Value,
            1,
            Axis.Rules.Domain.RuleOutcomeKind.Validation,
            UserId,
            DateTime.UtcNow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static SaveRuleDefinitionDraftCommand SaveCommand(string key) =>
        new(
            key,
            ExpectedRevision: 1,
            "Credit threshold",
            "Flags high credit values.",
            Contracts.RuleScope.Field,
            "business_objects.field.decimal",
            ContextSchemaVersion: 1,
            Contracts.RuleOutcomeKind.Validation,
            [new RuleParameterDefinitionDto("threshold", Contracts.RuleValueType.Decimal, true, false, [])],
            new RuleConditionNodeDto(
                "threshold_check",
                LogicalOperator: null,
                Contracts.RulePredicateOperator.GreaterThan,
                new RuleOperandDto(Contracts.RuleOperandKind.Context, "field.value", Literal: null),
                new RuleOperandDto(Contracts.RuleOperandKind.Parameter, "threshold", Literal: null),
                []),
            new RuleOutcomeDto(
                Contracts.RuleOutcomeKind.Validation,
                "credit.threshold.exceeded",
                Contracts.RuleSeverity.Error,
                "Credit value exceeds the configured threshold.",
                Decision: null));

    private static RuleContextSchemaRegistry ContextRegistry()
    {
        IRuleContextSchemaProvider provider = Substitute.For<IRuleContextSchemaProvider>();
        RuleContextSchemaDto schema = new(
            "business_objects.field.decimal",
            Version: 1,
            Contracts.RuleScope.Field,
            "Decimal field value",
            [new RuleContextFieldDto("field.value", "Field value", Contracts.RuleValueType.Decimal, false)],
            TargetTypeKey: "Decimal");
        provider.FindSchemaAsync(
                WorkspaceId,
                schema.ContextKey,
                schema.Version,
                Arg.Any<CancellationToken>())
            .Returns(schema);
        return new RuleContextSchemaRegistry([provider]);
    }

    private static ICurrentUser CurrentUser(Guid? userId)
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.workspaceId.Returns(WorkspaceId);
        return currentUser;
    }
}
