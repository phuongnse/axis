using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Administration;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data.Exports;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The settings of the <c>export</c> job: the platform database, the key of the tenant secrets, the installed package versions, the
/// service that decrypts sensitive values and the storage of the exports. A publisher without the storage setting still runs
/// every other job, and only the export refuses with the problem that names the setting. <paramref name="Leases"/> takes the tenant's
/// <c>concurrentImports</c> lease, with the limit from <paramref name="Quotas"/> when the tenant has no row of its own.
/// <paramref name="Quotas"/> is <see langword="null"/> outside a cell, where no quota applies and the export takes no lease.
/// </summary>
internal sealed record TenantExportSettings(
    string PlatformConnectionString,
    TenantSecretCipher Cipher,
    IReadOnlyCollection<PackageVersion> Packages,
    SensitiveValues Sensitive,
    JobSetting<CellBackupStorage> Storage,
    TenantLeases Leases,
    QuotaDefaults? Quotas);

/// <summary>
/// The <c>export</c> job of the publisher, with one <c>export</c> step. It reads every application's active release and its records in one
/// <c>RepeatableRead</c> snapshot of the tenant database, with the sensitive values in clear. It writes a ZIP with <c>export.json</c> and, for
/// each entity that is not a child entity, <c>data/&lt;application&gt;/&lt;entity&gt;.jsonl</c> in the record API shape, with the child rows in
/// each record, and <c>data/&lt;application&gt;/&lt;entity&gt;.csv</c> in the CSV form of entity export. Its columns start with <c>id</c>.
/// The rows of a child collection go to <c>&lt;entity&gt;.&lt;field&gt;.csv</c>, whose columns start with <c>recordId</c>. A character of a name that is not a
/// letter, a digit, <c>.</c>, <c>_</c> or <c>-</c> is written as <c>_</c>, and <c>export.json</c> lists the original name. The ZIP goes to
/// <c>exports/&lt;tenant&gt;/&lt;job id&gt;.zip</c> in the export storage with server-side encryption, so its owner can open it after the download.
/// The ZIP and the CSV parts are written to a folder that only its owner can use, which is deleted at the end. In a cell the job holds one
/// <c>concurrentImports</c> lease of the tenant until the ZIP is stored, and when none is free the job waits, see <see cref="JobStep"/>.
/// </summary>
internal static class TenantExport
{
    private const string Provisioning = "provisioning";

    private static readonly JsonSerializerOptions _manifestJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<JobStep> Steps(TenantExportSettings settings) =>
        [new JobStep(
            "export",
            (jobId, tenantId, _, cancellationToken) => ExportAsync(settings, jobId, tenantId, cancellationToken),
            (tenantId, cancellationToken) => LeaseAsync(settings, tenantId, cancellationToken))];

