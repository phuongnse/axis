using System.Security.Cryptography;
using Axis.Admin;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

public sealed class AdminKeyWrapTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("axis-key-wrap-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(KeyService.InstallationKey)]
    [InlineData(KeyService.AgentHostKey)]
    public async Task Wrap_prints_the_wrapped_key_that_unwraps_back_to_the_input_for_its_purpose_only(string purpose)
    {
        var other = purpose == KeyService.InstallationKey ? KeyService.AgentHostKey : KeyService.InstallationKey;
        var configuration = Configure(file: true);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["platform", "key", "wrap", "--purpose", purpose], configuration, new StringReader(key + "\n"), output, error, CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal("", error.ToString());
        var wrapped = output.ToString().TrimEnd('\r', '\n');
        Assert.DoesNotContain(wrapped, "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(key, wrapped, StringComparison.Ordinal);
        var keyService = KeyService.Load(configuration)!;
        Assert.Equal(key, keyService.Unwrap(purpose, "Platform:SecretKey", wrapped).Reveal());
        Assert.Throws<InvalidOperationException>(() => keyService.Unwrap(other, "Platform:SecretKey", wrapped));
    }

    [Fact]
    public async Task Wrap_keeps_the_text_of_a_pem_as_it_is_and_drops_only_the_last_line_break()
    {
        var configuration = Configure(file: true);
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pem = pair.ExportPkcs8PrivateKeyPem();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["platform", "key", "wrap", "--purpose", KeyService.AgentHostKey], configuration, new StringReader(pem + "\n"), output, error, CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(pem, KeyService.Load(configuration)!.Unwrap(KeyService.AgentHostKey, "Ai:HostKeyFile", output.ToString()).Reveal());
    }

    [Fact]
    public async Task Wrap_without_a_key_service_exits_2_and_names_the_setting()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["platform", "key", "wrap", "--purpose", KeyService.InstallationKey], Configure(file: false), new StringReader("key-text"), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.Equal("The setting KeyService:Kind is required." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public async Task Wrap_with_a_key_file_that_cannot_be_read_exits_2_and_names_the_setting_and_not_the_path()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeyService:Kind"] = "file",
            ["KeyService:KeyFile"] = Path.Combine(_folder, "missing"),
        }).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["platform", "key", "wrap", "--purpose", KeyService.InstallationKey], configuration, new StringReader("key-text"), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.Equal("The KeyService:KeyFile file cannot be read." + Environment.NewLine, error.ToString());
        Assert.DoesNotContain(_folder, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("platform", "key", "wrap")]
    [InlineData("platform", "key", "wrap", "--purpose")]
    [InlineData("platform", "key", "wrap", "--purpose", "other-key")]
    [InlineData("platform", "key", "wrap", "--purpose", "installation-key", "--extra")]
    [InlineData("platform", "key", "wrap", "--kind", "installation-key")]
    public async Task Wrap_with_an_unknown_purpose_or_argument_prints_the_usage_and_exits_2(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(args, Configure(file: true), new StringReader("key-text"), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.StartsWith("Usage:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("platform key wrap --purpose installation-key|agent-host-key", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("  \r\n")]
    public async Task Wrap_with_no_key_on_standard_input_exits_2(string input)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["platform", "key", "wrap", "--purpose", KeyService.InstallationKey], Configure(file: true), new StringReader(input), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.Equal("The key is required on standard input." + Environment.NewLine, error.ToString());
    }

    private IConfiguration Configure(bool file)
    {
        var values = new Dictionary<string, string?>();
        if (file)
        {
            var path = Path.Combine(_folder, "key-service-key");
            File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
            values["KeyService:Kind"] = "file";
            values["KeyService:KeyFile"] = path;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
