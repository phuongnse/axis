using Axis.Identity.Application.Queries.GetTenantSlugPreview;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetTenantSlugPreviewHandlerTests
{
    private readonly ITenantSlugGenerator _slugGenerator =
        Substitute.For<ITenantSlugGenerator>();

    private GetTenantSlugPreviewHandler CreateHandler() => new(_slugGenerator);

    [Fact]
    public async Task Handle_WhenTenantNameIsProvided_ReturnsGeneratedBaseSlug()
    {
        _slugGenerator.GenerateBaseSlug("Acme Corp").Returns("acme-corp");

        TenantSlugPreviewDto result = await CreateHandler().Handle(
            new GetTenantSlugPreviewQuery("Acme Corp"),
            CancellationToken.None);

        result.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_WhenSlugIsEmpty_FallsBackToTenant()
    {
        _slugGenerator.GenerateBaseSlug(Arg.Any<string>()).Returns(string.Empty);

        TenantSlugPreviewDto result = await CreateHandler().Handle(
            new GetTenantSlugPreviewQuery("!!!"),
            CancellationToken.None);

        result.Slug.Should().Be("tenant");
    }
}
