using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleEvaluatorTests
{
    private static readonly Guid WorkspaceId = RuleDefinitionHandlerTestContext.WorkspaceId;

    [Fact]
    public async Task EvaluateAsync_WhenBuiltInRuleAssertionIsSatisfied_ReturnsMatchDiagnostics()
    {
        RuleEvaluator sut = new(Substitute.For<IRuleDefinitionRepository>());

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.TextLength,
                1,
                Inputs(("value", ["abcd"]), ("max", ["4"])))],
            "test-correlation"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.IsMatch);
        result.CorrelationId.Should().Be("test-correlation");
    }

    [Theory]
    [InlineData("Axis", true)]
    [InlineData("   ", false)]
    public async Task EvaluateAsync_WhenRequiredReceivesText_ReturnsAssertionState(
        string value,
        bool expectedMatch)
    {
        RuleEvaluator sut = new(Substitute.For<IRuleDefinitionRepository>());

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(
                RuleDefinitionKeys.Required,
                1,
                Inputs(("value", [value])))],
            "required-text"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.IsMatch == expectedMatch);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRequiredValueIsAbsent_ReturnsNonMatch()
    {
        RuleEvaluator sut = new(Substitute.For<IRuleDefinitionRepository>());

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(RuleDefinitionKeys.Required, 1, Inputs())],
            "required-absent"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => !item.IsMatch);
    }

    [Fact]
    public async Task EvaluateAsync_WhenExactVersionCannotResolve_FailsClosed()
    {
        RuleEvaluator sut = new(Substitute.For<IRuleDefinitionRepository>());
        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(RuleDefinitionKeys.Required, 99, Inputs())],
            "test-correlation"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("version_not_found");
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceRuleIsPublished_ResolvesExactVersion()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.VersionedDefinition();
        IRuleDefinitionRepository repository = Substitute.For<IRuleDefinitionRepository>();
        repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        RuleEvaluator sut = new(repository);

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(
                definition.Key.Value,
                1,
                Inputs(("value", ["15"]), ("threshold", ["10"])))],
            "workspace-test"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.IsMatch);
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceRuleIsArchived_ResolvesItsExactPublishedVersion()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.VersionedDefinition();
        definition.Archive(definition.Revision, RuleSubjectReference.Human(RuleDefinitionHandlerTestContext.UserId), DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        IRuleDefinitionRepository repository = Substitute.For<IRuleDefinitionRepository>();
        repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        RuleEvaluator sut = new(repository);

        RuleEvaluationResult result = await sut.EvaluateAsync(new RuleEvaluationRequest(
            WorkspaceId,
            [new RuleEvaluationReference(
                definition.Key.Value,
                1,
                Inputs(("value", ["15"]), ("threshold", ["10"])))],
            "archived-workspace-test"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.IsMatch);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCancelled_Throws()
    {
        RuleEvaluator sut = new(Substitute.For<IRuleDefinitionRepository>());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => sut.EvaluateAsync(
            new RuleEvaluationRequest(WorkspaceId, [], "cancelled"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs(
        params (string Key, string[] Values)[] inputs) =>
        inputs.ToDictionary(
            input => input.Key,
            input => (IReadOnlyList<string>)input.Values,
            StringComparer.Ordinal);
}
