using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Configuration.Realms;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;
using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant restore</c>: checks the archive, writes a directory entry as <c>provisioning</c>, creates the database roles and
/// the database, restores the dump with <c>pg_restore</c> as the schema role, grants and migrates it, gives every work item
/// the new tenant id, empties the sessions, checks the runtime role and sets the entry <c>suspended</c>. When the archive's
/// tenant signed in through a realm that this installation's identity provider does not have, the restored tenant gets a new
/// realm in its own <c>realm</c> step, in step with its active releases. Otherwise it keeps the archive's sign-in. A rerun on a
/// <c>provisioning</c> entry skips what is done, and a database that already holds tables counts as restored. Each step writes one line that names the tenant and never a password, a database host or a
/// connection string. The archive is encrypted with the operator's backup key, see <see cref="BackupArchive"/>. The <c>check</c> step
/// decrypts it as far as the manifest and the start of the dump. The <c>restore</c> step decrypts it to the end, so a chunk that is changed
/// or missing anywhere fails that step. The manifest's data keys and lookup keys come back under this installation's key, in the entry step.
/// The dump goes to <c>pg_restore</c> on its standard input, so no copy of the business data is left on disk.
/// </summary>
internal sealed class TenantRestore : IAsyncDisposable
{
    public const string PgRestore = "pg_restore";

    private const string Provisioning = "provisioning";

    private const string ManifestEntry = "manifest.json";

    private const string DumpEntry = "tenant.dump";

    private const int SupportedFormat = 1;

    private const long MaxManifestBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly CreateSettings _settings;
    private readonly RestorePlan _plan;
    private readonly TenantAudit _audit;
    private readonly TenantRealm? _realm;
    private readonly HttpClient _http;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly CancellationToken _cancellationToken;
    private readonly string _id;
    private readonly PlatformDbContext _platform;
    private readonly NpgsqlConnectionStringBuilder _admin;

    private bool _newRealm;

    private TenantRow? _row;
    private TenantProvisioning? _provisioning;
    private TenantDatabaseSteps? _steps;

    public TenantRestore(
        CreateSettings settings,
        RestorePlan plan,
        TenantRealm? realm,
        HttpClient http,
        TenantAudit audit,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        _settings = settings;
        _plan = plan;
        _realm = realm;
        _http = http;
        _audit = audit;
        _output = output;
        _error = error;
        _newRealm = plan.NewRealm;
        _cancellationToken = cancellationToken;
        _id = plan.Request.Id;
        _admin = settings.Admin;
        _platform = NewPlatform(settings.PlatformConnectionString);
    }

    /// <summary>
    /// The <c>check</c> step: reads the archive and checks everything that can be checked before any tenant work. It writes one
    /// <c>check</c> line and returns null when the command must exit 2. It asks the identity provider whether the realm of the archive
    /// exists here. When the restore keeps the archive's sign-in, it also needs <c>AXIS_OIDC_CLIENT_SECRET</c>, and a missing one is
    /// written to <paramref name="error"/> before any <c>check ok</c> line.
    /// </summary>
    public static async Task<RestorePlan?> PrepareAsync(
        RestoreSettings settings,
        TenantRestoreRequest request,
        byte[] backupKey,
        string pgRestore,
        TenantRealm? realm,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var refusal = await CheckAsync(settings, request, backupKey, realm, cancellationToken);
        if (refusal.Problems.Count > 0)
        {
            foreach (var problem in refusal.Problems)
            {
                output.WriteLine($"{request.Id} check failed: {problem}");
            }

            return null;
        }

        if (!refusal.NewRealm && string.IsNullOrEmpty(settings.Create.OidcClientSecret))
        {
            error.WriteLine($"The environment variable {AdminCommands.ClientSecretSetting} is required.");
            return null;
        }

        output.WriteLine($"{request.Id} check ok");
        return new RestorePlan(request with { File = Path.GetFullPath(request.File) }, refusal.Manifest!, backupKey, pgRestore, refusal.NewRealm);
    }

