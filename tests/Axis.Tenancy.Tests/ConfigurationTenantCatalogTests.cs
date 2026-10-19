using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy.Tests;

public sealed class ConfigurationTenantCatalogTests
{
    private const string Connection = "Host=db.example.test;Database=axis;Username=axis;Password=db-p4ssw0rd";
    private const string SchemaConnection = "Host=db.example.test;Database=axis;Username=axis_schema;Password=schema-p4ssw0rd";
    private const string ClientSecret = "oidc-cl13nt-s3cret";
    private const string BindingSecret = "binding-s3cr3t-value";

    public static TheoryData<string, string[], string> InvalidSections() => new()
    {
        { "no tenants", [], "No tenants are configured. Add at least one tenant under 'Tenants'." },
        {
            "bad id",
            ["Tenants:Default:Hosts:0=localhost", $"Tenants:Default:ConnectionString={Connection}"],
            "Tenant id 'Default' is invalid: use only lowercase letters, digits and hyphens."
        },
        { "no hosts", [$"Tenants:default:ConnectionString={Connection}"], "Tenant 'default' has no hosts." },
        {
            "blank host",
            ["Tenants:default:Hosts:0=localhost", "Tenants:default:Hosts:1=   ", $"Tenants:default:ConnectionString={Connection}"],
            "Tenant 'default' has a blank host."
        },
        { "no connection string", ["Tenants:default:Hosts:0=localhost"], "Tenant 'default' has no connection string." },
        {
            "shared host",
            [
                "Tenants:tenant-a:Hosts:0=shared.example.test",
                $"Tenants:tenant-a:ConnectionString={Connection}",
                "Tenants:tenant-b:Hosts:0=Shared.Example.TEST",
                $"Tenants:tenant-b:ConnectionString={Connection}",
            ],
            "Host 'Shared.Example.TEST' is configured for both tenant 'tenant-a' and tenant 'tenant-b'."
        },
        {
            "binding with a bad kind",
            [.. Valid(), "Tenants:default:Bindings:Token:Kind=password", "Tenants:default:Bindings:Token:Value=s3cr3t"],
            "Tenant 'default' binding 'Token' has kind 'password': use 'url' or 'secret'."
        },
        {
            "binding without a kind",
            [.. Valid(), "Tenants:default:Bindings:Token:Value=s3cr3t"],
            "Tenant 'default' binding 'Token' has no kind: use 'url' or 'secret'."
        },
        {
            "binding without a value",
            [.. Valid(), "Tenants:default:Bindings:Token:Kind=secret"],
            "Tenant 'default' binding 'Token' has no value."
        },
        {
            "url binding that is not absolute",
            [.. Valid(), "Tenants:default:Bindings:Url:Kind=url", "Tenants:default:Bindings:Url:Value=purchasing.example.test"],
            "Tenant 'default' binding 'Url' is not an absolute http or https URL."
        },
    };

    [Theory]
    [MemberData(nameof(InvalidSections))]
    public void An_invalid_section_is_refused_with_the_problem_text(string caseName, string[] settings, string problem)
    {
        Assert.NotNull(caseName);

        var exception = Assert.Throws<InvalidOperationException>(() => Load(settings));

        Assert.Equal("The tenant configuration is invalid. " + problem, exception.Message);
    }