    /// <summary>A file or folder name for <paramref name="name"/>: every character outside <c>A-Z a-z 0-9 . _ -</c> becomes <c>_</c>.</summary>
    internal static string SafeName(string name)
    {
        var safe = string.Create(name.Length, name, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                span[index] = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-' ? c : '_';
            }
        });
        return safe is "" or "." or ".." ? "_" : safe;
    }

    // The tenant's own limit, read fresh, or the cell's default when it has no row. A tenant with no limit, or a publisher outside a cell, takes no lease.
    // A platform database that cannot be reached throws, so the claim ends and the export never runs without a lease.
    private static async Task<IAsyncDisposable?> LeaseAsync(TenantExportSettings settings, string tenantId, CancellationToken cancellationToken)
    {
        if (settings.Quotas is null)
        {
            return NoLease.Instance;
        }

        TenantQuotaRow? row;
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options))
        {
            row = await platform.TenantQuotas.AsNoTracking()
                .FirstOrDefaultAsync(quota => quota.TenantId == tenantId && quota.Quota == QuotaKeys.ConcurrentImports, cancellationToken);
        }

        if ((row is null ? settings.Quotas[QuotaKeys.ConcurrentImports] : row.Value) is not { } limit)
        {
            return NoLease.Instance;
        }

        return await settings.Leases.TryTakeAsync(tenantId, QuotaKeys.ConcurrentImports, limit, cancellationToken);
    }

    private static async Task<string?> ExportAsync(TenantExportSettings settings, Guid jobId, string tenantId, CancellationToken cancellationToken)
    {
        if (settings.Storage.Problem is { } problem)
        {
            return problem;
        }

        TenantRow? row;
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options))
        {
            row = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
        }

        if (row is null || row.PreviewOf is not null)
        {
            return TenantStatus.UnknownMessage;
        }

        if (row.Status == Provisioning)
        {
            return TenantStatus.ProvisioningMessage;
        }

        // Directory.CreateTempSubdirectory gives a folder that only its owner can use, and the ZIP holds all of the tenant's business data.
        var work = Directory.CreateTempSubdirectory("axis-export-");
        try
        {
            var archive = Path.Combine(work.FullName, "export.zip");
            await WriteArchiveAsync(settings, row, tenantId, work.FullName, archive, cancellationToken);
            using var client = settings.Storage.Value!.NewClient();
            await client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = settings.Storage.Value.Bucket,
                    Key = TenantExports.Key(tenantId, jobId),
                    FilePath = archive,
                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
                },
                cancellationToken);
        }
        finally
        {
            work.Delete(recursive: true);
        }

        return null;
    }

    private static async Task WriteArchiveAsync(TenantExportSettings settings, TenantRow row, string tenantId, string work, string archive, CancellationToken cancellationToken)
    {
        var password = settings.Cipher.Decrypt(tenantId, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = row.DatabaseHost,
            Port = row.DatabasePort,
            Database = row.DatabaseName,
            Username = row.SchemaUser,
            Password = password,
            Pooling = false,
        };
        TenantDatabaseTls.From(settings.PlatformConnectionString).ApplyTo(builder);

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // One snapshot for the releases and every record, so the files agree with each other.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var applications = await ReadApplicationsAsync(settings.Packages, connection, transaction, cancellationToken);
        var cipher = new RecordCipher(settings.Sensitive, tenantId);

        await using var file = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        await WriteManifestAsync(zip, tenantId, applications, cancellationToken);
        var part = 0;
        foreach (var application in applications)
        {
            foreach (var entity in application.Entities)
            {
                await WriteEntityAsync(zip, connection, transaction, cipher, application, entity, work, part++, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // The applications by name, each with its entity files named. A name that is not a safe file name is written with _, and a clash gets _2, _3 and so on.
    private static async Task<List<ExportApplication>> ReadApplicationsAsync(
        IReadOnlyCollection<PackageVersion> packages,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await configuration.Database.UseTransactionAsync(transaction, cancellationToken);
        var store = new ActiveReleaseStore(configuration);

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applications = new List<ExportApplication>();
        foreach (var active in (await store.ListAsync(cancellationToken)).OrderBy(active => active.Name, StringComparer.Ordinal))
        {
            var release = await store.GetReleaseAsync(active.ReleaseId, cancellationToken)
                ?? throw new InvalidOperationException("The active release of an application was not found.");
            var model = ReleaseCompiler.BuildModel(release, packages);

            int? number = null;
            await using (var command = new NpgsqlCommand(
                "SELECT number FROM axis.release_activations WHERE application_id = @application AND release_id = @release ORDER BY number DESC LIMIT 1",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("application", active.ApplicationId);
                command.Parameters.AddWithValue("release", active.ReleaseId);
                number = await command.ExecuteScalarAsync(cancellationToken) as int?;
            }

            var folder = Unique(SafeName(active.Name), folders);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entities = new List<ExportEntity>();
            foreach (var entity in model.Entities.Where(entity => !model.IsChildEntity(entity)))
            {
                var collections = new List<ExportCollection>();
                var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in entity.Fields.Where(field => field.Type == FieldType.ChildCollection))
                {
                    if (field.Target is { } target && model.FindEntity(target.Id) is { } child)
                    {
                        collections.Add(new ExportCollection(field, child, Unique(SafeName(field.Name), parts)));
                    }
                }

                entities.Add(new ExportEntity(entity, ReserveStem(SafeName(entity.Name), collections, files), collections));
            }

            applications.Add(new ExportApplication(active.Name, folder, number, model, entities));
        }

        return applications;
    }

    private static string Unique(string safe, HashSet<string> used)
    {
        var name = safe;
        for (var number = 2; !used.Add(name); number++)
        {
            name = $"{safe}_{number.ToString(CultureInfo.InvariantCulture)}";
        }

        return name;
    }

    // The first stem whose files, the JSONL, the CSV and one CSV for each child collection, are all unused in the application.
    private static string ReserveStem(string safe, List<ExportCollection> collections, HashSet<string> used)
    {
        for (var number = 1; ; number++)
        {
            var stem = number == 1 ? safe : $"{safe}_{number.ToString(CultureInfo.InvariantCulture)}";
            var files = new List<string> { $"{stem}.jsonl", $"{stem}.csv" };
            files.AddRange(collections.Select(collection => $"{stem}.{collection.Part}.csv"));
            if (files.All(file => !used.Contains(file)))
            {
                used.UnionWith(files);
                return stem;
            }
        }
    }

    private static async Task WriteManifestAsync(ZipArchive zip, string tenantId, List<ExportApplication> applications, CancellationToken cancellationToken)
    {
        var manifest = new
        {
            format = 1,
            tenantId,
            createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            axisVersion = TenantBackup.AxisVersion(),
            applications = applications.Select(application => new
            {
                name = application.Name,
                folder = application.Folder,
                release = application.Release,
                entities = application.Entities.Select(entity => new { name = entity.Entity.Name, file = entity.Stem }),
            }),
        };

        await using var stream = zip.CreateEntry("export.json", CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(stream, manifest, _manifestJson, cancellationToken);
    }

    // The JSONL entry is written while the records are read, and the CSV parts go to files, because a ZIP has one open entry at a time.
    private static async Task WriteEntityAsync(
        ZipArchive zip,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RecordCipher cipher,
        ExportApplication application,
        ExportEntity export,
        string work,
        int part,
        CancellationToken cancellationToken)
    {
        var entity = export.Entity;
        var columns = entity.Fields.Where(field => field.HasColumn).ToList();
        var folder = $"data/{application.Folder}";

        using var entityCsv = new CsvPart(
            Path.Combine(work, $"{part}.csv"),
            $"{folder}/{export.Stem}.csv",
            [new ExportColumn("id", FieldType.Text), .. columns.Select(field => new ExportColumn(field.Name, field.Type))]);
        var collections = export.Collections.Select((collection, index) =>
        {
            var childColumns = collection.Child.Fields.Where(field => field.HasColumn).ToList();
            var csv = new CsvPart(
                Path.Combine(work, $"{part}.{index.ToString(CultureInfo.InvariantCulture)}.csv"),
                $"{folder}/{export.Stem}.{collection.Part}.csv",
                [new ExportColumn("recordId", FieldType.Text), .. childColumns.Select(field => new ExportColumn(field.Name, field.Type))]);
            return (collection, childColumns, csv);
        }).ToList();
        try
        {
            var ids = new List<Guid>();
            var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
            await using (var command = new NpgsqlCommand(
                $"SELECT {idColumn} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} ORDER BY {idColumn}",
                connection,
                transaction))
            {
                // Npgsql allows one open reader per connection, so the ids are read before the records.
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetGuid(0));
                }
            }

            await using (var stream = zip.CreateEntry($"{folder}/{export.Stem}.jsonl", CompressionLevel.Optimal).Open())
            await using (var lines = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\n" })
            {
                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Read as the record GET route does: a computed field that is pending shows as the active release computes it.
                    if (await RecordQueries.GetAsync(connection, application.Model, entity, id, cipher, cancellationToken) is not { } record)
                    {
                        continue;
                    }

                    record = await PendingComputedReads.ApplyAsync(connection, transaction, application.Model, entity, record, cancellationToken, cipher: cipher);
                    await lines.WriteLineAsync(JsonSerializer.Serialize(record, JsonSerializerOptions.Web));

                    entityCsv.Writer.WriteRow([record.Id.ToString("D"), .. columns.Select(field => Cell(field, record.Values, record.Labels))]);
                    foreach (var (collection, childColumns, csv) in collections)
                    {
                        var rows = record.Values.GetValueOrDefault(collection.Field.Name) as JsonArray ?? [];
                        var labels = record.Labels.GetValueOrDefault(collection.Field.Name) as JsonArray;
                        for (var index = 0; index < rows.Count; index++)
                        {
                            if (rows[index] is JsonObject row)
                            {
                                var rowLabels = labels?.ElementAtOrDefault(index) as JsonObject;
                                csv.Writer.WriteRow([record.Id.ToString("D"), .. childColumns.Select(field => ExportValues.Format(field.Type, row[field.Name], rowLabels?[field.Name]))]);
                            }
                        }
                    }
                }
            }

            await CopyAsync(zip, entityCsv, cancellationToken);
            foreach (var (_, _, csv) in collections)
            {
                await CopyAsync(zip, csv, cancellationToken);
            }
        }
        finally
        {
            foreach (var (_, _, csv) in collections)
            {
                csv.Dispose();
            }
        }
    }

    private static string? Cell(FieldModel field, IReadOnlyDictionary<string, JsonNode?> values, IReadOnlyDictionary<string, JsonNode> labels) =>
        ExportValues.Format(field.Type, values.GetValueOrDefault(field.Name), labels.GetValueOrDefault(field.Name));

    private static async Task CopyAsync(ZipArchive zip, CsvPart part, CancellationToken cancellationToken)
    {
        part.Complete();
        await using var source = new FileStream(part.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        await using var target = zip.CreateEntry(part.EntryName, CompressionLevel.Optimal).Open();
        await source.CopyToAsync(target, cancellationToken);
    }

    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record ExportApplication(string Name, string Folder, int? Release, ApplicationModel Model, List<ExportEntity> Entities);

    private sealed record ExportEntity(EntityModel Entity, string Stem, List<ExportCollection> Collections);

    private sealed record ExportCollection(FieldModel Field, EntityModel Child, string Part);

    // A CSV file that is written beside the ZIP and copied into it, with its header already written.
    private sealed class CsvPart : IDisposable
    {
        private readonly FileStream _stream;
        private bool _disposed;

        public CsvPart(string path, string entryName, IReadOnlyList<ExportColumn> columns)
        {
            Path = path;
            EntryName = entryName;
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Writer = new CsvExportWriter(_stream);
            Writer.WriteHeader(columns);
        }

        public string Path { get; }

        public string EntryName { get; }

        public CsvExportWriter Writer { get; }

        public void Complete()
        {
            Writer.Complete();
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Writer.Dispose();
            _stream.Dispose();
        }
    }
}
