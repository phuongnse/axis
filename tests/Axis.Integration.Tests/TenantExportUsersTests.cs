using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Admin;
using Axis.Administration;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The tenant export of a tenant with its own realm on a real Keycloak holds <c>users.csv</c>: the header <c>userName,email,displayName,roles,enabled,created</c>,
/// and one row for each user of the realm with the given roles of the snapshot, a platform role by its kind and an application role as
/// <c>&lt;application name&gt;:&lt;role&gt;</c>. No entry of the ZIP holds the password hash of the user, and no service account is listed. A tenant without its own realm gets no
/// <c>users.csv</c>.
/// </summary>
public sealed class TenantExportUsersTests(KeycloakFixture keycloak, PostgreSqlFixture database, MinioFixture minio)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<MinioFixture>, IAsyncLifetime
{
    private const string Username = "person@example.test";

    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Dictionary<string, string?> IdentityProvider => new()
    {
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

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
    public async Task The_zip_of_a_tenant_with_a_realm_holds_users_csv_with_the_given_roles_of_the_user_and_no_password_hash_and_no_service_account()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithUserAsync(scenario);
        var app = new AdminActivateRealmTests.Application();
        using var folder = AdminActivateRealmTests.Folder(app, [], provider: false);
        var activated = await scenario.RunAsync(["activate", folder.Path, "--tenant", id], identityProvider: _handler);
        Assert.Equal(0, activated.Exit);
        var userId = await GiveRolesAsync(scenario, id, app.Id);
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);

        var (job, output) = await RunExportAsync(scenario, id, bucket);

        Assert.True(output.Contains($"{id} export ok", StringComparison.Ordinal), output);
        var files = await ReadZipAsync(s3, bucket, $"exports/{id}/{job:D}.zip");
        var lines = files["users.csv"].TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("userName,email,displayName,roles,enabled,created", lines[0]);
        var row = Assert.Single(lines.Skip(1), line => line.StartsWith($"{Username},", StringComparison.Ordinal));
        Assert.Matches($"^{Regex.Escape($"{Username},{Username},{Username},administrator; {app.Name}:approver,true,")}\\d{{4}}-\\d{{2}}-\\d{{2}}T\\d{{2}}:\\d{{2}}:\\d{{2}}Z$", row);
        Assert.DoesNotContain(lines.Skip(1), line => line.StartsWith("service-account-", StringComparison.Ordinal));

        var hash = await PasswordHashAsync(userId);
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.All(files, file => Assert.DoesNotContain(hash, file.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_zip_of_a_tenant_without_its_own_realm_holds_no_users_csv()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await scenario.ForgetRealmUsersAsync(id);
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);

        var (job, output) = await RunExportAsync(scenario, id, bucket);

        Assert.True(output.Contains($"{id} export ok", StringComparison.Ordinal), output);
        var files = await ReadZipAsync(s3, bucket, $"exports/{id}/{job:D}.zip");
        Assert.Contains("export.json", files.Keys);
        Assert.DoesNotContain("users.csv", files.Keys);
    }

    private async Task<(Guid Job, string Output)> RunExportAsync(AdminScenario scenario, string tenantId, string bucket)
    {
        var platform = scenario.Platform;
        var storage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
        await using var source = NpgsqlDataSource.Create(platform);
        using var http = new HttpClient(_handler, disposeHandler: false);
        var steps = TenantExport.Steps(new TenantExportSettings(
            platform,
            AdminScenario.Cipher,
            PackageCatalog.Load(SamplePackages.Path).Versions,
            new SensitiveValues(new TenantKeys(new DirectoryTenantKeySource(platform, AdminScenario.Cipher))),
            JobSetting<CellBackupStorage>.Of(storage),
            http,
            new TenantLeases(source, NullLogger<TenantLeases>.Instance),
            null));
        var kinds = new Dictionary<string, IReadOnlyList<JobStep>> { ["export"] = steps };
        var job = await ProvisioningJobs.QueueAsync(platform, kinds, "export", tenantId, "operator-1", null, null, CancellationToken);
        using var output = new StringWriter();

        Assert.True(await new ProvisioningJobs(platform, kinds, _lease).RunNextAsync(output, CancellationToken));
        return (job, output.ToString());
    }

    // Creates the tenant with its realm and puts one user in it, who has a password.
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
                users = new[]
                {
                    new
                    {
                        username = Username,
                        email = Username,
                        emailVerified = true,
                        enabled = true,
                        credentials = new[] { new { type = "password", value = $"Long-unbreached-{Guid.NewGuid():N}-password" } },
                    },
                },
            });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return id;
    }

    // Gives the Keycloak user one platform role and one application role, as rows of the tenant's given roles, and returns the user's id.
    private async Task<string> GiveRolesAsync(AdminScenario scenario, string id, Guid applicationId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(
            http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{id}/users?username={Uri.EscapeDataString(Username)}&exact=true", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var userId = users.EnumerateArray().Single().GetProperty("id").GetString()!;

        var row = await scenario.RowAsync(id);
        var password = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        foreach (var (application, role) in new (Guid?, string)[] { (null, "administrator"), (applicationId, "approver") })
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO axis.given_roles (id, user_id, application_id, role) VALUES (@id, @user, @application, @role)", connection);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("user", userId);
            command.Parameters.Add(new NpgsqlParameter("application", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)application ?? DBNull.Value });
            command.Parameters.AddWithValue("role", role);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return userId;
    }

    // The hash that Keycloak keeps for the password of the user, read as the read-only role of its credential table.
    private async Task<string?> PasswordHashAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(await keycloak.CredentialsConnectionStringAsync());
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT secret_data FROM credential WHERE user_id = @user AND type = 'password'
            """,
            connection);
        command.Parameters.AddWithValue("user", userId);
        var secret = (string?)await command.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(secret);
        using var document = JsonDocument.Parse(secret);
        return document.RootElement.GetProperty("value").GetString();
    }

    private static async Task<string> NewBucketAsync(IAmazonS3 s3)
    {
        var bucket = $"exports-{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6))}";
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, CancellationToken);
        return bucket;
    }

    private static async Task<Dictionary<string, string>> ReadZipAsync(IAmazonS3 s3, string bucket, string key)
    {
        using var memory = new MemoryStream(await MinioFixture.ReadObjectAsync(s3, bucket, key, CancellationToken));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            await using var stream = await entry.OpenAsync(CancellationToken);
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            files.Add(entry.FullName, await reader.ReadToEndAsync(CancellationToken));
        }

        return files;
    }

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
}
