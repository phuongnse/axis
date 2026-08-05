using Axis.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Tests.Services;

public sealed class OpenIddictClientCatalogTests
{
    [Fact]
    public void Load_WhenCatalogIsValid_ReturnsTypedNativeAndConfidentialClients()
    {
        OpenIddictClientCatalog catalog = OpenIddictClientCatalog.Load(
            BuildConfiguration(ValidCatalog()));

        catalog.SchemaVersion.Should().Be(1);
        OpenIddictClientRegistration native = catalog.Clients.Single(
            client => client.ClientId == "axis_mcp");
        native.Profile.Should().Be(OpenIddictClientProfile.NativePublic);
        native.ClientSecret.Should().BeNull();
        OpenIddictClientRegistration bff = catalog.Clients.Single(
            client => client.ClientId == "enterprise_bff");
        bff.Profile.Should().Be(OpenIddictClientProfile.WebBffConfidential);
        bff.ClientSecret.Should().Be(DevSecret);
        bff.PostLogoutRedirectUris.Should().ContainSingle();
    }

    [Fact]
    public void Load_WhenCatalogIsMissing_FailsClosed()
    {
        Action act = () => OpenIddictClientCatalog.Load(BuildConfiguration([]));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OpenIddict:ClientCatalog*");
    }

    [Fact]
    public void Load_WhenSchemaVersionIsUnsupported_FailsClosed()
    {
        Dictionary<string, string?> values = ValidCatalog();
        values["OpenIddict:ClientCatalog:SchemaVersion"] = "2";

        Action act = () => OpenIddictClientCatalog.Load(BuildConfiguration(values));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SchemaVersion must be 1*");
    }

    [Fact]
    public void Load_WhenExplicitCatalogIsEmpty_ReturnsNoClients()
    {
        OpenIddictClientCatalog catalog = OpenIddictClientCatalog.Load(BuildConfiguration(
            new Dictionary<string, string?> { ["OpenIddict:ClientCatalog:SchemaVersion"] = "1" }));

        catalog.Clients.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenClientIdIsDuplicated_FailsClosed()
    {
        Dictionary<string, string?> values = ValidCatalog();
        values["OpenIddict:ClientCatalog:Clients:1:ClientId"] = "axis_mcp";

        Action act = () => OpenIddictClientCatalog.Load(BuildConfiguration(values));

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate client ID*");
    }

    [Fact]
    public void Load_WhenNativeClientUsesSecretOrNonLoopbackRedirect_FailsClosed()
    {
        Dictionary<string, string?> withSecret = ValidCatalog();
        withSecret["OpenIddict:ClientCatalog:Clients:0:ClientSecret"] = DevSecret;
        Dictionary<string, string?> withRemoteRedirect = ValidCatalog();
        withRemoteRedirect["OpenIddict:ClientCatalog:Clients:0:RedirectUris:0"] =
            "https://native.example/callback";

        Action secretAct = () => OpenIddictClientCatalog.Load(BuildConfiguration(withSecret));
        Action redirectAct = () => OpenIddictClientCatalog.Load(BuildConfiguration(withRemoteRedirect));

        secretAct.Should().Throw<InvalidOperationException>().WithMessage("*not allowed*");
        redirectAct.Should().Throw<InvalidOperationException>().WithMessage("*loopback*");
    }

    [Theory]
    [InlineData("OpenIddict:ClientCatalog:Clients:1:ClientSecret", null, "*ClientSecret*required*")]
    [InlineData("OpenIddict:ClientCatalog:Clients:1:ClientSecret", "too-short", "*at least 32*")]
    [InlineData("OpenIddict:ClientCatalog:Clients:1:PostLogoutRedirectUris:0", null, "*PostLogoutRedirectUris*")]
    [InlineData("OpenIddict:ClientCatalog:Clients:1:RedirectUris:0", "http://enterprise.example/signin-oidc", "*must use HTTPS*")]
    public void Load_WhenConfidentialClientBoundaryIsInvalid_FailsClosed(
        string key,
        string? value,
        string expectedMessage)
    {
        Dictionary<string, string?> values = ValidCatalog();
        if (value is null)
            values.Remove(key);
        else
            values[key] = value;

        Action act = () => OpenIddictClientCatalog.Load(BuildConfiguration(values));

        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Fact]
    public void Load_WhenRedirectUsesWildcard_FailsClosed()
    {
        Dictionary<string, string?> values = ValidCatalog();
        values["OpenIddict:ClientCatalog:Clients:1:RedirectUris:0"] =
            "https://*.example/signin-oidc";

        Action act = () => OpenIddictClientCatalog.Load(BuildConfiguration(values));

        act.Should().Throw<InvalidOperationException>().WithMessage("*without wildcard*");
    }

    private const string DevSecret = "enterprise-bff-development-secret-0001";

    private static IConfiguration BuildConfiguration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> ValidCatalog() => new(StringComparer.Ordinal)
    {
        ["OpenIddict:ClientCatalog:SchemaVersion"] = "1",
        ["OpenIddict:ClientCatalog:Clients:0:ClientId"] = "axis_mcp",
        ["OpenIddict:ClientCatalog:Clients:0:DisplayName"] = "Axis MCP local client",
        ["OpenIddict:ClientCatalog:Clients:0:Profile"] = "NativePublic",
        ["OpenIddict:ClientCatalog:Clients:0:RedirectUris:0"] =
            "http://127.0.0.1:48123/callback",
        ["OpenIddict:ClientCatalog:Clients:1:ClientId"] = "enterprise_bff",
        ["OpenIddict:ClientCatalog:Clients:1:DisplayName"] = "Enterprise BFF",
        ["OpenIddict:ClientCatalog:Clients:1:Profile"] = "WebBffConfidential",
        ["OpenIddict:ClientCatalog:Clients:1:ClientSecret"] = DevSecret,
        ["OpenIddict:ClientCatalog:Clients:1:RedirectUris:0"] =
            "https://enterprise.example/signin-oidc",
        ["OpenIddict:ClientCatalog:Clients:1:PostLogoutRedirectUris:0"] =
            "https://enterprise.example/signout-callback-oidc",
    };
}
