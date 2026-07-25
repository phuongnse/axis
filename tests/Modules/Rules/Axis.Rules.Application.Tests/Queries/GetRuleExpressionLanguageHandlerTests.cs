using Axis.Rules.Application.Queries.GetRuleExpressionLanguage;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using DomainEvaluationLimits = Axis.Rules.Domain.RuleEvaluationLimits;
using DomainExpressionLanguage = Axis.Rules.Domain.RuleExpressionLanguage;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class GetRuleExpressionLanguageHandlerTests
{
    [Fact]
    public async Task Get_WhenRequested_ReturnsVersionedTypedCapabilitiesAndLimits()
    {
        GetRuleExpressionLanguageHandler sut = new();

        Result<RuleExpressionLanguageDto> result = await sut.Handle(
            new GetRuleExpressionLanguageQuery(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(DomainExpressionLanguage.Version);
        result.Value.Operators.Select(definition => definition.Operator)
            .Should().BeEquivalentTo(Enum.GetValues<RulePredicateOperator>());
        result.Value.Functions.Select(definition => definition.Function)
            .Should().BeEquivalentTo(Enum.GetValues<RuleExpressionFunction>());
        result.Value.LogicalOperators.Select(definition => definition.Operator)
            .Should().BeEquivalentTo(Enum.GetValues<RuleLogicalOperator>());
        result.Value.OperandKinds.Select(definition => definition.Kind)
            .Should().BeEquivalentTo(Enum.GetValues<RuleOperandKind>());
        result.Value.ValueTypes.Select(definition => definition.Type)
            .Should().BeEquivalentTo(Enum.GetValues<RuleValueType>());
        result.Value.Cardinalities.Select(definition => definition.Cardinality)
            .Should().BeEquivalentTo(Enum.GetValues<RuleExpressionCardinality>());
        result.Value.LimitDefinitions.Should().OnlyContain(definition =>
            definition.Value > 0 &&
            definition.Documentation.Locales.Keys.Order().SequenceEqual(new[] { "en", "vi" }));

        RuleExpressionFunctionDefinitionDto length = result.Value.Functions.Single(
            definition => definition.Function == RuleExpressionFunction.Length);
        length.Parameters.Should().ContainSingle();
        length.Parameters[0].AcceptedTypes.Should().Equal(RuleValueType.Text);
        length.Parameters[0].Cardinality.Should().Be(RuleExpressionCardinality.Scalar);
        length.ReturnType.Should().Be(RuleValueType.Integer);
        length.Documentation.Locales["en"].DisplayName.Should().Be("Length");
        length.Documentation.Locales["vi"].Usage.Should().NotBeNullOrWhiteSpace();
        result.Value.Operators.Should().OnlyContain(definition =>
            definition.Documentation.Locales.Keys.Order().SequenceEqual(new[] { "en", "vi" }));
        result.Value.Limits.MaxDepth.Should().Be(DomainEvaluationLimits.Default.MaxDepth);
        result.Value.Limits.MaxNodes.Should().Be(DomainEvaluationLimits.Default.MaxNodes);
        result.Value.Limits.MaxFunctionCalls.Should().Be(DomainEvaluationLimits.Default.MaxFunctionCalls);
        result.Value.Limits.MaxParameters.Should().Be(DomainEvaluationLimits.Default.MaxParameters);
        result.Value.Limits.MaxExecutionSteps.Should().Be(DomainEvaluationLimits.Default.MaxExecutionSteps);
    }
}
