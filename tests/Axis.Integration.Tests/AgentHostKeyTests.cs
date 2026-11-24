using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Agent;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// The agent host publishes the public half of its key in <c>platform.host_keys</c>, and the server answers it at <c>GET /api/agent-host/key</c>.
/// A value sealed with it opens in the agent host only, see <c>docs/reference/keys-and-data-protection.md#cryptographic-inventory</c>.
/// </summary>
public sealed class AgentHostKeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string Path = "/api/agent-host/key";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-host-key-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_value_sealed_with_the_published_key_opens_in_the_agent_host_and_not_in_the_server()
    {
        using var application = StudioPublishTests.NewFolder();
        var main = await database.CreateDatabaseAsync("agent_host_key", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(application.Path, [("a", StudioPublishTests.HostA, main)]);
        using var client = factory.CreateClient().SignedInAs("author");
        await using var agent = BuildAgent(main, KeyFile());
        await agent.StartAsync(CancellationToken);
        var hostKey = agent.Services.GetRequiredService<AgentHostKey>();

        using var answer = await FetchWhenPublishedAsync(client);

        var root = answer.RootElement;
        Assert.Equal(["keyId", "kem", "kdf", "aead", "publicKey"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal((16, 1, 2), (root.GetProperty("kem").GetInt32(), root.GetProperty("kdf").GetInt32(), root.GetProperty("aead").GetInt32()));
        var jwk = root.GetProperty("publicKey");
        Assert.Equal(["kty", "crv", "x", "y"], jwk.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("EC", "P-256"), (jwk.GetProperty("kty").GetString(), jwk.GetProperty("crv").GetString()));
        var x = jwk.GetProperty("x").GetString()!;
        var y = jwk.GetProperty("y").GetString()!;
        var recipient = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url.DecodeFromChars(x), Y = Base64Url.DecodeFromChars(y) },
        };
        Assert.Equal(JwkThumbprint.Of(recipient).KeyId, root.GetProperty("keyId").GetString());
        Assert.Equal(hostKey.KeyId, root.GetProperty("keyId").GetString());

        byte[] info = "axis test info"u8.ToArray();
        byte[] aad = "axis test aad"u8.ToArray();
        var sealedValue = Hpke.Seal(recipient, info, aad, "sk-person-1234"u8);

        Assert.Equal("sk-person-1234"u8.ToArray(), hostKey.Open(info, aad, sealedValue));
        Assert.Throws<CryptographicException>(() => hostKey.Open(info, "other aad"u8, sealedValue));
        var installationKey = TenantSecretCipher.FromKey(Convert.FromBase64String(TestPlatform.SecretKey));
        Assert.ThrowsAny<Exception>(() => installationKey.Decrypt("a", TenantSecretFields.ModelKey, sealedValue));
        Assert.Null(factory.Services.GetService<AgentHostKey>());
        Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(), name => name.Name == "Axis.Agent");
    }

    [Fact]
    public async Task The_route_answers_401_with_no_session()
    {
        using var application = StudioPublishTests.NewFolder();
        var main = await database.CreateDatabaseAsync("agent_host_key_401", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(application.Path, [("a", StudioPublishTests.HostA, main)]);
        using var client = factory.CreateClient();

        using var response = await StudioPublishTests.SendAsync(client, HttpMethod.Get, Path, StudioPublishTests.HostA);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_route_answers_404_when_the_agent_host_was_built_without_a_key_file()
    {
        using var application = StudioPublishTests.NewFolder();
        var main = await database.CreateDatabaseAsync("agent_host_key_404", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(application.Path, [("a", StudioPublishTests.HostA, main)]);
        using var client = factory.CreateClient().SignedInAs("author");
        await using var agent = BuildAgent(main, keyFile: null);
        await agent.StartAsync(CancellationToken);

        // The agent host has had time to read the empty directory, and it published nothing.
        await Polling.WaitUntilAsync(() => agent.Services.GetRequiredService<BackgroundTenantCatalog>().IsLoaded, "The agent host did not read the directory.");
        using var response = await StudioPublishTests.SendAsync(client, HttpMethod.Get, Path, StudioPublishTests.HostA);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await StudioPublishTests.ReadAsync(response);
        Assert.Equal("No agent host key is published.", problem.RootElement.GetProperty("title").GetString());
        Assert.Null(agent.Services.GetService<AgentHostKey>());
    }

    private static async Task<JsonDocument> FetchWhenPublishedAsync(HttpClient client)
    {
        JsonDocument? answer = null;
        await Polling.WaitUntilAsync(
            async () =>
            {
                using var response = await StudioPublishTests.SendAsync(client, HttpMethod.Get, Path, StudioPublishTests.HostA);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    return false;
                }

                answer = await StudioPublishTests.ReadAsync(response);
                return true;
            },
            "The agent host did not publish its key.");
        return answer!;
    }

    private string KeyFile()
    {
        var file = System.IO.Path.Combine(_folder, "agent-host-key.pem");
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(file, key.ExportPkcs8PrivateKeyPem());
        return file;
    }

    private static WebApplication BuildAgent(string main, string? keyFile)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(main),
            [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey,
        };
        if (keyFile is not null)
        {
            settings["Ai:HostKeyFile"] = keyFile;
        }

        return AgentTestHost.Build(aiOn: true, settings, new LogCollector());
    }
}
