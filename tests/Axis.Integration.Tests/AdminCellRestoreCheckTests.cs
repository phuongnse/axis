using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Core.Telemetry;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>cell restore-check</c> on real PostgreSQL, MinIO and Keycloak. A good archive restores into the scratch tenant, which has the
/// counts and the users of the archive, is removed with its database, roles and realm, and records 1. An archive whose counts do not match its
/// data records 0, exits 1 and still removes the scratch tenant. The command writes one platform audit record for the source tenant, before its
/// first change. A missing <c>Cell:InternalDomain</c> makes it exit 2 and write no audit record.
/// </summary>
[Collection(ScratchTenantCollection)]
public sealed class AdminCellRestoreCheckTests(KeycloakFixture keycloak, PostgreSqlFixture database, MinioFixture minio) : IClassFixture<PostgreSqlFixture>, IClassFixture<MinioFixture>, IAsyncLifetime
{
    /// <summary>The tests that use a scratch tenant id share the database server and the identity provider, and each removes leftovers, so they run one at a time.</summary>
    public const string ScratchTenantCollection = "Restore-check scratch tenants";

    private const string Command = "cell restore-check";
    private const string Username = "checked-person";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");
    private Uri _baseAddress = null!;
    private PostgresClientTools _tools = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _tools = await PostgresClientTools.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var scratch = ScratchId();
        await PostgreSqlServer.DropDatabaseAsync($"axis_{scratch}");
        foreach (var role in new[] { "runtime", "schema" })
        {
            await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"""DROP ROLE IF EXISTS "axis_{scratch}_{role}" """, CancellationToken);
        }

        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms.Append(scratch))
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}", null);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
        File.Delete(_cellKeyFile);
    }

    [Fact]
    public async Task A_good_archive_restores_into_a_scratch_tenant_that_matches_it_records_1_and_leaves_nothing_behind()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var s3 = minio.NewClient();
        using var studio = new TemporaryFolder();
        var cellKey = WriteCellKey();
        var cell = CellSettings(await NewBucketAsync(s3), studio);
        var source = await CreateSourceAsync(scenario, cell);
        await CreateUserAsync(source, Username);
        var backup = new Dictionary<string, string?>(cell) { ["IdentityProvider:DatabaseConnectionString"] = await keycloak.CredentialsConnectionStringAsync() };
        Assert.Equal(0, (await scenario.RunAsync(["cell", "backup"], path: _tools.PathSetting, identityProvider: _handler, extra: backup)).Exit);
        var scratch = ScratchId();
        var key = (await KeysAsync(s3, cell["Cell:BackupStorage:Bucket"]!)).Single();
        Assert.Equal([Username], await ArchivedUsernamesAsync(s3, cell["Cell:BackupStorage:Bucket"]!, key, await UnwrapAsync(scenario, source, cellKey)));

        using var gauge = new GaugeCapture(source);
        var result = await scenario.RunAsync(["cell", "restore-check"], path: _tools.PathSetting, identityProvider: _handler, extra: cell);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains($"{source} download ok", result.Output);
        Assert.Contains($"{scratch} counts ok", result.Output);
        Assert.Contains($"{scratch} users ok", result.Output);
        Assert.Contains($"{scratch} remove ok", result.Output);
        Assert.Equal($"{source} restore-check passed", result.Output[^1]);
        Assert.Equal(1, gauge.Points[source]);
        var audited = (await scenario.AuditRecordsAsync()).Where(record => record.Command == Command).ToList();
        var record = Assert.Single(audited);
        Assert.Equal(source, record.TenantId);
        Assert.Equal([$"tenant: {scratch}"], record.Changed);
        await AssertScratchGoneAsync(scenario, scratch);
        Assert.Equal([Username], await UsernamesAsync(source));
        Assert.Equal(1, await CountTenantsAsync(scenario, source));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task An_archive_whose_counts_do_not_match_its_data_records_0_exits_1_and_still_removes_the_scratch_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var s3 = minio.NewClient();
        using var studio = new TemporaryFolder();
        var cellKey = WriteCellKey();
        var bucket = await NewBucketAsync(s3);
        var cell = CellSettings(bucket, studio);
        var source = await CreateSourceAsync(scenario, cell);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.axisbak");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", source, "--out", file], path: _tools.PathSetting, identityProvider: _handler, extra: cell)).Exit);
        var bad = Path.Combine(folder.Path, "bad.axisbak");
        var table = RaiseOneCount(file, bad, await UnwrapAsync(scenario, source, cellKey), out var archived);
        await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = $"{source}/{Today()}.axisbak", FilePath = bad }, CancellationToken);
        var scratch = ScratchId();

        using var gauge = new GaugeCapture(source);
        var result = await scenario.RunAsync(["cell", "restore-check"], path: _tools.PathSetting, identityProvider: _handler, extra: cell);

        Assert.Equal(1, result.Exit);
        Assert.Equal(0, gauge.Points[source]);
        Assert.Contains($"{scratch} counts failed: {table} has {archived - 1} rows and the archive {archived}", result.Output);
        Assert.DoesNotContain(result.Output, line => line.Contains("counts ok", StringComparison.Ordinal));
        Assert.Contains($"{scratch} remove ok", result.Output);
        Assert.Equal($"{source} restore-check failed", result.Output[^1]);
        await AssertScratchGoneAsync(scenario, scratch);
        Assert.Equal(1, await CountTenantsAsync(scenario, source));
    }

    [Fact]
    public async Task Without_the_internal_domain_the_command_exits_2_names_the_setting_and_writes_no_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var s3 = minio.NewClient();
        using var studio = new TemporaryFolder();
        WriteCellKey();
        var bucket = await NewBucketAsync(s3);
        await s3.PutObjectAsync(
            new PutObjectRequest { BucketName = bucket, Key = $"t-0000/{Today()}.axisbak", ContentBody = "archive" },
            CancellationToken);
        var cell = new Dictionary<string, string?>(CellSettings(bucket, studio)) { ["Cell:InternalDomain"] = null };

        var result = await scenario.RunAsync(["cell", "restore-check"], path: _tools.PathSetting, identityProvider: _handler, extra: cell);

        Assert.Equal(2, result.Exit);
        Assert.Equal(["The setting Cell:InternalDomain is required."], result.Error);
        Assert.Empty(result.Output);
        Assert.Empty(await scenario.AuditRecordsAsync());
        Assert.Equal(0, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(ScratchId()));
    }

    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ScratchId() => $"restore-check-{DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    private Dictionary<string, string?> CellSettings(string bucket, TemporaryFolder studio) => new()
    {
        ["Tenancy:Source"] = "directory",
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:InternalDomain"] = "internal.test",
        ["Cell:BackupKeyFile"] = _cellKeyFile,
        ["Cell:BackupStorage:ServiceUrl"] = minio.ServiceUrl,
        ["Cell:BackupStorage:Region"] = "us-east-1",
        ["Cell:BackupStorage:Bucket"] = bucket,
        ["Cell:BackupStorage:AccessKeyId"] = minio.AccessKey,
        ["Cell:BackupStorage:SecretAccessKey"] = minio.SecretKey,
        ["Studio:ApplicationsPath"] = studio.Path,
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    private byte[] WriteCellKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_cellKeyFile, Convert.ToBase64String(key) + "\n");
        return key;
    }

    private async Task<string> CreateSourceAsync(AdminScenario scenario, Dictionary<string, string?> cell)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: cell);
        Assert.Equal(0, created.Exit);
        return id;
    }

    // Copies the archive with the count of its first table, except the sessions, raised by one. Returns the table and its raised count.
    private static string RaiseOneCount(string source, string destination, byte[] key, out long raised)
    {
        var entries = new List<(string Name, byte[] Content)>();
        using (var stream = File.OpenRead(source))
        using (var decrypted = BackupArchive.Decrypt(stream, key))
        using (var reader = new TarReader(decrypted))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                using var content = new MemoryStream();
                entry.DataStream!.CopyTo(content);
                entries.Add((entry.Name, content.ToArray()));
            }
        }

        var index = entries.FindIndex(entry => entry.Name == "manifest.json");
        var manifest = JsonNode.Parse(entries[index].Content)!.AsObject();
        var counts = manifest["counts"]!.AsObject();
        var table = counts.Select(pair => pair.Key).Order(StringComparer.Ordinal).First(name => name != "axis.sessions");
        raised = counts[table]!.GetValue<long>() + 1;
        counts[table] = raised;
        entries[index] = ("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        using var archive = File.Create(destination);
        using var encrypted = BackupArchive.Encrypt(archive, key);
        using var writer = new TarWriter(encrypted, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var (name, content) in entries)
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) });
        }

        return table;
    }

    // The usernames in the realm.json of an uploaded archive.
    private static async Task<string[]> ArchivedUsernamesAsync(IAmazonS3 s3, string bucket, string key, byte[] backupKey)
    {
        using var response = await s3.GetObjectAsync(bucket, key, CancellationToken);
        await using var plain = BackupArchive.Decrypt(response.ResponseStream, backupKey);
        using var reader = new TarReader(plain);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name == "realm.json")
            {
                var document = JsonNode.Parse(entry.DataStream!)!;
                return [.. document["users"]!.AsArray().Select(user => user!["username"]!.GetValue<string>())];
            }
        }

        return [];
    }

    private static async Task<string> NewBucketAsync(IAmazonS3 s3)
    {
        var bucket = $"backups-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket, ObjectLockEnabledForBucket = true }, CancellationToken);
        await s3.PutObjectLockConfigurationAsync(
            new PutObjectLockConfigurationRequest
            {
                BucketName = bucket,
                ObjectLockConfiguration = new ObjectLockConfiguration
                {
                    ObjectLockEnabled = ObjectLockEnabled.Enabled,
                    Rule = new ObjectLockRule { DefaultRetention = new DefaultRetention { Mode = ObjectLockRetentionMode.Compliance, Days = 1 } },
                },
            },
            CancellationToken);
        return bucket;
    }

    private static async Task<List<string>> KeysAsync(IAmazonS3 s3, string bucket)
    {
        var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, CancellationToken);
        return [.. (response.S3Objects ?? []).Select(item => item.Key).Order(StringComparer.Ordinal)];
    }

    /// <summary>The tenant's backup key, unwrapped with the cell's key.</summary>
    private static async Task<byte[]> UnwrapAsync(AdminScenario scenario, string id, byte[] cellKey)
    {
        await using var context = NewContext(scenario);
        var stored = await context.TenantBackupKeys.AsNoTracking().Where(row => row.TenantId == id).Select(row => row.Key).SingleAsync(CancellationToken);
        return Convert.FromBase64String(TenantSecretCipher.FromKey(cellKey).Decrypt(id, TenantSecretFields.BackupKey, stored).Reveal());
    }

    private async Task AssertScratchGoneAsync(AdminScenario scenario, string scratch)
    {
        await using (var context = NewContext(scenario))
        {
            Assert.False(await context.Tenants.AnyAsync(row => row.Id.StartsWith("restore-check-"), CancellationToken));
            Assert.False(await context.TenantHosts.AnyAsync(row => row.TenantId == scratch, CancellationToken));
            Assert.False(await context.TenantDataKeys.AnyAsync(row => row.TenantId == scratch, CancellationToken));
            Assert.False(await context.TenantBackupKeys.AnyAsync(row => row.TenantId == scratch, CancellationToken));
        }

        Assert.False(await scenario.DatabaseExistsAsync(scratch));
        Assert.Equal(0, await scenario.CountRolesAsync(scratch));
        Assert.False(await RealmExistsAsync(scratch));
    }

    private static PlatformDbContext NewContext(AdminScenario scenario) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);

    private static async Task<int> CountTenantsAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario);
        return await context.Tenants.CountAsync(row => row.Id == id, CancellationToken);
    }

    private async Task CreateUserAsync(string realm, string username)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Post, $"admin/realms/{realm}/users", new { username, enabled = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string[]> UsernamesAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}/users?briefRepresentation=true&max=1000", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray()
            .Select(user => user.GetProperty("username").GetString()!)
            .Where(name => !name.StartsWith("service-account-", StringComparison.Ordinal))];
    }

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}", null);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.OK;
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

    /// <summary>
    /// Collects the points of the gauge <see cref="AxisMetrics.BackupRestoreCheck"/> for the given tenants. Other tests that run at the
    /// same time record on the same meter, so points of other tenants are left out.
    /// </summary>
    private sealed class GaugeCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public GaugeCapture(params string[] tenants)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AxisMetrics.MeterName && instrument.Name == AxisMetrics.BackupRestoreCheck)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == AxisTelemetry.TenantTag && tag.Value is string tenant && tenants.Contains(tenant))
                    {
                        Points[tenant] = value;
                    }
                }
            });
            _listener.Start();
        }

        public ConcurrentDictionary<string, long> Points { get; } = new();

        public void Dispose() => _listener.Dispose();
    }
}
