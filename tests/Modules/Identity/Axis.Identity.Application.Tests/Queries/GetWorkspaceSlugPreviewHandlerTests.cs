using Axis.Identity.Application.Queries.GetWorkspaceSlugPreview;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetWorkspaceSlugPreviewHandlerTests
{
    private readonly IWorkspaceSlugGenerator _slugGenerator =
        Substitute.For<IWorkspaceSlugGenerator>();

    private GetWorkspaceSlugPreviewHandler CreateHandler() => new(_slugGenerator);

    [Fact]
    public async Task Handle_WhenWorkspaceNameIsProvided_ReturnsGeneratedBaseSlug()
    {
        _slugGenerator.GenerateBaseSlug("Acme Corp").Returns("acme-corp");

        WorkspaceSlugPreviewDto result = await CreateHandler().Handle(
            new GetWorkspaceSlugPreviewQuery("Acme Corp"),
            CancellationToken.None);

        result.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_WhenSlugIsEmpty_FallsBackToWorkspace()
    {
        _slugGenerator.GenerateBaseSlug(Arg.Any<string>()).Returns(string.Empty);

        WorkspaceSlugPreviewDto result = await CreateHandler().Handle(
            new GetWorkspaceSlugPreviewQuery("!!!"),
            CancellationToken.None);

        result.Slug.Should().Be("workspace");
    }
}
