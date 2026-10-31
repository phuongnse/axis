using System.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant backup</c> and <c>tenant restore</c> of a tenant with 100,000 purchase requests, two line items each, finish within
/// their time limits, and the restored database holds every row.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class BackupRestorePerformanceTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const int Requests = 100_000;
    private const int ItemsPerRequest = 2;

    private static readonly Guid _purchaseRequestEntity = Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5");
    private static readonly Guid _lineItemEntity = Guid.Parse("7c1f3e52-9b4a-4d8e-a6f0-3b2d5e8c91a4");

    // A backup runs pg_dump through the docker exec wrapper and packs the dump into the archive. It took about 2.2 s when
    // measured, for an archive of about 12 MB. The bound is more than ten times that, so shared CI runners do not make the
    // test flaky. It still catches a dump that is read into memory or written one row at a time.
    private static readonly TimeSpan _backupTime = TimeSpan.FromSeconds(30);

    // A restore creates the roles and the database, runs pg_restore through the docker exec wrapper, then grants and checks
    // the tenant. It took about 1.5 s when measured. The bound is far above that, so shared CI runners do not make the test
    // flaky. It still catches a restore that replays the data as one statement per row.
    private static readonly TimeSpan _restoreTime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_and_restore_of_a_tenant_with_100000_requests_each_finish_within_the_time_limit_and_keep_every_row()
    {
        var requests = EntityNaming.QualifiedTable(EntityNaming.Table(_purchaseRequestEntity));
        var items = EntityNaming.QualifiedTable(EntityNaming.Table(_lineItemEntity));
        await fixture.ExecuteAsync(
            $"""
            INSERT INTO {requests} (id, f_number, f_title, f_department, f_status)
            SELECT gen_random_uuid(), 'PR-P' || n, 'Request ' || n, '{SampleAppFixture.Operations:D}', 'draft'
            FROM generate_series(1, {Requests}) AS n
            """);
        await fixture.ExecuteAsync(
            $"""
            INSERT INTO {items} ({EntityNaming.OwnerColumn}, {EntityNaming.PositionColumn}, id, f_description, f_quantity, f_unitprice, f_amount)
            SELECT request.id, p, gen_random_uuid(), 'Item ' || p, p + 1, 10, (p + 1) * 10
            FROM {requests} AS request, generate_series(0, {ItemsPerRequest - 1}) AS p
            """);
        await fixture.ExecuteAsync($"ANALYZE {requests}");
        await fixture.ExecuteAsync($"ANALYZE {items}");
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var stopwatch = Stopwatch.StartNew();
        var backup = await fixture.BackupAsync(file);
        stopwatch.Stop();
        var backupElapsed = stopwatch.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine($"The backup took {backupElapsed}, and the archive is {new FileInfo(file).Length} bytes.");
        Assert.Equal(0, backup.Exit);
        Assert.True(backupElapsed < _backupTime, $"The backup took {backupElapsed}.");

        var newId = fixture.Scenario.NewTenantId();
        stopwatch.Restart();
        var restore = await fixture.Scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", newId, "--host", "perf-restored.example.test", .. AdminScenario.BackupKeyArgs],
            path: fixture.Tools.PathSetting);
        stopwatch.Stop();
        var restoreElapsed = stopwatch.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine($"The restore took {restoreElapsed}.");
        Assert.Equal(0, restore.Exit);
        Assert.True(restoreElapsed < _restoreTime, $"The restore took {restoreElapsed}.");
        var original = Database(fixture.TenantId);
        var restored = Database(newId);
        Assert.Equal(Requests, await CountAsync(original, requests));
        Assert.Equal(Requests * ItemsPerRequest, await CountAsync(original, items));
        Assert.Equal(await CountAsync(original, requests), await CountAsync(restored, requests));
        Assert.Equal(await CountAsync(original, items), await CountAsync(restored, items));
    }

    private string Database(string id) =>
        new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform) { Database = $"axis_{id}" }.ConnectionString;

    private static async Task<long> CountAsync(string connectionString, string table) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT count(*) FROM {table}", CancellationToken))!;
}
