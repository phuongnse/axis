using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests;

public sealed class RuleApplicationValidatorTests
{
    private readonly RuleApplicationValidator _sut =
        new(Substitute.For<IRuleDefinitionRepository>());

    [Fact]
    public async Task ValidateAsync_WhenInputsAreValid_ReturnsCanonicalInputs()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(RuleDefinitionKeys.NumericRange, Inputs(("value", ["12.0"]), ("min", ["0.0"]))),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.CanonicalInputs!["value"].Should().Equal("12.0");
        result.CanonicalInputs["min"].Should().Equal("0.0");
    }

    [Fact]
    public async Task ValidateAsync_WhenRequiredRuleValueIsAbsent_ReturnsValidCanonicalInput()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(RuleDefinitionKeys.Required, Inputs()),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.CanonicalInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WhenUnknownInputIsProvided_ReturnsInvalid()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(RuleDefinitionKeys.NumericRange, Inputs(("value", ["12"]), ("unknown", ["1"]))),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("input_invalid");
    }

    [Fact]
    public async Task ValidateAsync_WhenSystemVersionIsUnknown_DoesNotSubstituteLatest()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(RuleDefinitionKeys.Required, Inputs(("value", ["x"]))) with { DefinitionVersion = 2 },
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("definition_not_found");
    }

    private static RuleApplicationValidationRequest Request(
        string definitionKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>> inputs) =>
        new(Guid.NewGuid(), definitionKey, 1, inputs);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs(
        params (string Key, string[] Values)[] inputs) =>
        inputs.ToDictionary(
            input => input.Key,
            input => (IReadOnlyList<string>)input.Values,
            StringComparer.Ordinal);
}
