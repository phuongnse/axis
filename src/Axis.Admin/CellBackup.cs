using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Administration;
using Axis.Core.Telemetry;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>cell backup</c>: the daily backup job of a cell. It backs up every <c>active</c> and <c>suspended</c> tenant, four at a time,
/// with the tenant's own backup key, and uploads <c>&lt;tenant id&gt;/&lt;yyyy-mm-dd&gt;.axisbak</c> to the storage of
/// <see cref="CellBackupStorage"/>. The date is the UTC date at the start of the run. A tenant whose backup fails is tried once more
/// at once, and the other tenants go on. A tenant whose archive is uploaded gets its upload time stored in <c>platform.tenant_backups</c>,
/// and a tenant that fails keeps the time it had. At the end of the run, also when it stopped at its limit, the command records
/// <see cref="AxisMetrics.BackupLastSuccess"/> for each tenant that has a stored time, and exports it over OTLP when an OTLP endpoint is set.
/// The run stops after 6 hours. Each tenant gets one platform audit record,
/// written before its first attempt, and a retry writes none. Each archive is written to a folder that only its owner can use,
/// uploaded and deleted. Exit 0 means that every tenant succeeded, 1 that a tenant failed or the time ran out, and 2 that nothing
/// ran: outside a cell, or a missing setting or <c>pg_dump</c>, or an OTLP endpoint that is not <c>https</c> outside Development.
/// </summary>
internal static class CellBackup
{
    private const int Parallelism = 4;

    private const int Attempts = 2;

    private const string Archive = "archive.axisbak";

    private static readonly TimeSpan _timeLimit = TimeSpan.FromHours(6);

    public static async Task<int> BackupAsync(AdminRun run)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (run.Args.Length != 2)
        {
            run.Error.WriteLine(AdminCommands.Usage);
            return 2;
        }

        if (!run.Cell.IsCell)
        {
            run.Error.WriteLine("This command runs only in a cell: Cell:Id is not set.");
            return 2;
        }

        var environment = run.Configuration[AdminCommands.DotnetEnvironmentSetting] ?? Environments.Production;
        if (environment != Environments.Development && AxisTelemetry.InsecureOtlpEndpoint(name => run.Configuration[name]) is { } insecure)
        {
            run.Error.WriteLine($"'{insecure}' must be an https URL outside Development.");
            return 2;
        }