    [Fact]
    public void Every_problem_is_joined_by_a_space()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load("Tenants:Bad:Hosts:0=localhost"));

        Assert.Equal(
            "The tenant configuration is invalid. "
            + "Tenant id 'Bad' is invalid: use only lowercase letters, digits and hyphens. "
            + "Tenant 'Bad' has no connection string.",
            exception.Message);
    }

    [Fact]
    public void A_catalog_lists_its_tenants_in_ordinal_order_and_finds_them_by_id_and_host()
    {
        var catalog = Load(
            "Tenants:b:Hosts:0=b.example.test", $"Tenants:b:ConnectionString={Connection}",
            "Tenants:a:Hosts:0=a.example.test", "Tenants:a:Hosts:1=Alias.Example.Test", $"Tenants:a:ConnectionString={Connection}");

        Assert.Equal(["a", "b"], catalog.Tenants.Select(tenant => tenant.Id));
        Assert.Equal("a", catalog.Find("a")?.Id);
        Assert.Null(catalog.Find("A"));
        Assert.Equal("a", catalog.FindByHost("alias.example.test")?.Id);
        Assert.Equal("b", catalog.FindByHost("B.EXAMPLE.TEST")?.Id);
        Assert.Null(catalog.FindByHost("unknown.example.test"));
        Assert.Equal(["a.example.test", "Alias.Example.Test"], catalog.Find("a")!.Hosts);
    }

    [Fact]
    public void A_tenant_without_schema_connection_or_oidc_block_has_neither()
    {
        var entry = Assert.Single(Load(Valid()).Tenants);

        Assert.Null(entry.SchemaConnectionString);
        Assert.Null(entry.Oidc);
        Assert.Equal(Connection, entry.ConnectionString.Reveal());
    }

    [Fact]
    public void A_blank_schema_connection_or_client_secret_counts_as_missing()
    {
        var entry = Assert.Single(Load(
            [.. Valid(), "Tenants:default:SchemaConnectionString=  ", "Tenants:default:Oidc:ClientId=client", "Tenants:default:Oidc:ClientSecret= "]).Tenants);

        Assert.Null(entry.SchemaConnectionString);
        Assert.Equal("client", entry.Oidc?.ClientId);
        Assert.Null(entry.Oidc?.ClientSecret);
    }

    [Fact]
    public void Oidc_settings_and_bindings_are_read_from_the_section()
    {
        var entry = Assert.Single(Load(
            [
                .. Valid(),
                "Tenants:default:Oidc:Authority=https://idp.example.test/realm",
                "Tenants:default:Oidc:ClientId=client",
                $"Tenants:default:Oidc:ClientSecret={ClientSecret}",
                "Tenants:default:Oidc:EndpointAudience=axis-endpoints",
                "Tenants:default:Oidc:Claims:UserId=preferred_username",
                "Tenants:default:Oidc:Claims:Roles=groups",
                "Tenants:default:Bindings:PurchasingSystemUrl:Kind=url",
                "Tenants:default:Bindings:PurchasingSystemUrl:Value=https://purchasing.example.test",
                "Tenants:default:Bindings:Token:Kind=secret",
                $"Tenants:default:Bindings:Token:Value={BindingSecret}",
            ]).Tenants);

        var oidc = Assert.IsType<TenantOidc>(entry.Oidc);
        Assert.Equal("https://idp.example.test/realm", oidc.Authority);
        Assert.Equal("client", oidc.ClientId);
        Assert.Equal(ClientSecret, oidc.ClientSecret?.Reveal());
        Assert.Equal("axis-endpoints", oidc.EndpointAudience);
        Assert.Equal("preferred_username", oidc.UserIdClaim);
        Assert.Null(oidc.DisplayNameClaim);
        Assert.Equal("groups", oidc.RolesClaim);

        // The url keeps its text: no trailing slash is added.
        Assert.Equal(new TenantBinding("url", "https://purchasing.example.test", null), entry.Bindings["purchasingsystemurl"]);
        var secret = entry.Bindings["TOKEN"];
        Assert.Equal("secret", secret.Kind);
        Assert.Null(secret.Url);
        Assert.Equal(BindingSecret, secret.Secret?.Reveal());
    }

    [Fact]
    public void The_text_of_an_entry_shows_no_secret()
    {
        var entry = Assert.Single(Load(
            [
                .. Valid(),
                $"Tenants:default:SchemaConnectionString={SchemaConnection}",
                "Tenants:default:Oidc:ClientId=client",
                $"Tenants:default:Oidc:ClientSecret={ClientSecret}",
                "Tenants:default:Bindings:Token:Kind=secret",
                $"Tenants:default:Bindings:Token:Value={BindingSecret}",
            ]).Tenants);

        var text = entry.ToString();

        Assert.DoesNotContain(Connection, text, StringComparison.Ordinal);
        Assert.DoesNotContain("db-p4ssw0rd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("schema-p4ssw0rd", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(BindingSecret, text, StringComparison.Ordinal);
        Assert.Contains(SecretValue.Placeholder, text, StringComparison.Ordinal);
        Assert.Equal(Connection, entry.ConnectionString.Reveal());
        Assert.Equal(SchemaConnection, entry.SchemaConnectionString?.Reveal());
        Assert.Equal(ClientSecret, entry.Oidc?.ClientSecret?.Reveal());
        Assert.Equal(BindingSecret, entry.Bindings["Token"].Secret?.Reveal());
    }

    private static string[] Valid() => ["Tenants:default:Hosts:0=localhost", $"Tenants:default:ConnectionString={Connection}"];

    private static ConfigurationTenantCatalog Load(params string[] settings)
    {
        var values = settings.Select(setting => setting.Split('=', 2)).ToDictionary(pair => pair[0], pair => (string?)pair[1]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return ConfigurationTenantCatalog.Load(configuration, new StubEnvironment());
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Axis.Tenancy.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
