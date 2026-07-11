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
    public async Task ValidateAsync_WhenNumericRangeIsValid_ReturnsCanonicalParameters()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(
                RuleDefinitionKeys.NumericRange,
                "Decimal",
                Params(("min", ["0.0"]), ("max", ["100000.00"]))));

        result.IsValid.Should().BeTrue();
        result.CanonicalParameters!["min"].Should().Equal("0.0");
    }

    [Fact]
    public async Task ValidateAsync_WhenRuleIsIncompatible_ReturnsStableError()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(
                RuleDefinitionKeys.TextLength,
                "Boolean",
                Params(("min", ["1"]))));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("rule_incompatible");
    }

    [Fact]
    public async Task ValidateAsync_WhenRangeIsInvalid_ReturnsInvalid()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(
                RuleDefinitionKeys.TextLength,
                "Text",
                Params(("min", ["10"]), ("max", ["2"]))));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Text length minimum cannot exceed maximum.");
    }

    [Fact]
    public async Task ValidateAsync_WhenChoiceCountTargetsSingleChoice_ReturnsIncompatible()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(
                RuleDefinitionKeys.ChoiceSelectionCount,
                "Choice",
                Params(("min", ["1"])),
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["selection_mode"] = ["Single"],
                }));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("rule_incompatible");
    }

    [Fact]
    public async Task ValidateAsync_WhenSystemVersionIsUnknown_DoesNotSubstituteLatest()
    {
        RuleApplicationValidationRequest request = Request(
            RuleDefinitionKeys.Required,
            "Text",
            Params());

        RuleApplicationValidationResult result = await _sut.ValidateAsync(request with
        {
            DefinitionVersion = 2,
        });

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("definition_not_found");
    }

    [Fact]
    public async Task ValidateAsync_WhenNormalizedParameterKeysCollide_ReturnsInvalid()
    {
        RuleApplicationValidationResult result = await _sut.ValidateAsync(
            Request(
                RuleDefinitionKeys.NumericRange,
                "Decimal",
                Params(("min", ["0"]), (" min ", ["1"]))));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("parameter_invalid");
    }

    private static RuleApplicationValidationRequest Request(
        string definitionKey,
        string targetType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? configuration = null) =>
        new(
            Guid.NewGuid(),
            definitionKey,
            DefinitionVersion: 1,
            new RuleApplicationTarget(
                RuleScope.Field,
                "business_objects.field.text",
                ContextSchemaVersion: 1,
                targetType,
                configuration ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)),
            parameters);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);
}
