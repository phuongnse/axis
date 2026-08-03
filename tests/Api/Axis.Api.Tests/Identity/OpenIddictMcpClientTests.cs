using Axis.Api.Tests.Helpers;
using OpenIddict.Abstractions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class OpenIddictMcpClientTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task McpClient_WhenRegistered_UsesTheFixedLoopbackPkceRedirect()
    {
        using IServiceScope scope = fixture.CreateScope();
        IOpenIddictApplicationManager appManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        object? application = await appManager.FindByClientIdAsync("axis_mcp", cancellationToken);

        Assert.NotNull(application);
        IReadOnlyList<string> redirectUris = await appManager.GetRedirectUrisAsync(application!, cancellationToken);

        Assert.Equal(["http://127.0.0.1:48123/callback"], redirectUris);
    }
}
