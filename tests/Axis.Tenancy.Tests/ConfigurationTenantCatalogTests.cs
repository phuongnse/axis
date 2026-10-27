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
            "Tenant 'default' binding 'Token' has kind 'password': use 'url', 'secret' or 'storage'."
        },
        {
            "binding without a kind",
            [.. Valid(), "Tenants:default:Bindings:Token:Value=s3cr3t"],
            "Tenant 'default' binding 'Token' has no kind: use 'url', 'secret' or 'storage'."
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
        {
            "PreviewOf of an unknown tenant",
            [.. Valid(), "Tenants:default:PreviewOf=missing"],
            "Tenant 'default' names 'missing' in PreviewOf, which is not a configured tenant."
        },
        {
            "PreviewOf of a preview tenant",
            [.. PreviewPair(), "Tenants:third:Hosts:0=third.example.test", $"Tenants:third:ConnectionString={Connection}", "Tenants:third:PreviewOf=main-preview"],
            "Tenant 'third' names 'main-preview' in PreviewOf, which is itself a preview tenant."
        },
        {
            "two preview tenants of one main tenant",
            [.. PreviewPair(), "Tenants:main-preview-2:Hosts:0=preview2.example.test", $"Tenants:main-preview-2:ConnectionString={Connection}", "Tenants:main-preview-2:PreviewOf=main"],
            "Tenants 'main-preview' and 'main-preview-2' both name 'main' in PreviewOf. A tenant has at most one preview tenant."
        },
        {
            "preview tenant with an Oidc block",
            [.. PreviewPair(), "Tenants:main-preview:Oidc:Authority=https://idp.example.test/realm"],
            "Tenant 'main-preview' is a preview tenant and has its own Oidc block. A preview tenant uses the Oidc settings of its main tenant 'main'."
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
                "Tenants:default:Oidc:ServiceAudience=axis-services",
                "Tenants:default:Oidc:Claims:UserId=preferred_username",
                "Tenants:default:Oidc:Claims:Roles=groups",
                "Tenants:default:Oidc:Claims:Email=mail",
                "Tenants:default:Oidc:Claims:Locale=lang",
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
        Assert.Equal("axis-services", oidc.ServiceAudience);
        Assert.Equal("preferred_username", oidc.UserIdClaim);
        Assert.Null(oidc.DisplayNameClaim);
        Assert.Equal("groups", oidc.RolesClaim);
        Assert.Equal("mail", oidc.EmailClaim);
        Assert.Equal("lang", oidc.LocaleClaim);

        // The url keeps its text: no trailing slash is added.
        Assert.Equal(new TenantBinding("url", "https://purchasing.example.test", null), entry.Bindings["purchasingsystemurl"]);
        var secret = entry.Bindings["TOKEN"];
        Assert.Equal("secret", secret.Kind);
        Assert.Null(secret.Url);
        Assert.Equal(BindingSecret, secret.Secret?.Reveal());
    }

    [Fact]
    public void A_preview_tenant_signs_in_with_the_main_tenants_settings_without_its_audiences_or_bindings()
    {
        var catalog = Load(
            [
                .. PreviewPair(),
                "Tenants:main:Oidc:Authority=https://idp.example.test/realm",
                "Tenants:main:Oidc:ClientId=client",
                $"Tenants:main:Oidc:ClientSecret={ClientSecret}",
                "Tenants:main:Oidc:EndpointAudience=axis-endpoints",
                "Tenants:main:Oidc:ServiceAudience=axis-services",
                "Tenants:main:Oidc:Claims:UserId=preferred_username",
                "Tenants:main:Oidc:Claims:DisplayName=name",
                "Tenants:main:Oidc:Claims:Roles=groups",
                "Tenants:main:Oidc:Claims:Email=mail",
                "Tenants:main:Oidc:Claims:Locale=lang",
                "Tenants:main:Bindings:PurchasingSystemUrl:Kind=url",
                "Tenants:main:Bindings:PurchasingSystemUrl:Value=https://purchasing.example.test",
                "Tenants:main-preview:Bindings:Own:Kind=secret",
                $"Tenants:main-preview:Bindings:Own:Value={BindingSecret}",
            ]);

        var main = catalog.Find("main")!;
        var preview = catalog.Find("main-preview")!;

        Assert.Null(main.PreviewOf);
        Assert.Equal("axis-endpoints", main.Oidc?.EndpointAudience);
        Assert.Equal("axis-services", main.Oidc?.ServiceAudience);
        Assert.Equal("main", preview.PreviewOf);
        var oidc = Assert.IsType<TenantOidc>(preview.Oidc);
        Assert.Equal("https://idp.example.test/realm", oidc.Authority);
        Assert.Equal("client", oidc.ClientId);
        Assert.Equal(ClientSecret, oidc.ClientSecret?.Reveal());
        Assert.Equal("preferred_username", oidc.UserIdClaim);
        Assert.Equal("name", oidc.DisplayNameClaim);
        Assert.Equal("groups", oidc.RolesClaim);
        Assert.Equal("mail", oidc.EmailClaim);
        Assert.Equal("lang", oidc.LocaleClaim);
        Assert.Null(oidc.EndpointAudience);
        Assert.Null(oidc.ServiceAudience);
        Assert.Equal(["Own"], preview.Bindings.Keys);
        Assert.Single(main.Bindings);
    }

    [Fact]
    public void A_preview_tenant_of_a_main_tenant_without_an_Oidc_block_has_none()
    {
        var catalog = Load(PreviewPair());

        Assert.Null(catalog.Find("main-preview")!.Oidc);
    }

    [Fact]
    public void A_blank_PreviewOf_makes_a_main_tenant()
    {
        var entry = Assert.Single(Load([.. Valid(), "Tenants:default:PreviewOf=  "]).Tenants);

        Assert.Null(entry.PreviewOf);
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

    [Fact]
    public void An_Email_block_is_read_into_the_tenant_entry_and_is_absent_when_not_given()
    {
        var catalog = Load(
            [
                .. Valid(),
                "Tenants:default:Email:Smtp:Host=smtp.example.test",
                "Tenants:default:Email:Smtp:Port=2525",
                "Tenants:default:Email:Smtp:UseTls=false",
                "Tenants:default:Email:Smtp:UserName=mailer",
                "Tenants:default:Email:Smtp:Password=smtp-p4ssw0rd",
                "Tenants:default:Email:From=axis@example.test",
                "Tenants:other:Hosts:0=other.example.test",
                $"Tenants:other:ConnectionString={Connection}",
            ]);

        var email = Assert.IsType<EmailSettings>(catalog.Find("default")!.Email);
        Assert.Equal(("smtp.example.test", 2525, false, "mailer", "axis@example.test"), (email.Host, email.Port, email.UseTls, email.UserName, email.From));
        Assert.Equal("smtp-p4ssw0rd", email.Password!.Reveal());
        Assert.DoesNotContain("smtp-p4ssw0rd", email.ToString(), StringComparison.Ordinal);
        Assert.Null(catalog.Find("other")!.Email);
    }

    [Fact]
    public void An_Email_block_defaults_the_port_to_587_and_turns_TLS_on()
    {
        var catalog = Load([.. Valid(), "Tenants:default:Email:Smtp:Host=smtp.example.test", "Tenants:default:Email:From=axis@example.test"]);

        var email = Assert.IsType<EmailSettings>(catalog.Find("default")!.Email);
        Assert.Equal((587, true, null), (email.Port, email.UseTls, email.UserName));
    }

    [Fact]
    public void An_Email_block_with_a_host_and_no_From_stops_the_load_and_names_the_setting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load([.. Valid(), "Tenants:default:Email:Smtp:Host=smtp.example.test"]));

        Assert.Contains("Tenants:default:Email:From", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Smtp:Port", "0", "Tenants:default:Email:Smtp:Port")]
    [InlineData("Smtp:Port", "70000", "Tenants:default:Email:Smtp:Port")]
    [InlineData("Smtp:Port", "smtp-port-value", "Tenants:default:Email:Smtp:Port")]
    [InlineData("Smtp:UseTls", "maybe", "Tenants:default:Email:Smtp:UseTls")]
    public void An_Email_block_with_a_bad_value_stops_the_load_and_never_quotes_the_value(string key, string value, string setting)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load([.. Valid(), $"Tenants:default:Email:{key}={value}"]));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message.Replace(setting, "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static string[] PreviewPair() =>
    [
        "Tenants:main:Hosts:0=main.example.test",
        $"Tenants:main:ConnectionString={Connection}",
        "Tenants:main-preview:Hosts:0=preview.example.test",
        $"Tenants:main-preview:ConnectionString={Connection}",
        "Tenants:main-preview:PreviewOf=main",
    ];

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
