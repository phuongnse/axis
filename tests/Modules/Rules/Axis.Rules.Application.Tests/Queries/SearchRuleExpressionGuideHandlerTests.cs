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
    public void Validate_WhenParametersAreNull_ReturnsFailure()
    {
        SearchRuleExpressionGuideQueryValidator validator = new();
        SearchRuleExpressionGuideQuery query = new(new(
            ExpressionLanguageVersion: 1,
            DefinitionKey: null,
            ContextKey: null,
            ContextSchemaVersion: null,
            Parameters: null!,
            Query: null,
            Language: "en"));

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Search_WhenContextAndParametersAreProvided_BuildsLocalizedGuideFromMetadata()
    {
        RuleExpressionGuideService service = new(_context.ContextRegistry, _context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(
                ExpressionLanguageVersion: 1,
                DefinitionKey: null,
                RuleDefinitionHandlerTestContext.Schema.ContextKey,
                RuleDefinitionHandlerTestContext.Schema.Version,
                [new("threshold", RuleValueType.Decimal, true, false, [])],
                Query: null,
                Language: "vi")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sections.Should().Contain(section =>
            section.Key == "context" &&
            section.Items.Any(item =>
                item.ReferenceKind == RuleExpressionReferenceKind.Context &&
                item.ReferenceKey == "field.value"));
        result.Value.Sections.Should().Contain(section =>
            section.Key == "parameters" &&
            section.Items.Any(item =>
                item.DisplayName.Text == "@parameters.threshold" &&
                item.Detail!.Text.Contains("Số thập phân", StringComparison.Ordinal)));
        result.Value.Sections.Should().OnlyContain(section => section.Items.Count > 0);
        await _context.TextSearch.DidNotReceiveWithAnyArgs()
            .SearchAsync(default!, default!, default);
    }

    [Fact]
    public async Task Search_WhenQueryHasTypo_UsesProviderResultAndReturnsStructuredHighlight()
    {
        _context.TextSearch.SearchAsync(
                Arg.Any<IReadOnlyList<RuleTextSearchDocument>>(),
                "lenght",
                Arg.Any<CancellationToken>())
            .Returns([new RuleTextSearchMatch("Function:Length", 1)]);
        RuleExpressionGuideService service = new(_context.ContextRegistry, _context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(
                ExpressionLanguageVersion: 1,
                DefinitionKey: "field.required",
                ContextKey: null,
                ContextSchemaVersion: null,
                Parameters: [],
                Query: "lenght",
                Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalResults.Should().Be(1);
        RuleExpressionGuideItemDto item = result.Value.Sections.Should().ContainSingle()
            .Which.Items.Should().ContainSingle().Which;
        item.ReferenceKind.Should().Be(RuleExpressionReferenceKind.Function);
        item.ReferenceKey.Should().Be("Length");
        item.DisplayName.Segments.Should().Contain(segment =>
            segment.IsMatch && segment.Text == "Length");
    }

    [Fact]
    public async Task Search_WhenMatchesSpanSections_OrdersSectionsByBestProviderRank()
    {
        _context.TextSearch.SearchAsync(
                Arg.Any<IReadOnlyList<RuleTextSearchDocument>>(),
                "value length",
                Arg.Any<CancellationToken>())
            .Returns([
                new RuleTextSearchMatch("Function:Length", 2),
                new RuleTextSearchMatch("Context:field.value", 1)
            ]);
        RuleExpressionGuideService service = new(_context.ContextRegistry, _context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(
                ExpressionLanguageVersion: 1,
                DefinitionKey: "field.required",
                ContextKey: null,
                ContextSchemaVersion: null,
                Parameters: [],
                Query: "value length",
                Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sections.Select(section => section.Key)
            .Should().Equal("functions", "context");
    }

    [Fact]
    public async Task Search_WhenBuildingDocuments_ExcludesExamples()
    {
        IReadOnlyList<RuleTextSearchDocument>? captured = null;
        _context.TextSearch.SearchAsync(
                Arg.Do<IReadOnlyList<RuleTextSearchDocument>>(documents => captured = documents),
                "100",
                Arg.Any<CancellationToken>())
            .Returns([new RuleTextSearchMatch("PredicateOperator:GreaterThan", 1)]);
        RuleExpressionGuideService service = new(_context.ContextRegistry, _context.TextSearch);
        SearchRuleExpressionGuideHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionGuideDto> result = await sut.Handle(
            new SearchRuleExpressionGuideQuery(new(
                ExpressionLanguageVersion: 1,
                DefinitionKey: "field.required",
                ContextKey: null,
                ContextSchemaVersion: null,
                Parameters: [],
                Query: "100",
                Language: "en")),
            CancellationToken.None);

        captured.Should().NotBeNull();
        RuleTextSearchDocument greaterThan = captured!.Single(
            document => document.Key == "PredicateOperator:GreaterThan");
        greaterThan.Content.Should().NotContain("Amount is greater than 100");
        result.Value.Sections.SelectMany(section => section.Items).Single()
            .Examples.SelectMany(example => example.Segments)
            .Should().NotContain(segment => segment.IsMatch);
    }
}