        var settings = AdminCommands.ReadBackupSettings(run.Configuration, out var problem);
        if (settings is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        var storage = CellBackupStorage.Read(run.Configuration, out problem);
        if (storage is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        var cellCipher = TenantBackupKeys.ReadCellCipher(run.Cell, out problem);
        if (cellCipher is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        if (AdminCommands.FindTool(settings.SearchPath, "pg_dump") is null)
        {
            run.Error.WriteLine($"pg_dump was not found in the directories of {AdminCommands.PathSetting}.");
            return 2;
        }

        string[] ids;
        try
        {
            problem = await AdminCommands.PlatformTablesProblemAsync(settings.PlatformConnectionString, run.CancellationToken);
            if (problem is not null)
            {
                run.Error.WriteLine(problem);
                return 2;
            }

            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
            ids = await platform.Tenants.AsNoTracking()
                .Where(tenant => tenant.Status == "active" || tenant.Status == "suspended")
                .OrderBy(tenant => tenant.Id)
                .Select(tenant => tenant.Id)
                .ToArrayAsync(run.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
        {
            run.Error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return 2;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(run.CancellationToken);
        limit.CancelAfter(_timeLimit);
        var output = TextWriter.Synchronized(run.Output);
        var audit = new TenantAudit(run.Command.Name, settings.PlatformConnectionString);
        using var client = storage.NewClient();
        using var metrics = CellBackupMetrics.Start(run.Configuration);
        var failed = 0;
        var stopped = false;
        try
        {
            await Parallel.ForEachAsync(
                ids,
                new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = limit.Token },
                async (id, token) =>
                {
                    if (!await BackUpTenantAsync(settings, cellCipher, storage, client, audit, id, date, output, token))
                    {
                        Interlocked.Increment(ref failed);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            if (limit.IsCancellationRequested && !run.CancellationToken.IsCancellationRequested)
            {
                output.WriteLine("cell backup stopped: the run reached its 6-hour limit");
            }
        }

        try
        {
            await RecordLastSuccessAsync(settings.PlatformConnectionString, ids);
        }
        catch (Exception exception)
        {
            output.WriteLine($"cell backup metric failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }

        return stopped || failed > 0 ? 1 : 0;
    }

    // The gauge of each tenant that has a stored time. It reads without the run's tokens, because the limit must not skip it.
    private static async Task RecordLastSuccessAsync(string connectionString, string[] ids)
    {
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);
        var backups = await platform.TenantBackups.AsNoTracking()
            .Where(backup => ids.Contains(backup.TenantId))
            .ToArrayAsync();
        foreach (var backup in backups)
        {
            AxisMetrics.RecordBackupLastSuccess(backup.TenantId, backup.LastSuccessAt);
        }
    }

    // Upserts the moment the upload succeeded as the last success of the tenant.
    private static async Task StoreLastSuccessAsync(string connectionString, string id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES (@id, @at) "
            + "ON CONFLICT (tenant_id) DO UPDATE SET last_success_at = excluded.last_success_at",
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("at", at);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // One tenant: its audit record once, then up to two tries, stopping at the first success, which stores the upload time.
    private static async Task<bool> BackUpTenantAsync(
        BackupSettings settings,
        TenantSecretCipher cellCipher,
        CellBackupStorage storage,
        IAmazonS3 client,
        TenantAudit audit,
        string id,
        string date,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!await audit.WriteAsync(id, output, cancellationToken))
        {
            return false;
        }

        for (var attempt = 1; attempt <= Attempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (await AttemptAsync(settings, cellCipher, storage, client, id, date, output, cancellationToken))
            {
                try
                {
                    await StoreLastSuccessAsync(settings.PlatformConnectionString, id, DateTimeOffset.UtcNow, cancellationToken);
                    return true;
                }
                catch (Exception exception)
                {
                    output.WriteLine($"{id} record failed: {TenantDatabaseSteps.Safe(exception)}");
                    return false;
                }
            }
        }

        return false;
    }

    // One try for one tenant. It writes a line for each step and returns false at the first failure.
    private static async Task<bool> AttemptAsync(
        BackupSettings settings,
        TenantSecretCipher cellCipher,
        CellBackupStorage storage,
        IAmazonS3 client,
        string id,
        string date,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"axis-cell-backup-{Guid.NewGuid():N}");
        try
        {
            byte[]? key;
            try
            {
                key = await TenantBackupKeys.ReadAsync(settings.PlatformConnectionString, id, cellCipher, cancellationToken);
            }
            catch (Exception exception)
            {
                output.WriteLine($"{id} key failed: {TenantDatabaseSteps.Safe(exception)}");
                return false;
            }

            if (key is null)
            {
                output.WriteLine($"{id} key failed: the tenant has no backup key");
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // The archive holds all of the tenant's business data, so only its owner may use the folder.
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var file = Path.Combine(folder, Archive);
            using var errors = new StringWriter();
            BackupPlan? plan;
            try
            {
                plan = await TenantBackup.PrepareAsync(settings, new TenantBackupRequest(id, file, null), key, errors, cancellationToken);
            }
            catch (Exception exception)
            {
                output.WriteLine($"{id} check failed: {TenantDatabaseSteps.Safe(exception)}");
                return false;
            }

            if (plan is null)
            {
                foreach (var line in errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    output.WriteLine($"{id} check failed: {line}");
                }

                return false;
            }

            if (await new TenantBackup(settings, plan, audit: null, output, cancellationToken).RunAsync() != 0)
            {
                return false;
            }

            try
            {
                await client.PutObjectAsync(
                    new PutObjectRequest { BucketName = storage.Bucket, Key = $"{id}/{date}.axisbak", FilePath = file },
                    cancellationToken);
            }
            catch (Exception exception)
            {
                output.WriteLine($"{id} upload failed: {SafeUpload(exception)}");
                return false;
            }

            output.WriteLine($"{id} upload ok");
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be removed must not hide the result of the tenant.
            }
        }
    }

    // Never ToString(): the message of an upload error can name the service URL or the object key.
    internal static string SafeUpload(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        AmazonServiceException { ErrorCode: { Length: > 0 } code } => code,
        _ => exception.GetType().Name,
    };
}
