using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant backup</c> of a tenant with its own realm on a real Keycloak. With the credentials connection string, the archive holds
/// <c>realm.json</c> right after the manifest, with the roles, groups and users of the realm and the credentials of each user, and no client
/// and no service-account user. Nothing but the archive reaches the disk, and no line holds a credential. Without the setting there is no
/// <c>realm.json</c> and the output is as before. A credentials connection string that does not work fails the <c>realm</c> step and leaves no file.
/// </summary>
public sealed class AdminTenantBackupRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Setting = "IdentityProvider:DatabaseConnectionString";
    private const string Username = "person@example.test";
    private const string TotpSecret = "JBSWY3DPEHPK3PXP-axis-totp-secret";

    private static readonly string[] _requiredActions = ["UPDATE_PROFILE"];
    private static readonly string[] _realmRoles = ["approver"];
    private static readonly string[] _groupPaths = ["/finance"];
    private static readonly Dictionary<string, string[]> _clientRoles = new() { ["axis"] = ["reviewer"] };

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private PostgresClientTools _tools = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Dictionary<string, string?> IdentityProvider => new()
    {
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _tools = await PostgresClientTools.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}", null);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task Backup_writes_realm_json_after_the_manifest_with_the_credentials_roles_groups_and_required_actions_of_a_user_and_no_client()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await BackupAsync(scenario, id, file, await keycloak.CredentialsConnectionStringAsync());

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} dump ok", $"{id} realm ok", $"{id} archive ok"], result.Output);
        Assert.Empty(result.Error);
        var entries = ReadEntries(file);
        Assert.Equal(["manifest.json", "realm.json", "tenant.dump"], entries.Select(entry => entry.Name));
        using var document = JsonDocument.Parse(entries.Single(entry => entry.Name == "realm.json").Text);
        var root = document.RootElement;
        Assert.Equal(id, root.GetProperty("realm").GetString());
        Assert.False(root.TryGetProperty("clients", out _));

        var user = Assert.Single(root.GetProperty("users").EnumerateArray(), user => user.GetProperty("username").GetString() == Username);
        Assert.False(string.IsNullOrEmpty(user.GetProperty("id").GetString()));
        Assert.Equal(["UPDATE_PROFILE"], Strings(user.GetProperty("requiredActions")));
        Assert.Contains("approver", Strings(user.GetProperty("realmRoles")));
        Assert.Equal(["reviewer"], Strings(user.GetProperty("clientRoles").GetProperty("axis")));
        Assert.Equal(["/finance"], Strings(user.GetProperty("groups")));
        var credentials = user.GetProperty("credentials").EnumerateArray().ToList();
        Assert.Equal(["otp", "password"], credentials.Select(credential => credential.GetProperty("type").GetString()!).Order(StringComparer.Ordinal));
        Assert.False(string.IsNullOrEmpty(PasswordHash(root)));
        Assert.Contains(TotpSecret, credentials.Single(credential => credential.GetProperty("type").GetString() == "otp").GetProperty("secretData").GetString());
        Assert.Contains("\"totp\"", credentials.Single(credential => credential.GetProperty("type").GetString() == "otp").GetProperty("credentialData").GetString());

        var roles = root.GetProperty("roles");
        Assert.Contains("approver", roles.GetProperty("realm").EnumerateArray().Select(role => role.GetProperty("name").GetString()));
        Assert.Contains("reviewer", roles.GetProperty("client").GetProperty("axis").EnumerateArray().Select(role => role.GetProperty("name").GetString()));
        Assert.Equal(["finance"], root.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("name").GetString()));

        // The service accounts of the axis-activation and axis-users clients are users of the realm, and they are not in the file.
        Assert.DoesNotContain(
            root.GetProperty("users").EnumerateArray(),
            member => member.GetProperty("username").GetString()!.StartsWith("service-account-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Backup_leaves_only_the_archive_in_the_out_folder_and_prints_no_credential()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await BackupAsync(scenario, id, file, await keycloak.CredentialsConnectionStringAsync());

        Assert.Equal(0, result.Exit);
        Assert.Equal([file], Directory.GetFileSystemEntries(folder.Path));
        using var document = JsonDocument.Parse(ReadEntries(file).Single(entry => entry.Name == "realm.json").Text);
        var hash = PasswordHash(document.RootElement);
        Assert.False(string.IsNullOrEmpty(hash));
        foreach (var line in result.Output.Concat(result.Error))
        {
            Assert.DoesNotContain(hash, line, StringComparison.Ordinal);
            Assert.DoesNotContain(TotpSecret, line, StringComparison.Ordinal);
        }

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Backup_without_the_setting_writes_no_realm_json_and_the_two_lines_of_before()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await BackupAsync(scenario, id, file, credentials: null);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} dump ok", $"{id} archive ok"], result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(["manifest.json", "tenant.dump"], ReadEntries(file).Select(entry => entry.Name));
    }

    [Fact]
    public async Task Backup_with_a_wrong_password_in_the_setting_fails_the_realm_step_and_leaves_no_file()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var wrong = new NpgsqlConnectionStringBuilder(await keycloak.CredentialsConnectionStringAsync()) { Password = "wrong-password" }.ConnectionString;

        var result = await BackupAsync(scenario, id, file, wrong);

        Assert.Equal(1, result.Exit);
        Assert.StartsWith($"{id} realm failed:", result.Output[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("archive", string.Join('\n', result.Output), StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", string.Join('\n', result.Output.Concat(result.Error)), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Backup_with_a_setting_that_is_no_connection_string_exits_2_naming_the_setting()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await BackupAsync(scenario, id, file, "not a connection string");

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Equal([$"The setting {Setting} is not a valid connection string."], result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    private Task<AdminResult> BackupAsync(AdminScenario scenario, string id, string file, string? credentials)
    {
        var extra = IdentityProvider;
        extra[Setting] = credentials;
        return scenario.RunAsync(
            ["tenant", "backup", id, "--out", file, .. AdminScenario.BackupKeyArgs],
            path: _tools.PathSetting,
            extra: extra,
            identityProvider: _handler);
    }

    // Creates the tenant with its realm, then fills the realm in one partial import: a realm role, a client role of axis, a group and a user
    // who holds all three, has a required action and has a password and an authenticator.
    private async Task<string> CreateTenantWithUserAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: IdentityProvider);
        Assert.Equal(0, created.Exit);

        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(
            http,
            await TokenAsync(http),
            HttpMethod.Post,
            $"admin/realms/{id}/partialImport",
            new
            {
                ifResourceExists = "FAIL",
                roles = new
                {
                    realm = new[] { new { name = "approver" } },
                    client = new Dictionary<string, object[]> { ["axis"] = [new { name = "reviewer" }] },
                },
                groups = new[] { new { name = "finance", path = "/finance" } },
                users = new[]
                {
                    new
                    {
                        username = Username,
                        email = Username,
                        emailVerified = true,
                        enabled = true,
                        requiredActions = _requiredActions,
                        realmRoles = _realmRoles,
                        clientRoles = _clientRoles,
                        groups = _groupPaths,
                        credentials = new object[]
                        {
                            new { type = "password", value = $"Long-unbreached-{Guid.NewGuid():N}-password" },
                            new
                            {
                                type = "otp",
                                secretData = $$"""{"value":"{{TotpSecret}}"}""",
                                credentialData = """{"subType":"totp","digits":6,"counter":0,"period":30,"algorithm":"HmacSHA1"}""",
                            },
                        },
                    },
                },
            });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return id;
    }

    // The entries of the decrypted archive in the order of the tar, each kept in memory.
    private static List<ArchiveEntry> ReadEntries(string file)
    {
        var entries = new List<ArchiveEntry>();
        using var stream = File.OpenRead(file);
        using var decrypted = BackupArchive.Decrypt(stream, AdminScenario.BackupKeyBytes);
        using var reader = new TarReader(decrypted);
        while (reader.GetNextEntry() is { } entry)
        {
            using var content = new MemoryStream();
            entry.DataStream!.CopyTo(content);
            entries.Add(new ArchiveEntry(entry.Name, entry.Name == "tenant.dump" ? "" : Encoding.UTF8.GetString(content.ToArray())));
        }

        return entries;
    }

    private static string? PasswordHash(JsonElement root)
    {
        var password = root.GetProperty("users").EnumerateArray()
            .SelectMany(user => user.GetProperty("credentials").EnumerateArray())
            .Single(credential => credential.GetProperty("type").GetString() == "password");
        using var secret = JsonDocument.Parse(password.GetProperty("secretData").GetString()!);
        return secret.RootElement.GetProperty("value").GetString();
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private async Task<string> TokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }

    // One entry of the archive. The dump has no text here.
    private sealed record ArchiveEntry(string Name, string Text);
}
