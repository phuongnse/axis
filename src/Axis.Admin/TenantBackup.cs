using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Configuration.Loading;
using Axis.Processes.Instances;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant backup</c>: dumps the tenant database with <c>pg_dump</c> as its schema role and writes the dump and a manifest
/// into one tar archive. The manifest is read inside the database snapshot that <c>pg_dump</c> uses, so it always matches the
/// dump. Each step writes one line that names the tenant and never a password, a host or a connection string. The archive
/// holds no secret: the manifest is built from explicit fields, and the binding secrets are never read.
/// </summary>
internal sealed class TenantBackup : IAsyncDisposable
{
    private const string Provisioning = "provisioning";

    private const string PgDump = "pg_dump";

    private const string ManifestEntry = "manifest.json";

    private const string DumpEntry = "tenant.dump";

    private static readonly UnixFileMode _ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly BackupSettings _settings;
    private readonly BackupPlan _plan;
    private readonly TextWriter _output;
    private readonly CancellationToken _cancellationToken;
    private readonly string _id;
    private readonly string _archiveTemp;
    private readonly string _dumpTemp;

    private List<ManifestPackage> _packages = [];
    private List<ManifestModule> _modules = [];

    public TenantBackup(BackupSettings settings, BackupPlan plan, TextWriter output, CancellationToken cancellationToken)
    {
        _settings = settings;
        _plan = plan;
        _output = output;
        _cancellationToken = cancellationToken;
        _id = plan.Row.Id;
        var random = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        _archiveTemp = $"{plan.OutPath}.{random}.tmp";
        _dumpTemp = $"{plan.OutPath}.{random}.dump.tmp";
    }

    /// <summary>
    /// Checks everything that can be checked before any tenant work: <c>pg_dump</c>, the platform tables, the tenant, its
    /// status and <c>--out</c>. It writes the reason to <paramref name="error"/> and returns null when the command must exit 2.
    /// </summary>
    public static async Task<BackupPlan?> PrepareAsync(
        BackupSettings settings,
        TenantBackupRequest request,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var pgDump = AdminCommands.FindTool(settings.SearchPath, PgDump);
        if (pgDump is null)
        {
            error.WriteLine($"{PgDump} was not found in the directories of {AdminCommands.PathSetting}.");
            return null;
        }

        var problem = await AdminCommands.PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);
        if (problem is not null)
        {
            error.WriteLine(problem);
            return null;
        }

        TenantRow? row;
        string[] hosts;
        BackupBinding[] bindings;
        try
        {
            await using var platform = NewPlatform(settings.PlatformConnectionString);
            row = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == request.Id, cancellationToken);
            if (row is null)
            {
                error.WriteLine("The tenant does not exist.");
                return null;
            }

            hosts = await platform.TenantHosts.AsNoTracking()
                .Where(host => host.TenantId == request.Id)
                .Select(host => host.Host)
                .ToArrayAsync(cancellationToken);

