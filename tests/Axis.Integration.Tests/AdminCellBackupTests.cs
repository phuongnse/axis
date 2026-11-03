using System.Globalization;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>cell backup</c> backs up every active and suspended tenant of a cell with the tenant's own backup key and uploads
/// <c>&lt;tenant id&gt;/&lt;date&gt;.axisbak</c> to an object-locked bucket. A failed tenant is tried twice and the others still go
/// up. Each tenant gets one platform audit record. Outside a cell, or with a missing setting, the command uploads nothing.
/// </summary>
public sealed class AdminCellBackupTests(PostgreSqlFixture database, MinioFixture minio) : IClassFixture<PostgreSqlFixture>, IClassFixture<MinioFixture>, IDisposable
{
    private const string Command = "cell backup";

    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        File.Delete(_cellKeyFile);
    }

    [Fact]
    public async Task Every_active_and_suspended_tenant_gets_one_archive_that_opens_with_its_own_key_and_a_provisioning_tenant_gets_none()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        using var s3 = minio.NewClient();
        var cellKey = WriteCellKey();
        var bucket = await NewBucketAsync(s3);
        var cell = CellSettings(bucket);
        var (a, b, provisioning) = (scenario.NewTenantId(), scenario.NewTenantId(), scenario.NewTenantId());
        foreach (var id in new[] { a, b, provisioning })
        {
            Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), extra: cell)).Exit);
        }

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", b], extra: cell)).Exit);
        await scenario.SetStatusAsync(provisioning, "provisioning");

        var before = Today();
        var result = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: cell);
        var after = Today();

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        var keys = await KeysAsync(s3, bucket);
        Assert.Equal(Sorted(a, b), keys.Select(key => key.Split('/')[0]).Order(StringComparer.Ordinal));
        foreach (var id in new[] { a, b })
        {
            Assert.Contains(keys, key => key == $"{id}/{before}.axisbak" || key == $"{id}/{after}.axisbak");
            Assert.Contains($"{id} upload ok", result.Output);
        }

        Assert.DoesNotContain(result.Output, line => line.StartsWith(provisioning, StringComparison.Ordinal));

        var keyOfA = await UnwrapAsync(scenario, a, cellKey);
        var keyOfB = await UnwrapAsync(scenario, b, cellKey);
        foreach (var (id, key) in new[] { (a, keyOfA), (b, keyOfB) })
        {
            await DecryptAsync(s3, bucket, keys.Single(name => name.StartsWith($"{id}/", StringComparison.Ordinal)), key);
        }

        await Assert.ThrowsAsync<BackupArchiveException>(
            () => DecryptAsync(s3, bucket, keys.Single(name => name.StartsWith($"{a}/", StringComparison.Ordinal)), keyOfB));

        Assert.Equal(Sorted(a, b), (await AuditedAsync(scenario)).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(result.Output.Concat(result.Error), line => line.Contains(minio.SecretKey, StringComparison.Ordinal));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_tenant_whose_database_is_gone_fails_twice_the_others_are_uploaded_and_each_tenant_has_one_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        using var s3 = minio.NewClient();
        WriteCellKey();
        var bucket = await NewBucketAsync(s3);
        var cell = CellSettings(bucket);
        var (a, b, c) = (scenario.NewTenantId(), scenario.NewTenantId(), scenario.NewTenantId());
        foreach (var id in new[] { a, b, c })
        {
            Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), extra: cell)).Exit);
        }

        await PostgreSqlServer.DropDatabaseAsync($"axis_{b}", CancellationToken);

        var result = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: cell);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal(2, result.Output.Count(line => line.StartsWith($"{b} dump failed:", StringComparison.Ordinal)));
        Assert.DoesNotContain(result.Output, line => line.StartsWith($"{b} upload", StringComparison.Ordinal));
        Assert.Equal(Sorted(a, c), (await KeysAsync(s3, bucket)).Select(key => key.Split('/')[0]).Order(StringComparer.Ordinal));
        foreach (var id in new[] { a, c })
        {
            Assert.Contains($"{id} upload ok", result.Output);
            Assert.DoesNotContain(result.Output, line => line.StartsWith($"{id}", StringComparison.Ordinal) && line.Contains("failed", StringComparison.Ordinal));
        }

        // The retry writes no second record.
        Assert.Equal(Sorted(a, b, c), (await AuditedAsync(scenario)).Order(StringComparer.Ordinal));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failed_upload_is_tried_twice_and_names_the_error_code_and_never_the_address_or_the_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        WriteCellKey();
        var id = scenario.NewTenantId();
        var cell = CellSettings($"missing-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}");
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), extra: cell)).Exit);

        var result = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: cell);

        Assert.Equal(1, result.Exit);
        Assert.Equal(2, result.Output.Count(line => line == $"{id} upload failed: NoSuchBucket"));
        Assert.Equal([id], await AuditedAsync(scenario));
        var text = string.Join('\n', result.Output.Concat(result.Error));
        Assert.DoesNotContain(minio.SecretKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(minio.ServiceUrl, text, StringComparison.Ordinal);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Outside_a_cell_or_with_a_missing_setting_the_command_exits_2_uploads_nothing_and_writes_no_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        using var s3 = minio.NewClient();
        WriteCellKey();
        var bucket = await NewBucketAsync(s3);
        var cell = CellSettings(bucket);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), extra: cell)).Exit);
        var empty = Directory.CreateTempSubdirectory("axis-no-tools-");
        try
        {
            var storage = CellSettings(bucket).Where(pair => pair.Key.StartsWith("Cell:BackupStorage:", StringComparison.Ordinal)).ToDictionary();
            var outside = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: storage);
            var noBucket = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: new Dictionary<string, string?>(cell) { ["Cell:BackupStorage:Bucket"] = null });
            var noSecret = await scenario.RunAsync(["cell", "backup"], path: tools.PathSetting, extra: new Dictionary<string, string?>(cell) { ["Cell:BackupStorage:SecretAccessKey"] = "" });
            var noPgDump = await scenario.RunAsync(["cell", "backup"], path: empty.FullName, extra: cell);
            var extraArgument = await scenario.RunAsync(["cell", "backup", "now"], path: tools.PathSetting, extra: cell);

            Assert.Equal(["This command runs only in a cell: Cell:Id is not set."], outside.Error);
            Assert.Equal(["The setting Cell:BackupStorage:Bucket is required."], noBucket.Error);
            Assert.Equal(["The setting Cell:BackupStorage:SecretAccessKey is required."], noSecret.Error);
            Assert.Equal([$"pg_dump was not found in the directories of {AdminCommands.PathSetting}."], noPgDump.Error);
            Assert.StartsWith("Usage:", extraArgument.Error[0], StringComparison.Ordinal);
            foreach (var refused in new[] { outside, noBucket, noSecret, noPgDump, extraArgument })
            {
                Assert.Equal(2, refused.Exit);
                Assert.Empty(refused.Output);
            }
        }
        finally
        {
            empty.Delete(recursive: true);
        }

        Assert.Empty(await KeysAsync(s3, bucket));
        Assert.Empty(await AuditedAsync(scenario));
        await scenario.AssertNoSecretsAsync();
    }

    private static string[] Sorted(params string[] ids) => [.. ids.Order(StringComparer.Ordinal)];

    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Dictionary<string, string?> CellSettings(string bucket) => new()
    {
        ["Tenancy:Source"] = "directory",
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:BackupKeyFile"] = _cellKeyFile,
        ["Cell:BackupStorage:ServiceUrl"] = minio.ServiceUrl,
        ["Cell:BackupStorage:Region"] = "us-east-1",
        ["Cell:BackupStorage:Bucket"] = bucket,
        ["Cell:BackupStorage:AccessKeyId"] = minio.AccessKey,
        ["Cell:BackupStorage:SecretAccessKey"] = minio.SecretKey,
    };

    /// <summary>Writes a new random cell key to a file and returns the key. The file holds the base64 text.</summary>
    private byte[] WriteCellKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_cellKeyFile, Convert.ToBase64String(key) + "\n");
        return key;
    }

    /// <summary>A bucket with object lock and a default compliance retention, as the backup storage of a cell has.</summary>
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

    /// <summary>Reads the whole archive with the key. A wrong key or a damaged chunk throws.</summary>
    private static async Task DecryptAsync(IAmazonS3 s3, string bucket, string key, byte[] backupKey)
    {
        using var response = await s3.GetObjectAsync(bucket, key, CancellationToken);
        await using var plain = BackupArchive.Decrypt(response.ResponseStream, backupKey);
        await plain.CopyToAsync(Stream.Null, CancellationToken);
    }

    /// <summary>The tenant of each <c>cell backup</c> audit record, one entry for each record.</summary>
    private static async Task<List<string>> AuditedAsync(AdminScenario scenario) =>
        [.. (await scenario.AuditRecordsAsync()).Where(record => record.Command == Command).Select(record => record.TenantId ?? "")];

    /// <summary>The tenant's backup key, unwrapped with the cell's key.</summary>
    private static async Task<byte[]> UnwrapAsync(AdminScenario scenario, string id, byte[] cellKey)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        var stored = await context.TenantBackupKeys.AsNoTracking().Where(row => row.TenantId == id).Select(row => row.Key).SingleAsync(CancellationToken);
        return Convert.FromBase64String(TenantSecretCipher.FromKey(cellKey).Decrypt(id, TenantSecretFields.BackupKey, stored).Reveal());
    }
}
