using Axis.Rules.Application;
using Axis.Rules.Contracts;
using FluentAssertions;

namespace Axis.Rules.Application.Tests;

public sealed class FieldRuleApplicationValidatorTests
{
    private readonly FieldRuleApplicationValidator _sut = new();

    [Fact]
    public void ValidateFieldRuleApplication_WhenNumericRangeIsValid_ReturnsValid()
    {
        FieldRuleApplicationValidationResult result = _sut.ValidateFieldRuleApplication(
            FieldRuleDefinitionKeys.NumericRange,
            "Decimal",
            Params(("min", ["0"]), ("max", ["100000"])));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFieldRuleApplication_WhenRuleIsIncompatible_ReturnsInvalid()
    {
        FieldRuleApplicationValidationResult result = _sut.ValidateFieldRuleApplication(
            FieldRuleDefinitionKeys.TextLength,
            "Boolean",
            Params(("min", ["1"])));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Field rule is not compatible with the selected field type.");
    }

    [Fact]
    public void ValidateFieldRuleApplication_WhenParametersAreInvalid_ReturnsInvalid()
    {
        FieldRuleApplicationValidationResult result = _sut.ValidateFieldRuleApplication(
            FieldRuleDefinitionKeys.TextLength,
            "Text",
            Params(("min", ["10"]), ("max", ["2"])));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Text length minimum cannot exceed maximum.");
    }

    [Fact]
    public void ValidateFieldRuleApplication_WhenSingleSelectOptionsAreDuplicated_ReturnsInvalid()
    {
        FieldRuleApplicationValidationResult result = _sut.ValidateFieldRuleApplication(
            FieldRuleDefinitionKeys.SingleSelectOptions,
            "SingleSelect",
            Params(("options", ["Draft", "Draft"])));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Single-select option values must be unique.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);
}
