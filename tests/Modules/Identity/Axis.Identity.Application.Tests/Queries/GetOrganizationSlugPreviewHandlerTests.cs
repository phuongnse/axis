using Axis.Identity.Application.Queries.GetOrganizationSlugPreview;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetOrganizationSlugPreviewHandlerTests
{
    private readonly IOrganizationSlugGenerator _slugGenerator =
        Substitute.For<IOrganizationSlugGenerator>();

    private GetOrganizationSlugPreviewHandler CreateHandler() => new(_slugGenerator);

    [Fact]
    public async Task Handle_WhenOrganizationNameIsProvided_ReturnsGeneratedBaseSlug()
    {
        _slugGenerator.GenerateBaseSlug("Acme Corp").Returns("acme-corp");

        OrganizationSlugPreviewDto result = await CreateHandler().Handle(
            new GetOrganizationSlugPreviewQuery("Acme Corp"),
            CancellationToken.None);

        result.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_WhenSlugIsEmpty_FallsBackToOrganization()
    {
        _slugGenerator.GenerateBaseSlug(Arg.Any<string>()).Returns(string.Empty);

        OrganizationSlugPreviewDto result = await CreateHandler().Handle(
            new GetOrganizationSlugPreviewQuery("!!!"),
            CancellationToken.None);

        result.Slug.Should().Be("organization");
    }
}
