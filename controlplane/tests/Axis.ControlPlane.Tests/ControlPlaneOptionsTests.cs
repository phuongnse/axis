using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Axis.ControlPlane.Tests;

public sealed class ControlPlaneOptionsTests
{
    [Theory]
    [InlineData("ControlPlane:Region")]
    [InlineData("ControlPlane:TenantDomain")]
    [InlineData("ControlPlane:ConnectionString")]
    [InlineData("ControlPlane:MailFrom")]
    [InlineData("ControlPlane:Mail:Host")]
    public void A_missing_setting_stops_the_start_and_the_message_names_it(string setting)
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { [setting] = string.Empty });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_with_capital_letters_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Region"] = "VN" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Region", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tenant_domain_that_is_not_a_domain_name_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:TenantDomain"] = "not a domain" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:TenantDomain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_missing_setting_is_named_at_once()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:Region"] = string.Empty,
            ["ControlPlane:TenantDomain"] = string.Empty,
            ["ControlPlane:ConnectionString"] = string.Empty,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ControlPlane:TenantDomain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_Development_a_connection_without_VerifyFull_stops_the_start_and_the_message_holds_no_password()
    {
        using var factory = ControlPlaneFactory.Create(environment: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VerifyFull", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_that_does_not_parse_stops_the_start_and_the_message_holds_no_value()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:ConnectionString"] = "Password=secret-password;NoSuchKey=1",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_sender_that_is_not_an_address_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:MailFrom"] = "not an address" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:MailFrom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_port_out_of_range_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Mail:Port"] = "70000" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:Port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_Development_a_mail_relay_without_TLS_stops_the_start()
    {
        using var factory = ControlPlaneFactory.Create(
            environment: "Production",
            settings: new Dictionary<string, string?>
            {
                ["ControlPlane:ConnectionString"] = ControlPlaneFactory.UnreachableConnectionString + ";SSL Mode=VerifyFull",
                ["ControlPlane:Mail:UseTls"] = "false",
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:UseTls", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mail_user_name_without_a_password_stops_the_start_and_the_message_holds_no_value()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?> { ["ControlPlane:Mail:UserName"] = "relay-user" });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Mail:Password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("relay-user", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_internal_url_of_a_cell_stops_the_start_and_the_message_names_the_setting()
    {
        using var files = new CellFiles();
        using var factory = ControlPlaneFactory.Create(settings: files.Settings(internalUrl: "http://cell.internal:8443"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Cells:0:InternalUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cell.internal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_token_url_of_a_cell_stops_the_start_and_the_message_names_the_setting()
    {
        using var files = new CellFiles();
        using var factory = ControlPlaneFactory.Create(settings: files.Settings(tokenUrl: "http://keycloak.internal/token"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Cells:0:TokenUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_cell_without_its_secret_stops_the_start_and_the_message_names_the_setting_and_no_value()
    {
        using var files = new CellFiles();
        var settings = files.Settings();
        settings.Remove("ControlPlane:CellSecrets:vn-1");
        settings["ControlPlane:CellSecrets:other"] = "secret-of-another-cell";
        using var factory = ControlPlaneFactory.Create(settings: settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:CellSecrets:vn-1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-of-another-cell", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_cell_without_the_client_certificate_stops_the_start_and_the_message_names_the_settings()
    {
        using var files = new CellFiles();
        var settings = files.Settings();
        settings.Remove("ControlPlane:CellClientCertificate:Path");
        settings.Remove("ControlPlane:CellClientCertificate:KeyPath");
        using var factory = ControlPlaneFactory.Create(settings: settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:CellClientCertificate:Path", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ControlPlane:CellClientCertificate:KeyPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_certificate_authority_file_that_does_not_exist_stops_the_start_and_the_message_holds_no_path()
    {
        using var files = new CellFiles();
        var settings = files.Settings();
        settings["ControlPlane:CellCertificateAuthorityPath"] = "/no/such/ca-file.pem";
        using var factory = ControlPlaneFactory.Create(settings: settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:CellCertificateAuthorityPath", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ca-file.pem", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("VN-1")]
    [InlineData("vn_1")]
    [InlineData("-vn")]
    public void A_cell_id_that_is_not_lowercase_words_joined_by_hyphens_stops_the_start(string id)
    {
        using var files = new CellFiles();
        using var factory = ControlPlaneFactory.Create(settings: files.Settings(id: id));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Cells:0:Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_cells_with_one_id_stop_the_start()
    {
        using var files = new CellFiles();
        var settings = files.Settings();
        settings["ControlPlane:Cells:1:Id"] = "vn-1";
        settings["ControlPlane:Cells:1:InternalUrl"] = "https://cell-b.internal:8443";
        settings["ControlPlane:Cells:1:TokenUrl"] = "https://keycloak-b.internal/token";
        settings["ControlPlane:Cells:1:ClientId"] = "axis-controlplane";
        using var factory = ControlPlaneFactory.Create(settings: settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ControlPlane:Cells:1:Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_cells_the_service_starts_as_before()
    {
        using var factory = ControlPlaneFactory.Create();

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void A_listed_cell_with_all_its_settings_loads()
    {
        using var files = new CellFiles();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(files.Settings()).Build();

        var options = ControlPlaneOptions.Load(configuration, new TestEnvironment());

        var cell = Assert.Single(options.Cells);
        Assert.Equal("vn-1", cell.Id);
        Assert.Equal("https://cell.internal:8443", cell.InternalUrl);
        Assert.Equal("cell-secret", options.CellSecrets["vn-1"]);
    }

    /// <summary>Empty files that exist, so a test of the settings needs no certificate.</summary>
    private sealed class CellFiles : IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("axis-cells").FullName;

        public Dictionary<string, string?> Settings(string id = "vn-1", string internalUrl = "https://cell.internal:8443", string tokenUrl = "https://keycloak.internal/realms/operator/protocol/openid-connect/token")
        {
            var certificate = Path.Combine(_folder, "client.crt");
            var key = Path.Combine(_folder, "client.key");
            File.WriteAllText(certificate, string.Empty);
            File.WriteAllText(key, string.Empty);
            return new Dictionary<string, string?>
            {
                ["ControlPlane:Region"] = "vn",
                ["ControlPlane:TenantDomain"] = "vn.example.com",
                ["ControlPlane:ConnectionString"] = ControlPlaneFactory.UnreachableConnectionString,
                ["ControlPlane:MailFrom"] = "no-reply@vn.example.com",
                ["ControlPlane:Mail:Host"] = "127.0.0.1",
                ["ControlPlane:Cells:0:Id"] = id,
                ["ControlPlane:Cells:0:InternalUrl"] = internalUrl,
                ["ControlPlane:Cells:0:TokenUrl"] = tokenUrl,
                ["ControlPlane:Cells:0:ClientId"] = "axis-controlplane",
                [$"ControlPlane:CellSecrets:{id}"] = "cell-secret",
                ["ControlPlane:CellClientCertificate:Path"] = certificate,
                ["ControlPlane:CellClientCertificate:KeyPath"] = key,
            };
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Axis.ControlPlane";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
