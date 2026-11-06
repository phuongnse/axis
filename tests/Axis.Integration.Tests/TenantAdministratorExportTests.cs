using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Administration;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's export on the tenant's own host, see <c>docs/reference/tenant-lifecycle.md#exports-by-a-tenant-administrator</c>,
/// with real PostgreSQL, Keycloak and MinIO. Tenant <c>a</c> is served at <c>localhost</c> in a cell. The tests mark the queued job as
/// succeeded and put a ZIP in the bucket, as the tests of the provisioning API's exports route do, and do not run the publisher.
/// </summary>
public sealed class TenantAdministratorExportTests(PostgreSqlFixture database, KeycloakFixture keycloak, MinioFixture minio)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<MinioFixture>
{
    private const string AdministratorRole = "tenant-administrator";

    private string _platform = null!;
    private CellBackupStorage _storage = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("active")]
    [InlineData("readOnly")]
    public async Task An_administrator_starts_an_export_and_downloads_its_ZIP_and_each_step_is_recorded(string status)
    {
        await using var factory = await StartAsync(status);
        using var client = factory.CreateClient().SignedInAs("admin");

        using var start = await PostAsync(client, csrf: true);

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var id = started.GetProperty("id").GetGuid();
        Assert.Equal("a", started.GetProperty("tenantId").GetString());
        Assert.Equal("queued", started.GetProperty("state").GetString());
        Assert.Equal($"/api/tenant/exports/{id:D}", start.Headers.Location!.OriginalString);
        Assert.Equal([("export", "queued", "admin")], await JobsAsync());
        Assert.Equal([("tenant export start", "admin", $"export:{id:D}")], await RecordsAsync());

        var bytes = RandomNumberGenerator.GetBytes(256);
        await ExecuteAsync($"UPDATE platform.jobs SET state = 'succeeded', finished_at = now() WHERE id = '{id:D}'");
        using (var s3 = _storage.NewClient())
        using (var content = new MemoryStream(bytes))
        {
            await s3.PutObjectAsync(
                new PutObjectRequest { BucketName = _storage.Bucket, Key = TenantExports.Key("a", id), InputStream = content },
                CancellationToken);
        }

        using var read = await client.GetAsync(new Uri($"/api/tenant/exports/{id:D}", UriKind.Relative), CancellationToken);
        using var download = await DownloadAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var export = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("succeeded", export.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, export.GetProperty("expiresAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(CancellationToken));
        Assert.Equal(
            [("tenant export start", "admin", $"export:{id:D}"), ("tenant export download", "admin", $"export:{id:D}")],
            await RecordsAsync());
    }

    [Fact]
    public async Task A_request_without_a_session_the_role_or_the_CSRF_header_is_refused_and_queues_nothing()
    {
        await using var factory = await StartAsync("active");
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClient().SignedInAs("member");
        using var administrator = factory.CreateClient().SignedInAs("admin");

        using var noSession = await PostAsync(anonymous, csrf: true);
        using var noRole = await PostAsync(member, csrf: true);
        using var noCsrf = await PostAsync(administrator, csrf: false);
        using var memberRead = await member.GetAsync(new Uri($"/api/tenant/exports/{Guid.NewGuid():D}", UriKind.Relative), CancellationToken);
        using var anonymousRead = await anonymous.GetAsync(new Uri($"/api/tenant/exports/{Guid.NewGuid():D}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task A_second_start_while_the_first_is_queued_answers_the_first_export_and_queues_nothing()
    {
        await using var factory = await StartAsync("active");
        using var client = factory.CreateClient().SignedInAs("admin");

        using var first = await PostAsync(client, csrf: true);
        using var second = await PostAsync(client, csrf: true);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        var again = await second.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(firstId, again.GetProperty("id").GetGuid());
        Assert.Equal($"/api/tenant/exports/{firstId:D}", second.Headers.Location!.OriginalString);
        Assert.Single(await JobsAsync());
        Assert.Single(await RecordsAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, bool csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/tenant/exports", UriKind.Relative));
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient client, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/tenant/exports/{id:D}", UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<List<(string Kind, string State, string Actor)>> JobsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT kind, state, actor FROM platform.jobs ORDER BY created_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var jobs = new List<(string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            jobs.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return jobs;
    }

    private async Task<List<(string Command, string Actor, string Changed)>> RecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, actor, array_to_string(changed, ',') FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<WebApplicationFactory<Program>> StartAsync(string tenantStatus)
    {
        var authority = await keycloak.StartAsync();
        _platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);

        // The directory loads at start, so the status is set before the first request.
        await ExecuteAsync($"UPDATE platform.tenants SET status = '{tenantStatus}' WHERE id = 'a'");

        // A bucket of its own, so no test sees another's objects.
        var bucket = $"exports-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        _storage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
        using (var s3 = _storage.NewClient())
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, CancellationToken);
        }

        var platform = _platform;
        var storage = _storage;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", "console.test");
            builder.UseSetting("Cell:OperatorRealm", "operator");
            builder.UseSetting("Cell:OperatorRole", "cell-operator");
            builder.UseSetting("IdentityProvider:Url", authority.ToString());
            builder.UseSetting("Users:AdministratorRole", AdministratorRole);
            builder.UseSetting("Cell:ExportStorage:ServiceUrl", storage.ServiceUrl);
            builder.UseSetting("Cell:ExportStorage:Region", storage.Region);
            builder.UseSetting("Cell:ExportStorage:Bucket", storage.Bucket);
            builder.UseSetting("Cell:ExportStorage:AccessKeyId", storage.AccessKeyId);
            builder.UseSetting("Cell:ExportStorage:SecretAccessKey", storage.SecretAccessKey);
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }
}
