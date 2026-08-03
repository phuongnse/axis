using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;
using NSubstitute;
using ContractRuleValueType = Axis.Rules.Contracts.RuleValueType;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;

namespace Axis.Rules.Application.Tests;

public sealed class RuleBindingEvaluatorTests
{
    [Fact]
    public async Task EvaluateBinding_WhenNeutralConsumerMapsItsOwnContext_UsesMappedTypedValues()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        RuleDefinitionKey key = RuleDefinitionKey.Create("field.required").Value;
        RuleBinding binding = RuleBinding.Create(
            workspaceId,
            key,
            1,
            "invoice-line",
            "line-42",
            "invoice.validate",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("payload.description").Value,
            },
            priority: 0,
            enabled: true,
            DomainFailureBehavior.FailClosed,
            userId,
            DateTime.UtcNow).Value;

        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>())
            .Returns(binding);
        IRuleEvaluator evaluator = Substitute.For<IRuleEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<RuleEvaluationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                RuleEvaluationRequest request = call.Arg<RuleEvaluationRequest>();
                RuleEvaluationReference reference = request.Rules.Single();
                reference.Inputs["value"].Should().Equal("Approved");
                reference.InputTypes!["value"].Should().Be(ContractRuleValueType.Text);
                return new RuleEvaluationResult(
                    true,
                    [new RuleEvaluationItemDto(key.Value, 1, true, [])],
                    request.CorrelationId,
                    null,
                    null);
            });

        IRuleContextAdapter<InvoiceLineContext> adapter = new InvoiceLineContextAdapter();
        RuleBindingEvaluator sut = new(repository, evaluator);
        RuleEvaluationResult result = await sut.EvaluateBindingAsync(
            new RuleBindingEvaluationRequest(
                workspaceId,
                binding.Id.Value,
                adapter.CreateContext(new InvoiceLineContext("Approved")),
                "neutral-consumer-test"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Single().DefinitionKey.Should().Be(key.Value);
        await evaluator.Received(1).EvaluateAsync(Arg.Any<RuleEvaluationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateBinding_WhenOptionalMappedContextIsAbsent_EvaluatesAsAbsentInput()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinitionKey key = RuleDefinitionKey.Create(RuleDefinitionKeys.Required).Value;
        RuleBinding binding = Binding(workspaceId, key);
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>())
            .Returns(binding);
        RuleBindingEvaluator sut = new(repository, new RuleEvaluator(Substitute.For<IRuleDefinitionRepository>()));

        RuleEvaluationResult result = await sut.EvaluateBindingAsync(
            new RuleBindingEvaluationRequest(
                workspaceId,
                binding.Id.Value,
                new RuleContext(new Dictionary<string, RuleContextValue>()),
                "absent-optional-context"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().ContainSingle(item => !item.IsMatch);
    }

    [Fact]
    public async Task EvaluateBinding_WhenCurrentBindingIsDisabled_UsesEnabledHistoricalRevision()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        RuleDefinitionKey key = RuleDefinitionKey.Create(RuleDefinitionKeys.Required).Value;
        RuleBinding binding = Binding(workspaceId, key);
        binding.Update(
            expectedRevision: 1,
            key,
            1,
            "invoice-line",
            "line-42",
            "invoice.validate",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("payload.description").Value,
            },
            priority: 0,
            enabled: false,
            DomainFailureBehavior.FailClosed,
            userId,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>())
            .Returns(binding);
        IRuleEvaluator evaluator = Substitute.For<IRuleEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<RuleEvaluationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RuleEvaluationResult(
                true,
                [new RuleEvaluationItemDto(key.Value, 1, true, [])],
                "historical-revision",
                null,
                null));

        RuleBindingEvaluator sut = new(repository, evaluator);
        RuleEvaluationResult result = await sut.EvaluateBindingAsync(
            new RuleBindingEvaluationRequest(
                workspaceId,
                binding.Id.Value,
                new RuleContext(new Dictionary<string, RuleContextValue>
                {
                    ["payload.description"] = new(ContractRuleValueType.Text, ["Approved"]),
                }),
                "historical-revision",
                BindingRevision: 1),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await evaluator.Received(1).EvaluateAsync(Arg.Any<RuleEvaluationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateBinding_WhenRequiredMappedContextIsAbsent_ReturnsInputFailure()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinitionKey key = RuleDefinitionKey.Create(RuleDefinitionKeys.NumericRange).Value;
        RuleBinding binding = Binding(workspaceId, key);
        IRuleBindingRepository repository = Substitute.For<IRuleBindingRepository>();
        repository.GetByIdForWorkspaceAsync(binding.Id, workspaceId, Arg.Any<CancellationToken>())
            .Returns(binding);
        RuleBindingEvaluator sut = new(repository, new RuleEvaluator(Substitute.For<IRuleDefinitionRepository>()));

        RuleEvaluationResult result = await sut.EvaluateBindingAsync(
            new RuleBindingEvaluationRequest(
                workspaceId,
                binding.Id.Value,
                new RuleContext(new Dictionary<string, RuleContextValue>()),
                "absent-required-context"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("input_invalid");
    }

    private static RuleBinding Binding(Guid workspaceId, RuleDefinitionKey key) =>
        RuleBinding.Create(
            workspaceId,
            key,
            1,
            "invoice-line",
            "line-42",
            "invoice.validate",
            new Dictionary<string, RuleInputMapping>
            {
                ["value"] = RuleInputMapping.FromContext("payload.description").Value,
            },
            priority: 0,
            enabled: true,
            DomainFailureBehavior.FailClosed,
            Guid.NewGuid(),
            DateTime.UtcNow).Value;

    private sealed record InvoiceLineContext(string Description);

    private sealed class InvoiceLineContextAdapter : IRuleContextAdapter<InvoiceLineContext>
    {
        public string TargetType => "invoice-line";

        public RuleContext CreateContext(InvoiceLineContext consumerContext) =>
            new(new Dictionary<string, RuleContextValue>
            {
                ["payload.description"] = new(ContractRuleValueType.Text, [consumerContext.Description]),
            });
    }
}
