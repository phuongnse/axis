using System.Text.Json;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public sealed class OpenIddictSeederTests(IdentityDatabaseFixture database) : IAsyncLifetime
{
    private const string DevSecret = "enterprise-bff-development-secret-0001";

    public async ValueTask InitializeAsync() => await DeleteAllApplicationsAsync();
    public async ValueTask DisposeAsync() => await DeleteAllApplicationsAsync();

    [Fact]
    public async Task StartAsync_WhenCatalogContainsBothProfiles_CreatesExactDescriptors()
    {
        await using ServiceProvider provider = BuildProvider(Catalog(includeNative: true, includeBff: true));

        await Seeder(provider).StartAsync(TestContext.Current.CancellationToken);

        IOpenIddictApplicationManager manager = provider.GetRequiredService<IOpenIddictApplicationManager>();
        object native = (await manager.FindByClientIdAsync(
            "axis_mcp",
            TestContext.Current.CancellationToken))!;
        (await manager.GetClientTypeAsync(native, TestContext.Current.CancellationToken))
            .Should().Be(ClientTypes.Public);
        (await manager.GetRequirementsAsync(native, TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo([Requirements.Features.ProofKeyForCodeExchange]);

        object bff = (await manager.FindByClientIdAsync(
            "enterprise_bff",
            TestContext.Current.CancellationToken))!;
        (await manager.GetClientTypeAsync(bff, TestContext.Current.CancellationToken))
            .Should().Be(ClientTypes.Confidential);
        (await manager.ValidateClientSecretAsync(
            bff,
            DevSecret,
            TestContext.Current.CancellationToken)).Should().BeTrue();
        (await manager.GetPostLogoutRedirectUrisAsync(bff, TestContext.Current.CancellationToken))
            .Should().Equal("https://enterprise.example/signout-callback-oidc");
        (await manager.GetPermissionsAsync(bff, TestContext.Current.CancellationToken)).Should().Contain(
            Permissions.Endpoints.PushedAuthorization,
            Permissions.Endpoints.Revocation,
            Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.RefreshToken,
            Permissions.Prefixes.Scope + Scopes.OfflineAccess);
        (await manager.GetRequirementsAsync(bff, TestContext.Current.CancellationToken)).Should().Contain(
            Requirements.Features.ProofKeyForCodeExchange,
            Requirements.Features.PushedAuthorizationRequests);
        IReadOnlyDictionary<string, JsonElement> properties =
            await manager.GetPropertiesAsync(bff, TestContext.Current.CancellationToken);
        properties[OpenIddictSeeder.ManagedProfileProperty].GetString()
            .Should().Be(nameof(OpenIddictClientProfile.WebBffConfidential));
    }

    [Fact]
    public async Task StartAsync_WhenUnmanagedClientCollides_FailsBeforeAnyCatalogMutation()
    {
        await using ServiceProvider provider = BuildProvider(Catalog(includeNative: true, includeBff: true));
        IOpenIddictApplicationManager manager = provider.GetRequiredService<IOpenIddictApplicationManager>();
        await manager.CreateAsync(PublicDescriptor(
            "enterprise_bff",
            "External owner",
            "http://127.0.0.1:9000/callback"), TestContext.Current.CancellationToken);

        Func<Task> act = () => Seeder(provider).StartAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*enterprise_bff*not owned*");
        (await manager.FindByClientIdAsync(
            "axis_mcp",
            TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WhenManagedClientIsOmitted_DeletesOnlyCatalogOwnedClient()
    {
        await using (ServiceProvider initial = BuildProvider(Catalog(includeNative: true, includeBff: false)))
        {
            IOpenIddictApplicationManager manager = initial.GetRequiredService<IOpenIddictApplicationManager>();
            await manager.CreateAsync(PublicDescriptor(
                "external_owner",
                "External",
                "http://127.0.0.1:9000/callback"), TestContext.Current.CancellationToken);
            await Seeder(initial).StartAsync(TestContext.Current.CancellationToken);
        }

        await using ServiceProvider empty = BuildProvider(Catalog(includeNative: false, includeBff: false));
        await Seeder(empty).StartAsync(TestContext.Current.CancellationToken);
        IOpenIddictApplicationManager persisted = empty.GetRequiredService<IOpenIddictApplicationManager>();

        (await persisted.FindByClientIdAsync(
            "axis_mcp",
            TestContext.Current.CancellationToken)).Should().BeNull();
        (await persisted.FindByClientIdAsync(
            "external_owner",
            TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    private ServiceProvider BuildProvider(IEnumerable<KeyValuePair<string, string?>> catalog)
    {
        Dictionary<string, string?> values = new(catalog, StringComparer.Ordinal)
        {
            ["ConnectionStrings:Identity"] = database.ConnectionString,
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new();
        services.AddIdentityInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static OpenIddictSeeder Seeder(IServiceProvider provider) =>
        (OpenIddictSeeder)provider.GetServices<IHostedService>()
            .Single(service => service is OpenIddictSeeder);

    private async Task DeleteAllApplicationsAsync()
    {
        await using ServiceProvider provider = BuildProvider(Catalog(false, false));
        IOpenIddictApplicationManager manager = provider.GetRequiredService<IOpenIddictApplicationManager>();
        List<object> applications = [];
        await foreach (object application in manager.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken))
        {
            applications.Add(application);
        }

        foreach (object application in applications)
            await manager.DeleteAsync(application, TestContext.Current.CancellationToken);
    }

    private static OpenIddictApplicationDescriptor PublicDescriptor(
        string clientId,
        string displayName,
        string redirectUri)
    {
        OpenIddictApplicationDescriptor descriptor = new()
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            DisplayName = displayName,
        };
        descriptor.RedirectUris.Add(new Uri(redirectUri));
        return descriptor;
    }

    private static Dictionary<string, string?> Catalog(bool includeNative, bool includeBff)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            ["OpenIddict:ClientCatalog:SchemaVersion"] = "1",
        };
        int index = 0;
        if (includeNative)
        {
            string path = $"OpenIddict:ClientCatalog:Clients:{index++}";
            values[$"{path}:ClientId"] = "axis_mcp";
            values[$"{path}:DisplayName"] = "Axis MCP local client";
            values[$"{path}:Profile"] = "NativePublic";
            values[$"{path}:RedirectUris:0"] = "http://127.0.0.1:48123/callback";
        }

        if (includeBff)
        {
            string path = $"OpenIddict:ClientCatalog:Clients:{index}";
            values[$"{path}:ClientId"] = "enterprise_bff";
            values[$"{path}:DisplayName"] = "Enterprise BFF";
            values[$"{path}:Profile"] = "WebBffConfidential";
            values[$"{path}:ClientSecret"] = DevSecret;
            values[$"{path}:RedirectUris:0"] = "https://enterprise.example/signin-oidc";
            values[$"{path}:PostLogoutRedirectUris:0"] =
                "https://enterprise.example/signout-callback-oidc";
        }

        return values;
    }
}
