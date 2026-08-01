using Axis.Rules.Application.Queries.SearchRuleExpressionGuide;
using Axis.Rules.Application.Search;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class SearchRuleExpressionGuideHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public void Validate_WhenInputsAreNull_ReturnsFailure()
    {
        SearchRuleExpressionGuideQueryValidator validator = new();
        FluentValidation.Results.ValidationResult result = validator.Validate(
            new SearchRuleExpressionGuideQuery(new(1, null, null!, null, "en")));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Search_WhenInputsAreProvided_BuildsInputGuide()
    {
        RuleExpressionGuideService service = new(_context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(
                1,
                null,
                [new("threshold", "Threshold", [RuleValueType.Decimal], true, false, [])],
                null,
                "vi")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sections.Should().Contain(section =>
            section.Key == "inputs" && section.Items.Any(item =>
                item.ReferenceKind == RuleExpressionReferenceKind.Input &&
                item.DisplayName.Text == "Threshold"));
    }

    [Fact]
    public async Task Search_WhenQueryMatchesFunction_UsesProviderRanking()
    {
        _context.TextSearch.SearchAsync(
                Arg.Any<IReadOnlyList<RuleTextSearchDocument>>(),
                "lenght",
                Arg.Any<CancellationToken>())
            .Returns([new RuleTextSearchMatch("Function:Length", 1)]);
        RuleExpressionGuideService service = new(_context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(1, null, [], "lenght", "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalResults.Should().Be(1);
        result.Value.Sections.Single().Items.Single().ReferenceKey.Should().Be("Length");
    }
}