    public async Task<int> RunAsync()
    {
        try
        {
            var manifest = _plan.Manifest;
            if (!await _audit.WriteAsync(_id, _output, _cancellationToken))
            {
                return 1;
            }

            var entry = await EntryAsync();
            if (entry is not null)
            {
                return entry.Value;
            }

            var older = string.CompareOrdinal(manifest.SchemaVersion, TenantSchemaVersion.Current) < 0;
            if (await Step("roles", () => Roles.EnsureRolesAsync())
                && await Step("database", () => Roles.EnsureDatabaseAsync())
                && await Step("restore", RestoreAsync)
                && await Step("grants", () => Steps.GrantAsync())
                && (!older || await Step("migrations", () => Steps.MigrateAsync()))
                && await Step("grants", () => Steps.GrantAsync())
                && await Step("work", RetagWorkAsync)
                && await Step("sessions", EmptySessionsAsync)
                && await Step("role-check", () => Steps.CheckRuntimeRoleAsync())
                && (!_newRealm || await Step("realm", RealmAsync))
                && await Step("version", SuspendAsync))
            {
                return 0;
            }

            return 1;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provisioning is not null)
        {
            await _provisioning.DisposeAsync();
            _provisioning = null;
        }

        if (_steps is not null)
        {
            await _steps.DisposeAsync();
            _steps = null;
        }

        await _platform.DisposeAsync();
    }

