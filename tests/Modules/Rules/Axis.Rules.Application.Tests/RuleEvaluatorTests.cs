using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleEvaluatorTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task EvaluateAsync_WhenSystemValidationMatches_ReturnsViolationAndDenies()
    {
        RuleEvaluator sut = CreateSut();

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.TextLength,
                DefinitionVersion: 1,
                Params(("max", ["3"])))],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
            {
                ["field.value"] = new(RuleValueType.Text, ["abcd"]),
            },
            CorrelationId: "test-correlation"));

        result.IsSuccess.Should().BeTrue();
        result.IsAllowed.Should().BeFalse();
        result.ContextSchemaVersion.Should().Be(1);
        result.Violations.Should().ContainSingle(violation =>
            violation.DefinitionKey == RuleDefinitionKeys.TextLength &&
            violation.DefinitionVersion == 1);
        result.Items.Should().ContainSingle(item => item.IsMatch);
        result.CorrelationId.Should().Be("test-correlation");
    }

    [Fact]
    public async Task EvaluateAsync_WhenExactVersionCannotResolve_FailsClosedWithoutPartialItems()
    {
        RuleEvaluator sut = CreateSut();

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.Required,
                DefinitionVersion: 99,
                Params())],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal),
            CorrelationId: "test-correlation"));

        result.IsSuccess.Should().BeFalse();
        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("version_not_found");
        result.Items.Should().BeEmpty();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WhenSystemRuleIsIncompatibleWithContext_FailsBeforeEvaluation()
    {
        RuleEvaluator sut = CreateSut();

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.NumericRange,
                DefinitionVersion: 1,
                Params(("min", ["0"])))],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
            {
                ["field.value"] = new(RuleValueType.Text, ["10"]),
            },
            CorrelationId: "test-correlation"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("rule_incompatible");
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WhenSystemParametersAreSemanticallyInvalid_FailsClosed()
    {
        RuleEvaluator sut = CreateSut();

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.decimal",
            ContextSchemaVersion: 1,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.NumericRange,
                DefinitionVersion: 1,
                Params(("min", ["10"]), ("max", ["2"])))],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
            {
                ["field.value"] = new(RuleValueType.Decimal, ["5"]),
            },
            CorrelationId: "test-correlation"));

        result.IsSuccess.Should().BeFalse();
        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("range_invalid");
    }

    [Fact]
    public async Task EvaluateAsync_WhenMultipleValidationsMatch_ReturnsEveryViolationInCallerOrder()
    {
        RuleEvaluator sut = CreateSut();

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [
                new RuleEvaluationReference(
                    RuleDefinitionKeys.TextPattern,
                    DefinitionVersion: 1,
                    Params(("pattern", ["^[A-Z]+$"]))),
                new RuleEvaluationReference(
                    RuleDefinitionKeys.TextLength,
                    DefinitionVersion: 1,
                    Params(("max", ["3"]))),
            ],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
            {
                ["field.value"] = new(RuleValueType.Text, ["abcd"]),
            },
            CorrelationId: "validation-order"));

        result.IsSuccess.Should().BeTrue();
        result.IsAllowed.Should().BeFalse();
        result.Violations.Select(violation => violation.DefinitionKey).Should().Equal(
            RuleDefinitionKeys.TextPattern,
            RuleDefinitionKeys.TextLength);
        result.Items.Select(item => item.DefinitionKey).Should().Equal(
            RuleDefinitionKeys.TextPattern,
            RuleDefinitionKeys.TextLength);
    }

    [Fact]
    public async Task EvaluateAsync_WhenArchivedWorkspaceDecisionDenyMatches_DenyWinsWithoutLosingExactVersion()
    {
        Axis.Rules.Domain.RuleDefinition allow = PublishedDecision("allow_blocked", Axis.Rules.Domain.RuleDecision.Allow);
        Axis.Rules.Domain.RuleDefinition deny = PublishedDecision("deny_blocked", Axis.Rules.Domain.RuleDecision.Deny);
        deny.Archive(deny.Revision, UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        IRuleDefinitionRepository repository = Substitute.For<IRuleDefinitionRepository>();
        repository.GetByKeyForWorkspaceAsync(
                Arg.Any<Axis.Rules.Domain.RuleDefinitionKey>(),
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Axis.Rules.Domain.RuleDefinitionKey key = call.ArgAt<Axis.Rules.Domain.RuleDefinitionKey>(0);
                return key.Value == allow.Key.Value ? allow : key.Value == deny.Key.Value ? deny : null;
            });
        RuleEvaluator sut = CreateSut(repository);

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            RuleOutcomeKind.Decision,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [
                new RuleEvaluationReference(allow.Key.Value, DefinitionVersion: 1, Params()),
                new RuleEvaluationReference(deny.Key.Value, DefinitionVersion: 1, Params()),
            ],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
            {
                ["field.value"] = new(RuleValueType.Text, ["blocked"]),
            },
            CorrelationId: "decision-order"));

        result.IsSuccess.Should().BeTrue();
        result.IsAllowed.Should().BeFalse();
        result.Items.Should().HaveCount(2).And.OnlyContain(item => item.IsMatch);
        result.Items.Select(item => item.Outcome?.Decision).Should().Equal(
            RuleDecision.Allow,
            RuleDecision.Deny);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCancelled_ThrowsWithoutReturningPartialSuccess()
    {
        RuleEvaluator sut = CreateSut();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RuleEvaluationRequest request = new(
            WorkspaceId,
            RuleOutcomeKind.Validation,
            RuleScope.Field,
            "business_objects.field.text",
            ContextSchemaVersion: 1,
            [new RuleEvaluationReference(RuleDefinitionKeys.Required, DefinitionVersion: 1, Params())],
            new Dictionary<string, RuleValueDto>(StringComparer.Ordinal),
            CorrelationId: "cancelled");

        Func<Task> act = () => sut.EvaluateAsync(request, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static RuleEvaluator CreateSut(IRuleDefinitionRepository? repository = null)
    {
        RuleContextSchemaRegistry registry = new([new TextContextSchemaProvider()]);
        return new RuleEvaluator(registry, repository ?? Substitute.For<IRuleDefinitionRepository>());
    }

    private static Axis.Rules.Domain.RuleDefinition PublishedDecision(
        string key,
        Axis.Rules.Domain.RuleDecision decision)
    {
        Axis.Rules.Domain.RuleDefinition definition = Axis.Rules.Domain.RuleDefinition.CreateDraft(
            WorkspaceId,
            Axis.Rules.Domain.RuleDefinitionKey.Create(key).Value,
            key,
            "Workspace decision rule.",
            Axis.Rules.Domain.RuleScope.Field,
            Axis.Rules.Domain.RuleContextKey.Create("business_objects.field.text").Value,
            contextSchemaVersion: 1,
            Axis.Rules.Domain.RuleOutcomeKind.Decision,
            UserId,
            DateTime.UtcNow).Value;
        Axis.Rules.Domain.RulePredicateCondition condition = Axis.Rules.Domain.RulePredicateCondition.Create(
            $"{key}-condition",
            Axis.Rules.Domain.RulePredicateOperator.Equal,
            Axis.Rules.Domain.RuleOperand.Context("field.value").Value,
            Axis.Rules.Domain.RuleOperand.LiteralValue(
                Axis.Rules.Domain.RuleValue.Create(
                    Axis.Rules.Domain.RuleValueType.Text,
                    ["blocked"]).Value).Value).Value;
        Axis.Rules.Domain.RuleDecisionOutcome outcome =
            Axis.Rules.Domain.RuleDecisionOutcome.Create(decision).Value;
        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                definition.Scope,
                definition.ContextKey,
                definition.ContextSchemaVersion,
                definition.OutcomeKind,
                [],
                condition,
                outcome,
                UserId,
                DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        definition.Publish(definition.Revision, UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);

    private sealed class TextContextSchemaProvider : IRuleContextSchemaProvider
    {
        private static readonly IReadOnlyList<RuleContextSchemaDto> Schemas =
        [
            new(
                "business_objects.field.text",
                Version: 1,
                RuleScope.Field,
                "Text field value",
                [new RuleContextFieldDto("field.value", "Field value", RuleValueType.Text, AllowMultiple: false)],
                TargetTypeKey: "Text"),
            new(
                "business_objects.field.decimal",
                Version: 1,
                RuleScope.Field,
                "Decimal field value",
                [new RuleContextFieldDto("field.value", "Field value", RuleValueType.Decimal, AllowMultiple: false)],
                TargetTypeKey: "Decimal"),
        ];

        public Task<IReadOnlyList<RuleContextSchemaDto>> ListSchemasAsync(
            Guid workspaceId,
            RuleScope? scope = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Schemas);

        public Task<RuleContextSchemaDto?> FindSchemaAsync(
            Guid workspaceId,
            string contextKey,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Schemas.SingleOrDefault(schema =>
                contextKey == schema.ContextKey && version == schema.Version));
    }
}
