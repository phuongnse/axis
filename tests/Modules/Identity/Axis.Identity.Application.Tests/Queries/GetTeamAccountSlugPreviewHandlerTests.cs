using Axis.Identity.Application.Queries.GetTeamAccountSlugPreview;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetTeamAccountSlugPreviewHandlerTests
{
    private readonly ITeamAccountSlugGenerator _slugGenerator =
        Substitute.For<ITeamAccountSlugGenerator>();

    private GetTeamAccountSlugPreviewHandler CreateHandler() => new(_slugGenerator);

    [Fact]
    public async Task Handle_WhenTeamAccountNameIsProvided_ReturnsGeneratedBaseSlug()
    {
        _slugGenerator.GenerateBaseSlug("Acme Corp").Returns("acme-corp");

        TeamAccountSlugPreviewDto result = await CreateHandler().Handle(
            new GetTeamAccountSlugPreviewQuery("Acme Corp"),
            CancellationToken.None);

        result.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_WhenSlugIsEmpty_FallsBackToTeamAccountSlug()
    {
        _slugGenerator.GenerateBaseSlug(Arg.Any<string>()).Returns(string.Empty);

        TeamAccountSlugPreviewDto result = await CreateHandler().Handle(
            new GetTeamAccountSlugPreviewQuery("!!!"),
            CancellationToken.None);

        result.Slug.Should().Be("team-account");
    }
}
