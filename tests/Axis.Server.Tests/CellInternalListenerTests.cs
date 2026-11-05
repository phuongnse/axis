using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Axis.Server.CellApi;
using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

/// <summary>
/// With <c>Cell:InternalClientCa</c> set, a cell needs a readable authority file, an https <c>Cell:InternalUrl</c> and an https
/// <c>Internal</c> endpoint, or the start stops with a message that names the setting. Without the setting, or outside a cell,
/// nothing is checked.
/// </summary>
public sealed class CellInternalListenerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("axis-internal-listener-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_host_that_is_not_in_a_cell_has_no_listener_and_reads_nothing()
    {
        var configuration = Settings(("Cell:InternalClientCa", Path.Combine(_folder, "missing.pem")));

        Assert.Null(CellInternalListener.Load(configuration, CellOptions.Load(configuration)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_cell_without_the_authority_has_no_listener(string? authority)
    {
        var configuration = CellSettings(("Cell:InternalClientCa", authority));

        Assert.Null(CellInternalListener.Load(configuration, CellOptions.Load(configuration)));
    }

    [Fact]
    public void A_cell_with_an_authority_an_https_url_and_an_https_endpoint_has_a_listener()
    {
        var configuration = ValidSettings();

        Assert.NotNull(CellInternalListener.Load(configuration, CellOptions.Load(configuration)));
    }

    [Theory]
    [InlineData("https://+:8443")]
    [InlineData("HTTPS://*:8443")]
    public void A_wildcard_https_endpoint_is_accepted(string url)
    {
        var configuration = ValidSettings(("Kestrel:Endpoints:Internal:Url", url));

        Assert.NotNull(CellInternalListener.Load(configuration, CellOptions.Load(configuration)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://axis-internal:8080")]
    [InlineData("not a url")]
    public void A_blank_or_not_https_internal_url_is_a_start_problem_that_names_the_setting(string? url)
    {
        var configuration = ValidSettings(("Cell:InternalUrl", url));

        var message = Refused(configuration);

        Assert.Contains("Cell:InternalUrl", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://+:8080")]
    public void A_missing_or_not_https_endpoint_is_a_start_problem_that_names_the_setting(string? url)
    {
        var configuration = ValidSettings(("Kestrel:Endpoints:Internal:Url", url));

        var message = Refused(configuration);

        Assert.Contains("Kestrel:Endpoints:Internal", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_authority_file_is_a_start_problem_that_names_the_setting_and_not_the_path()
    {
        var path = Path.Combine(_folder, "missing.pem");
        var configuration = ValidSettings(("Cell:InternalClientCa", path));

        var message = Refused(configuration);

        Assert.Contains("Cell:InternalClientCa", message, StringComparison.Ordinal);
        Assert.DoesNotContain(path, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not a certificate")]
    public void An_authority_file_without_a_certificate_is_a_start_problem_that_names_the_setting(string content)
    {
        var path = Path.Combine(_folder, "bad.pem");
        File.WriteAllText(path, content);
        var configuration = ValidSettings(("Cell:InternalClientCa", path));

        var message = Refused(configuration);

        Assert.Contains("Cell:InternalClientCa", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_problem_is_named_in_one_message()
    {
        var configuration = ValidSettings(
            ("Cell:InternalUrl", "http://axis-internal"),
            ("Kestrel:Endpoints:Internal:Url", null),
            ("Cell:InternalClientCa", Path.Combine(_folder, "missing.pem")));

        var message = Refused(configuration);

        Assert.Contains("Cell:InternalUrl", message, StringComparison.Ordinal);
        Assert.Contains("Kestrel:Endpoints:Internal", message, StringComparison.Ordinal);
        Assert.Contains("Cell:InternalClientCa", message, StringComparison.Ordinal);
    }

    private static string Refused(IConfiguration configuration) =>
        Assert.Throws<InvalidOperationException>(() => CellInternalListener.Load(configuration, CellOptions.Load(configuration))).Message;

    private IConfiguration ValidSettings(params (string Key, string? Value)[] overrides)
    {
        var path = Path.Combine(_folder, "ca.pem");
        if (!File.Exists(path))
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Test internal CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(path, authority.ExportCertificatePem());
        }

        var values = new Dictionary<string, string?>
        {
            ["Cell:InternalClientCa"] = path,
            ["Cell:InternalUrl"] = "https://axis-internal:8443",
            ["Kestrel:Endpoints:Internal:Url"] = "https://+:8443",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return CellSettings([.. values.Select(row => (row.Key, row.Value))]);
    }

    private static IConfiguration CellSettings(params (string Key, string? Value)[] values) =>
        Settings([
            ("Tenancy:Source", "directory"),
            ("Cell:Id", "vn-1"),
            ("Cell:Region", "vn"),
            ("Cell:TenantDomain", "vn.example.com"),
            .. values,
        ]);

    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(row => new KeyValuePair<string, string?>(row.Key, row.Value))).Build();
}