    private static PlatformDbContext NewPlatform(string connectionString) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<CheckResult> CheckAsync(
        RestoreSettings settings, TenantRestoreRequest request, byte[] backupKey, TenantRealm? realm, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var (manifest, readProblem) = ReadManifest(request.File, backupKey);
        var trusted = false;
        var preview = false;
        if (manifest is null)
        {
            problems.Add(readProblem);
        }
        else if (manifest.Format != SupportedFormat)
        {
            problems.Add($"the archive format {manifest.Format.ToString(CultureInfo.InvariantCulture)} is not known");
        }
        else
        {
            trusted = true;
            var version = manifest.SchemaVersion;
            if (string.IsNullOrEmpty(version))
            {
                problems.Add("the archive has no schema version");
            }
            else if (string.CompareOrdinal(version, TenantSchemaVersion.Current) > 0)
            {
                problems.Add($"the schema version {Printable(version)} of the archive is newer than this build's");
            }
            else if (!TenantSchemaVersion.IsKnown(version))
            {
                problems.Add($"the schema version {Printable(version)} of the archive is not one of this build");
            }

            // Preview data is disposable. An archive from before previewOf is recognised by its empty sign-in settings, which only a preview entry has.
            var directory = manifest.Directory;
            preview = directory.PreviewOf is not null || (directory.OidcAuthority.Length == 0 && directory.OidcClientId.Length == 0);
            if (!preview)
            {
                if (!Uri.TryCreate(directory.OidcAuthority, UriKind.Absolute, out var authority)
                    || (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps))
                {
                    problems.Add("the OIDC authority of the archive is not an absolute http or https URL");
                }

                if (string.IsNullOrWhiteSpace(directory.OidcClientId))
                {
                    problems.Add("the OIDC client id of the archive is blank");
                }
            }
        }

        var typed = request.Hosts.Select(host => host.Trim()).ToArray();
        var hosts = typed.Select(host => host.ToLowerInvariant()).ToArray();
        try
        {
            await using var platform = NewPlatform(settings.Create.PlatformConnectionString);
            var status = await platform.Tenants.AsNoTracking()
                .Where(tenant => tenant.Id == request.Id)
                .Select(tenant => tenant.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status is not null && status != Provisioning)
            {
                problems.Add($"the tenant {request.Id} already exists");
            }

            var held = await platform.TenantHosts.AsNoTracking()
                .Where(host => host.TenantId != request.Id && hosts.Contains(host.Host.ToLower()))
                .Select(host => host.Host.ToLower())
                .ToListAsync(cancellationToken);
            if (held.Count > 0)
            {
                var names = typed
                    .Where(host => held.Contains(host.ToLowerInvariant()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal);
                problems.Add("a host belongs to another tenant: " + string.Join(", ", names));
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            problems.Add($"the setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory");
        }

        if (preview)
        {
            problems.Add("the archive is of a preview tenant, so create the preview again with tenant create --preview-of");
        }

        // A tenant that signed in through its own realm gets a new one where that realm does not exist. Any other archive keeps its sign-in.
        var newRealm = false;
        if (trusted && !preview && manifest!.Directory.Realm)
        {
            if (realm is null)
            {
                problems.Add($"the setting {AdminCommands.IdentityProviderUrlSetting} is required, to find out whether the realm of the archive exists");
            }
            else
            {
                try
                {
                    newRealm = !await RealmExistsAsync(realm, manifest, cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException
                    || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    problems.Add($"the identity provider could not tell whether the realm of the archive exists: {Safe(exception)}");
                }
            }
        }

        if (trusted)
        {
            var required = manifest!.Packages.Where(package => package.Required).ToList();
            if (required.Count > 0)
            {
                PackageCatalog? catalog = null;
                try
                {
                    catalog = PackageCatalog.Load(settings.ExtensionsPath);
                }
                catch (InvalidOperationException)
                {
                    problems.Add($"the setting {PackageCatalog.PathSetting} could not be used to find the required extension packages");
                }

                if (catalog is not null)
                {
                    var missing = required.Where(package => catalog.Find(package.Id, package.Version) is null).ToList();
                    if (missing.Count > 0)
                    {
                        problems.Add(
                            "missing required extension packages: "
                            + string.Join(", ", missing.Select(package => $"{Printable(package.Id)} {Printable(package.Version)}")));
                    }
                }
            }
        }

        return new CheckResult(manifest, problems, newRealm);
    }

    // The realm of the archive is the realm its authority names, on this installation's identity provider.
    private static async Task<bool> RealmExistsAsync(TenantRealm realm, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var archiveBase = TenantRealmAddress.BaseOf(manifest.Directory.OidcAuthority, manifest.TenantId);
        return archiveBase is null || (archiveBase == realm.BaseAddress && await realm.ExistsAsync(manifest.TenantId, cancellationToken));
    }

    // Text from the archive goes into a message, so a control character cannot start a fake output line.
    private static string Printable(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
            }
        });

    // Decrypts the archive as far as the manifest and the start of the dump. Returns the manifest, or null and the reason when the file is
    // not an encrypted archive that the key opens, with a complete manifest and a dump. Chunks after that are checked by the restore step.
    private static (BackupManifest? Manifest, string Problem) ReadManifest(string file, byte[] key)
    {
        const string Unreadable = "the archive cannot be read";
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var decrypted = BackupArchive.Decrypt(stream, key);
            using var reader = new TarReader(decrypted);
            BackupManifest? manifest = null;
            var hasDump = false;
            while ((manifest is null || !hasDump) && reader.GetNextEntry() is { } entry)
            {
                if (entry.Name == ManifestEntry && entry.DataStream is { } data && manifest is null)
                {
                    if (entry.Length > MaxManifestBytes)
                    {
                        return (null, Unreadable);
                    }

                    manifest = JsonSerializer.Deserialize<BackupManifest>(data, _json);
                }
                else if (entry.Name == DumpEntry && entry.DataStream is not null)
                {
                    hasDump = true;
                }
            }

            return hasDump && manifest is not null && IsComplete(manifest) ? (manifest, "") : (null, Unreadable);
        }
        catch (BackupArchiveException exception)
        {
            return (null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException
            or JsonException or NotSupportedException or ArgumentException)
        {
            return (null, Unreadable);
        }
    }

    // A missing field deserializes to null. An archive that this tool did not write can be missing any of them.
    private static bool IsComplete(BackupManifest manifest) =>
        manifest.SchemaVersion is not null
        && manifest.Directory is { OidcAuthority: not null, OidcClientId: not null, Bindings: not null }
        && manifest.Packages is not null
        && manifest.Packages.All(package => package is { Id: not null, Version: not null })
        && manifest.Directory.Bindings.All(binding => binding is { Name: not null, Kind: "url" or "secret" or "storage" } && (binding.Kind != "url" || binding.Url is not null) && (binding.Kind != "storage" || binding.Url == StorageBindingValues.Database))
        && manifest.Directory.Bindings.Select(binding => binding.Name.ToLowerInvariant()).Distinct().Count() == manifest.Directory.Bindings.Count
        && (manifest.Directory.Sites is null || (manifest.Directory.Sites.All(site => site is { Host: not null } && SitePaths.IsValid(site.Path))
            && manifest.Directory.Sites.Select(site => site.Path.ToLowerInvariant()).Distinct().Count() == manifest.Directory.Sites.Count))
        && (manifest.Keys is null || (manifest.Keys.All(IsKey)
            && manifest.Keys.Select(key => (key.Kind, key.Number)).Distinct().Count() == manifest.Keys.Count));

    private static bool IsKey(ManifestKey? key)
    {
        if (key is not { Kind: TenantKeyKinds.Data or TenantKeyKinds.Lookup, Number: >= 1, Key: not null })
        {
            return false;
        }

        var buffer = new byte[BackupArchive.KeySize + 4];
        return Convert.TryFromBase64String(key.Key, buffer, out var length) && length == BackupArchive.KeySize;
    }

    // Returns the exit code that ends the command, or null to go on.
    private async Task<int?> EntryAsync()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var hosts = _plan.Request.Hosts
                .Select(host => host.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var keys = hosts.Select(host => host.ToLowerInvariant()).ToArray();
            var directory = _plan.Manifest.Directory;

            // A typed host that matches a site host of the archive, ignoring letter case, gets that site. Other hosts get none.
            var sitesByHost = (directory.Sites ?? []).ToDictionary(site => site.Host, site => site.Path, StringComparer.OrdinalIgnoreCase);

            _row = await _platform.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == _id, _cancellationToken);
            if (_row is not null && _row.Status != Provisioning)
            {
                Failed("entry", "the tenant already exists");
                return 2;
            }

            // A realm that an earlier run made is this tenant's own, so a rerun goes on with it, also when the archive's realm bears the same name.
            if (_row?.RealmActivationClientSecret is { Length: > 0 })
            {
                _newRealm = true;
            }

            var taken = await _platform.TenantHosts.AnyAsync(
                host => host.TenantId != _id && keys.Contains(host.Host.ToLower()),
                _cancellationToken);
            if (taken)
            {
                Failed("entry", "a host belongs to another tenant");
                return 2;
            }

            await using var transaction = await _platform.Database.BeginTransactionAsync(_cancellationToken);
            if (_row is null)
            {
                // A new realm gives the sign-in settings in the realm step, as tenant create does.
                _row = _newRealm
                    ? new TenantRow { Id = _id, Status = Provisioning, OidcAuthority = "", OidcClientId = "", OidcClientSecret = [], PlatformAccounts = true }
                    : new TenantRow { Id = _id, Status = Provisioning, PlatformAccounts = true };
                _platform.Tenants.Add(_row);
            }
            else
            {
                // A provisioning tenant is never served, so a rerun can fix the hosts. The bindings come from the archive again.
                _row.Version++;
                _platform.TenantHosts.RemoveRange(_platform.TenantHosts.Where(host => host.TenantId == _id));
                _platform.TenantBindings.RemoveRange(_platform.TenantBindings.Where(binding => binding.TenantId == _id));
                _platform.TenantDataKeys.RemoveRange(_platform.TenantDataKeys.Where(key => key.TenantId == _id));
                await _platform.SaveChangesAsync(_cancellationToken);
            }

            _row.DatabaseHost = _admin.Host!;
            _row.DatabasePort = _admin.Port;
            _row.DatabaseName = $"axis_{_id}";
            _row.SchemaUser = $"axis_{_id}_schema";
            _row.RuntimeUser = $"axis_{_id}_runtime";
            if (!_newRealm)
            {
                _row.OidcAuthority = directory.OidcAuthority;
                _row.OidcClientId = directory.OidcClientId;
                _row.OidcClientSecret = _settings.Cipher.Encrypt(_id, TenantSecretFields.OidcClientSecret, _settings.OidcClientSecret!);
            }

            _row.ClaimUserId = directory.ClaimUserId;
            _row.ClaimDisplayName = directory.ClaimDisplayName;
            _row.ClaimRoles = directory.ClaimRoles;
            _row.ClaimEmail = directory.ClaimEmail;
            _row.ClaimLocale = directory.ClaimLocale;
            _row.EndpointAudience = directory.EndpointAudience;
            _row.ServiceAudience = directory.ServiceAudience;
            _platform.TenantHosts.AddRange(hosts.Select(host => new TenantHostRow { TenantId = _id, Host = host, SitePath = sitesByHost.GetValueOrDefault(host)?.ToLowerInvariant() }));

            // A secret binding comes back by name and kind only: the archive holds no value. A url or storage binding comes back with its value.
            _platform.TenantBindings.AddRange(directory.Bindings.Select(binding => new TenantBindingRow
            {
                TenantId = _id,
                Name = binding.Name,
                Kind = binding.Kind,
                Url = binding.Kind == "secret" ? null : binding.Url,
                SecretValue = null,
            }));

            // The tenant keeps the archive's keys with their numbers, under this installation's key. An archive with none gets number 1 of each kind.
            var archiveKeys = _plan.Manifest.Keys ?? [];
            _platform.TenantDataKeys.AddRange(archiveKeys.Select(key => new TenantDataKeyRow
            {
                TenantId = _id,
                Kind = key.Kind,
                Number = key.Number,
                Key = _settings.Cipher.Encrypt(_id, key.Kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(key.Number) : TenantSecretFields.LookupKey(key.Number), key.Key),
            }));
            if (archiveKeys.Count == 0)
            {
                await TenantKeyRows.AddMissingAsync(_platform, _id, _settings.Cipher, _cancellationToken);
            }

            await _platform.SaveChangesAsync(_cancellationToken);
            await transaction.CommitAsync(_cancellationToken);

            Ok("entry");
            return null;
        }
        catch (Exception exception) when (IsHostConflict(exception))
        {
            Failed("entry", "a host belongs to another tenant");
            return 2;
        }
        catch (Exception exception)
        {
            Failed("entry", Safe(exception));
            return 1;
        }
    }

    private static bool IsHostConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    private TenantProvisioning Roles =>
        _provisioning ??= new TenantProvisioning(_admin, _settings.Cipher, _platform, _row!, _cancellationToken);

    // The steps of the database run with the host and port of the admin connection, where the database was created.
    private TenantDatabaseSteps Steps =>
        _steps ??= new TenantDatabaseSteps(_settings.Cipher, _row!, _admin.Host!, _admin.Port, TenantDatabaseTls.From(_admin.ConnectionString), _cancellationToken);

    // A database that holds tables is restored already: pg_restore runs in one transaction, so it restored everything or nothing.
    private async Task RestoreAsync()
    {
        var connection = await Steps.SchemaConnectionAsync();
        await using (var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema'))",
            connection))
        {
            if ((bool)(await command.ExecuteScalarAsync(_cancellationToken))!)
            {
                return;
            }
        }

        var row = _row!;
        var password = _settings.Cipher.Decrypt(_id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var info = new ProcessStartInfo(_plan.PgRestore)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Arguments show in the process list of every user, so the password goes in the environment of the child only.
        info.ArgumentList.Add("--single-transaction");
        info.ArgumentList.Add("--no-owner");
        info.ArgumentList.Add("--no-privileges");
        info.ArgumentList.Add("--exit-on-error");
        info.ArgumentList.Add($"--dbname={row.DatabaseName}");
        info.Environment["PGHOST"] = row.DatabaseHost;
        info.Environment["PGPORT"] = row.DatabasePort.ToString(CultureInfo.InvariantCulture);
        info.Environment["PGDATABASE"] = row.DatabaseName;
        info.Environment["PGUSER"] = row.SchemaUser;
        info.Environment["PGPASSWORD"] = password;
        TenantDatabaseTls.From(_admin.ConnectionString).ApplyTo(info.Environment);
        info.Environment.Remove("PGPASSFILE");
        info.Environment.Remove("PGSERVICE");

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{PgRestore} could not start");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"{PgRestore} could not start");
        }

        using (process)
        {
            try
            {
                // pg_restore's own messages can name the database host, so they are drained and never printed.
                var drainedOutput = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, _cancellationToken);
                var drainedError = process.StandardError.BaseStream.CopyToAsync(Stream.Null, _cancellationToken);
                await SendDumpAsync(process);
                await drainedOutput;
                await drainedError;
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
                throw new InvalidOperationException($"{PgRestore} exited with code {process.ExitCode}");
            }
        }
    }

    // Copies the dump of the archive to the standard input of pg_restore. A pg_restore that stops early closes the pipe, and its exit code tells why.
    private async Task SendDumpAsync(Process process)
    {
        try
        {
            using var stream = new FileStream(_plan.Request.File, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var decrypted = BackupArchive.Decrypt(stream, _plan.BackupKey);
            using var reader = new TarReader(decrypted, leaveOpen: true);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.Name == DumpEntry && entry.DataStream is { } dump)
                {
                    await dump.CopyToAsync(process.StandardInput.BaseStream, _cancellationToken);

                    // Read to the end of the file before pg_restore sees the end of its input, so a changed or missing chunk after the dump fails this step.
                    while (reader.GetNextEntry() is not null)
                    {
                    }

                    await decrypted.CopyToAsync(Stream.Null, _cancellationToken);
                    return;
                }
            }

            throw new InvalidOperationException("the archive has no dump");
        }
        catch (IOException) when (HasEnded(process))
        {
            // The pipe is closed because pg_restore ended.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The same closed pipe.
            }
        }
    }

    private static bool HasEnded(Process process)
    {
        try
        {
            return process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    // Each work item names the tenant whose worker claims it. A restore under a new id takes over the work of the archive.
    private async Task RetagWorkAsync()
    {
        var connection = await Steps.SchemaConnectionAsync();
        await using var command = new NpgsqlCommand("UPDATE axis.process_work_items SET tenant_id = @tenant WHERE tenant_id <> @tenant", connection);
        command.Parameters.AddWithValue("tenant", _id);
        await command.ExecuteNonQueryAsync(_cancellationToken);
    }

    // No session of the archive may sign anyone in. Migrations ran before this step, so even an archive older than the table has it here.
    private async Task EmptySessionsAsync()
    {
        var connection = await Steps.SchemaConnectionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM axis.sessions", connection);
        await command.ExecuteNonQueryAsync(_cancellationToken);
    }

    // Makes the realm of the restored tenant and brings it in step with the tenant's active releases. The archive holds no secret value, so a
    // provider whose bindings have none yet is left out with a warning. The next activation adds it once the operator has set them.
    private async Task RealmAsync()
    {
        var row = _row!;
        var cipher = _settings.Cipher;
        await _realm!.EnsureForTenantAsync(_platform, row, cipher, _settings.Mail ?? RealmMail.None, _cancellationToken);

        var applications = new List<RealmApplication>();
        var connection = await Steps.SchemaConnectionAsync();
        await using (var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options))
        {
            var store = new ActiveReleaseStore(configuration);
            foreach (var active in await store.ListAsync(_cancellationToken))
            {
                if (await store.GetReleaseAsync(active.ReleaseId, _cancellationToken) is { } stored)
                {
                    applications.Add(RealmApplication.FromRelease(active.Name, stored));
                }
            }
        }

        var all = RealmPlanner.Build(applications);
        if (all.Conflicts.Count > 0)
        {
            throw new InvalidOperationException("the active releases name identity providers that cannot share one realm");
        }

        var rows = await _platform.TenantBindings.AsNoTracking().Where(binding => binding.TenantId == _id).ToListAsync(_cancellationToken);
        var byName = rows.ToDictionary(binding => binding.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var provider in all.Plan.Providers)
        {
            if (MissingBinding(byName, provider.Type, provider.IssuerBinding, provider.ClientIdBinding, provider.ClientSecretBinding, provider.ClientKeyBinding, provider.MetadataBinding) is { } missing)
            {
                _error.WriteLine($"{_id} warning the identity provider '{provider.Alias}' waits for the binding '{missing}', which has no value");
            }
        }

        var plan = RealmPlanner.Build([.. applications.Select(application => application with
        {
            Providers = [.. application.Providers.Where(provider =>
                MissingBinding(byName, provider.Type, provider.IssuerBinding, provider.ClientIdBinding, provider.ClientSecretBinding, provider.ClientKeyBinding, provider.MetadataBinding) is null)],
        })]).Plan;
        await ActivationRealmSync.SyncAsync(
            _http, DirectoryTenantCatalog.Realm(row, cipher), DirectoryTenantCatalog.Bindings(_id, rows, cipher), plan, _cancellationToken);
    }

    // The name of the first binding of an oidc or saml provider that has no value or the wrong kind, ignoring letter case, or null when all
    // are set. A provider that the realm plan leaves out anyway, for lack of an issuer, a client id or a metadata binding, misses none.
    private static string? MissingBinding(
        Dictionary<string, TenantBindingRow> bindings,
        string type,
        string? issuer,
        string? clientId,
        string? clientSecret,
        string? clientKey,
        string? metadata)
    {
        bool Has(string name, string kind) =>
            bindings.TryGetValue(name, out var binding)
            && binding.Kind == kind
            && (kind == DeploymentBindingKinds.Secret ? binding.SecretValue is { Length: > 0 } : !string.IsNullOrWhiteSpace(binding.Url));

        if (type == "saml")
        {
            return metadata is not null && !Has(metadata, DeploymentBindingKinds.Url) ? metadata : null;
        }

        if (type != "oidc" || issuer is null || clientId is null)
        {
            return null;
        }

        if (!Has(issuer, DeploymentBindingKinds.Url))
        {
            return issuer;
        }

        foreach (var name in new[] { clientId, clientSecret, clientKey })
        {
            if (name is not null && !Has(name, DeploymentBindingKinds.Secret))
            {
                return name;
            }
        }

        return null;
    }

    private async Task SuspendAsync()
    {
        var version = await Steps.ReadSchemaVersionAsync();
        if (version != TenantSchemaVersion.Current)
        {
            throw new InvalidOperationException("the database is not on the schema of this build");
        }

        var row = _row!;
        row.SchemaVersion = version;
        row.Status = "suspended";
        row.Version++;
        await _platform.SaveChangesAsync(_cancellationToken);
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

    private static string Safe(Exception exception) => TenantDatabaseSteps.Safe(exception);

    private void Ok(string step) => _output.WriteLine($"{_id} {step} ok");

    private void Failed(string step, string message) => _output.WriteLine($"{_id} {step} failed: {message}");

    private sealed record CheckResult(BackupManifest? Manifest, IReadOnlyList<string> Problems, bool NewRealm);
}

/// <summary>
/// What the <c>check</c> step found: the request with the archive's full path, the manifest, the backup key, the path of <c>pg_restore</c> and whether
/// the tenant gets a new realm (<paramref name="NewRealm"/>) or keeps the archive's sign-in.
/// </summary>
internal sealed record RestorePlan(TenantRestoreRequest Request, BackupManifest Manifest, byte[] BackupKey, string PgRestore, bool NewRealm);
