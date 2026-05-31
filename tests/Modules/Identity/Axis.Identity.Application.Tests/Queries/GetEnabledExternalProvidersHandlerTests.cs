using Axis.Identity.Application.Queries.GetEnabledExternalProviders;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetEnabledExternalProvidersHandlerTests
{
    private readonly IExternalAuthProviderRegistry _registry =
        Substitute.For<IExternalAuthProviderRegistry>();

    private GetEnabledExternalProvidersHandler CreateHandler() => new(_registry);

    [Fact]
    public async Task Handle_ReturnsEnabledProvidersFromRegistry()
    {
        _registry.GetEnabledProviders().Returns(["google", "github"]);

        IReadOnlyList<string> result = await CreateHandler().Handle(
            new GetEnabledExternalProvidersQuery(),
            CancellationToken.None);

        result.Should().BeEquivalentTo("google", "github");
    }

    [Fact]
    public async Task Handle_WhenNoProvidersConfigured_ReturnsEmpty()
    {
        _registry.GetEnabledProviders().Returns([]);

        IReadOnlyList<string> result = await CreateHandler().Handle(
            new GetEnabledExternalProvidersQuery(),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