            // The projection never reads SecretValue, so a secret binding value is never in memory here.
            bindings = await platform.TenantBindings.AsNoTracking()
                .Where(binding => binding.TenantId == request.Id)
                .Select(binding => new BackupBinding(binding.Name, binding.Kind, binding.Url))
                .ToArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return null;
        }

        if (row.Status == Provisioning)
        {
            error.WriteLine("The tenant is still provisioning, so its database is not complete. Finish tenant create first.");
            return null;
        }

        var outPath = Path.GetFullPath(request.Out);
        if (File.Exists(outPath) || Directory.Exists(outPath))
        {
            error.WriteLine("The --out file already exists.");
            return null;
        }

        if (!Directory.Exists(Path.GetDirectoryName(outPath)))
        {
            error.WriteLine("The folder of --out does not exist.");
            return null;
        }

        return new BackupPlan(row, hosts, bindings, pgDump, outPath);
    }

    public async Task<int> RunAsync()
    {
        try
        {
            return await Step("dump", DumpAsync) && await Step("archive", ArchiveAsync) ? 0 : 1;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        Delete(_dumpTemp);
        Delete(_archiveTemp);
        return ValueTask.CompletedTask;
    }

    private static PlatformDbContext NewPlatform(string connectionString) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp file that cannot be removed must not hide the result of the command.
        }
    }

    private static FileStreamOptions Create(FileAccess access = FileAccess.Write)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = access, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            // The archive holds all of the tenant's business data, so only its owner may read it.
            options.UnixCreateMode = _ownerOnly;
        }

        return options;
    }

    // Reads the manifest data and runs pg_dump inside one exported snapshot, so both see the same database state.
    private async Task DumpAsync()
    {
        var row = _plan.Row;
        var password = _settings.Cipher.Decrypt(_id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = row.DatabaseHost,
            Port = row.DatabasePort,
            Database = row.DatabaseName,
            Username = row.SchemaUser,
            Password = password,
            Pooling = false,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(_cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, _cancellationToken);

        string snapshot;
        await using (var export = new NpgsqlCommand("SELECT pg_export_snapshot()", connection, transaction))
        {
            snapshot = (string)(await export.ExecuteScalarAsync(_cancellationToken))!;
        }

        _packages = await ReadPackagesAsync(connection, transaction);
        _modules = await ReadModulesAsync(connection, transaction);
        await RunPgDumpAsync(snapshot, password);
        await transaction.CommitAsync(_cancellationToken);
    }

    private async Task<List<ManifestPackage>> ReadPackagesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT p.package_id, p.version,
                   bool_or(
                       EXISTS (SELECT 1 FROM axis.active_releases a WHERE a.release_id = p.release_id)
                       OR EXISTS (
                           SELECT 1 FROM axis.process_instances i
                           WHERE i.release_id = p.release_id AND i.state IN (@running, @waiting)))
            FROM axis.release_packages p
            GROUP BY p.package_id, p.version
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);

        var packages = new List<ManifestPackage>();
        await using var reader = await command.ExecuteReaderAsync(_cancellationToken);
        while (await reader.ReadAsync(_cancellationToken))
        {
            packages.Add(new ManifestPackage(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        }

        return [.. packages.OrderBy(package => package.Id, StringComparer.Ordinal).ThenBy(package => package.Version, StringComparer.Ordinal)];
    }

    private async Task<List<ManifestModule>> ReadModulesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT path, content FROM axis.release_resources WHERE path LIKE 'modules/%/module.json'",
            connection,
            transaction);

        var modules = new HashSet<ManifestModule>();
        await using var reader = await command.ExecuteReaderAsync(_cancellationToken);
        while (await reader.ReadAsync(_cancellationToken))
        {
            // The pattern also matches a deeper file such as modules/a/b/module.json, which is not a module manifest.
            var segments = reader.GetString(0).Split('/');
            if (segments.Length != 3 || segments[0] + "/" != ApplicationLoader.ModulesFolderPrefix)
            {
                continue;
            }

            using var document = JsonDocument.Parse(reader.GetString(1));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("a stored module manifest has no id or version");
            }

            modules.Add(new ManifestModule(id.GetString()!, version.GetString()!));
        }

        return [.. modules.OrderBy(module => module.Id, StringComparer.Ordinal).ThenBy(module => module.Version, StringComparer.Ordinal)];
    }

    private async Task RunPgDumpAsync(string snapshot, string password)
    {
        var row = _plan.Row;
        var info = new ProcessStartInfo(_plan.PgDump)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Arguments show in the process list of every user, so the password goes in the environment of the child only.
        info.ArgumentList.Add("--format=custom");
        info.ArgumentList.Add("--no-owner");
        info.ArgumentList.Add("--no-privileges");
        info.ArgumentList.Add($"--snapshot={snapshot}");
        info.Environment["PGHOST"] = row.DatabaseHost;
        info.Environment["PGPORT"] = row.DatabasePort.ToString(CultureInfo.InvariantCulture);
        info.Environment["PGDATABASE"] = row.DatabaseName;
        info.Environment["PGUSER"] = row.SchemaUser;
        info.Environment["PGPASSWORD"] = password;
        info.Environment.Remove("PGPASSFILE");
        info.Environment.Remove("PGSERVICE");

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{PgDump} could not start");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"{PgDump} could not start");
        }

        using (process)
        {
            try
            {
                // pg_dump's own messages can name the database host, so stderr is drained and never printed.
                var drained = process.StandardError.BaseStream.CopyToAsync(Stream.Null, _cancellationToken);
                await using (var file = new FileStream(_dumpTemp, Create()))
                {
                    await process.StandardOutput.BaseStream.CopyToAsync(file, _cancellationToken);
                }

                await drained;
                await process.WaitForExitAsync(_cancellationToken);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{PgDump} exited with code {process.ExitCode}");
            }
        }
    }

    private async Task ArchiveAsync()
    {
        var manifest = JsonSerializer.SerializeToUtf8Bytes(BuildManifest(), _json);
        await using (var archive = new FileStream(_archiveTemp, Create()))
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax))
        {
            await writer.WriteEntryAsync(NewEntry(ManifestEntry, new MemoryStream(manifest)), _cancellationToken);
            await using var dump = new FileStream(_dumpTemp, FileMode.Open, FileAccess.Read, FileShare.Read);
            await writer.WriteEntryAsync(NewEntry(DumpEntry, dump), _cancellationToken);
        }

        // Moving without overwrite keeps an --out that appeared during the backup.
        File.Move(_archiveTemp, _plan.OutPath, overwrite: false);
    }

    private static PaxTarEntry NewEntry(string name, Stream data) =>
        new(TarEntryType.RegularFile, name) { DataStream = data, Mode = _ownerOnly };

    private BackupManifest BuildManifest()
    {
        var row = _plan.Row;
        var directory = new ManifestDirectory(
            row.Status,
            row.PreviewOf,
            [.. _plan.Hosts.Order(StringComparer.Ordinal)],
            row.OidcAuthority,
            row.OidcClientId,
            row.ClaimUserId,
            row.ClaimDisplayName,
            row.ClaimRoles,
            row.EndpointAudience,
            row.ServiceAudience,
            [.. _plan.Bindings
                .OrderBy(binding => binding.Name, StringComparer.Ordinal)
                .Select(binding => new ManifestBinding(binding.Name, binding.Kind, binding.Kind == "url" ? binding.Url : null))]);

        return new BackupManifest(
            Format: 1,
            _id,
            AxisVersion(),
            row.SchemaVersion,
            DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            directory,
            _packages,
            _modules);
    }

    private static string AxisVersion()
    {
        var assembly = typeof(TenantBackup).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    // Runs one step. Any failure, also a stop by Ctrl+C, writes a failed line and ends the command.
    private async Task<bool> Step(string name, Func<Task> work)
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            await work();
        }
        catch (Exception exception)
        {
            Failed(name, Safe(exception));
            return false;
        }

        Ok(name);
        return true;
    }

    // Never ToString(): an Npgsql message can name the host.
    private static string Safe(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        InvalidOperationException invalid => invalid.Message,
        PostgresException postgres => postgres.MessageText,
        _ => exception.GetType().Name,
    };

    private void Ok(string step) => _output.WriteLine($"{_id} {step} ok");

    private void Failed(string step, string message) => _output.WriteLine($"{_id} {step} failed: {message}");
}

