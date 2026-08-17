using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Shared.Application;
using FluentAssertions;
using FluentValidation.Results;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ListRuleDefinitionsQueryValidatorTests
{
    private readonly ListRuleDefinitionsQueryValidator _sut = new();

    [Fact]
    public void Validate_WhenSortParametersAreOmitted_IsValid()
    {
        ValidationResult result = _sut.Validate(new ListRuleDefinitionsQuery(Page: 1, PageSize: 20));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_WhenOnlyOneSortParameterIsProvided_IsInvalid(bool provideField)
    {
        ListRuleDefinitionsQuery query = provideField
            ? new ListRuleDefinitionsQuery(
                Page: 1,
                PageSize: 20,
                SortBy: RuleDefinitionSortField.Name)
            : new ListRuleDefinitionsQuery(
                Page: 1,
                PageSize: 20,
                SortDirection: CollectionSortDirection.Ascending);

        ValidationResult result = _sut.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().OnlyContain(error => error.ErrorCode == RulesProblemCodes.DefinitionInvalid);
    }

    [Fact]
    public void Validate_WhenSortEnumsAreUndefined_IsInvalid()
    {
        ValidationResult result = _sut.Validate(new ListRuleDefinitionsQuery(
            Page: 1,
            PageSize: 20,
            SortBy: (RuleDefinitionSortField)999,
            SortDirection: (CollectionSortDirection)999));

        result.Errors.Select(error => error.PropertyName).Should().Contain([
            nameof(ListRuleDefinitionsQuery.SortBy),
            nameof(ListRuleDefinitionsQuery.SortDirection),
        ]);
    }
}