/// <summary>What the checks before the backup found: the directory entry, with no secret binding value, and the resolved paths.</summary>
internal sealed record BackupPlan(TenantRow Row, string[] Hosts, BackupBinding[] Bindings, string PgDump, string OutPath);

/// <summary>A binding of the tenant as the backup reads it. A <c>secret</c> binding has no value here.</summary>
internal sealed record BackupBinding(string Name, string Kind, string? Url);

/// <summary>The content of <c>manifest.json</c>. Each field is named here, so a new directory column never reaches the archive by accident.</summary>
internal sealed record BackupManifest(
    int Format,
    string TenantId,
    string AxisVersion,
    string SchemaVersion,
    string CreatedAt,
    ManifestDirectory Directory,
    IReadOnlyList<ManifestPackage> Packages,
    IReadOnlyList<ManifestModule> Modules);

internal sealed record ManifestDirectory(
    string Status,
    string? PreviewOf,
    IReadOnlyList<string> Hosts,
    string OidcAuthority,
    string OidcClientId,
    string? ClaimUserId,
    string? ClaimDisplayName,
    string? ClaimRoles,
    string? EndpointAudience,
    string? ServiceAudience,
    IReadOnlyList<ManifestBinding> Bindings);

/// <summary>A binding in the manifest. <c>Url</c> is set only for a <c>url</c> binding.</summary>
internal sealed record ManifestBinding(string Name, string Kind, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);

internal sealed record ManifestPackage(string Id, string Version, bool Required);

internal sealed record ManifestModule(string Id, string Version);
