using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Administration;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>cell restore-check</c>: checks once that a recent archive restores. It lists the backup storage and picks at random a tenant that is not a
/// preview, has a backup key and has an archive of today or yesterday (UTC). It writes the one platform audit record, for that source tenant and
/// before its first change, naming the scratch tenant. It removes any <c>restore-check-*</c> tenant that an earlier run left behind, downloads the newest
/// archive of the source tenant to a folder that only its owner can use, and restores it with the logic of <c>tenant restore</c> into the scratch tenant
/// <c>restore-check-&lt;yyyymmdd&gt;</c>, with a host under <c>Cell:InternalDomain</c> and always a new realm. The users of that realm get new ids, because
/// the ids of the live source realm cannot be imported a second time. It then compares the row count of each table with the manifest's
/// <c>counts</c>, leaving out <c>axis.sessions</c> and the tables that <c>counts</c> does not hold, and the users of the realm with those of
/// <c>realm.json</c>, leaving out service accounts. An archive without <c>realm.json</c> counts as zero users, and one without <c>counts</c> fails.
/// Then it compiles each application folder that the restore wrote under <c>Studio:ApplicationsPath</c>/&lt;scratch id&gt; with the cell's extension packages
/// and modules (<c>Extensions:Path</c>, <c>Modules:Path</c>) and runs its test scenarios, module scenarios included, with the logic of the publisher's test step. Each scenario
/// runs in its own temporary database on <c>Scenarios:ServerConnectionString</c>, for at most the publisher's test limit for each application, and writes a
/// <c>scenario &lt;name&gt; passed</c> or <c>failed</c> line. A compile error or a failed scenario fails the check, and a tenant with an application but no
/// scenario server fails it with <c>AXC0217</c>. A tenant without Studio applications needs no scenario server. A message names a table or a number, never a row or a username. The scratch tenant is removed at the end, whatever the result, with
/// <see cref="ScratchTenantRemoval"/>, and the archive folder is deleted. The result goes to <see cref="AxisMetrics.BackupRestoreCheck"/>, tagged with
/// the source tenant and exported over OTLP when an OTLP endpoint is set, and to the output. Exit 0 means that the check passed, 1 that any step after
/// the pick failed or that no tenant has an archive of today or yesterday, and 2 that nothing ran: arguments, outside a cell, or a missing setting or
/// <c>pg_restore</c>, an invalid release or scenario server setting, or an OTLP endpoint that is not <c>https</c> outside Development.
/// </summary>
internal static partial class CellRestoreCheck
{
    private const string Archive = "archive.axisbak";

    // The sessions of a restored tenant are emptied on purpose.
    private const string SessionsTable = "axis.sessions";

    private const int UserPageSize = 100;

    public static async Task<int> RunAsync(AdminRun run)
    {
        var now = DateTime.UtcNow;
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

        if (string.IsNullOrWhiteSpace(run.Cell.InternalDomain))
        {
            run.Error.WriteLine("The setting Cell:InternalDomain is required.");
            return 2;
        }

        var environment = run.Configuration[AdminCommands.DotnetEnvironmentSetting] ?? Environments.Production;
        if (environment != Environments.Development && AxisTelemetry.InsecureOtlpEndpoint(name => run.Configuration[name]) is { } insecure)
        {
            run.Error.WriteLine($"'{insecure}' must be an https URL outside Development.");
            return 2;
        }

        var settings = AdminCommands.ReadRestoreSettings(run.Configuration, out var problem);
        if (settings is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        // The scratch tenant always gets a new realm, so the identity provider is required.
        if (settings.Create.IdentityProvider is null)
        {
            run.Error.WriteLine($"The setting {AdminCommands.IdentityProviderUrlSetting} is required.");
            return 2;
        }

        var release = AdminCommands.ReadReleaseSettings(run.Configuration, run.Error, out problem);
        if (release is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        if (!AdminCommands.TryReadScenarioServer(run.Configuration, run.Error, out var scenarioServer))
        {
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

        var pgRestore = AdminCommands.FindTool(settings.SearchPath, TenantRestore.PgRestore);
        if (pgRestore is null)
        {
            run.Error.WriteLine($"{TenantRestore.PgRestore} was not found in the directories of {AdminCommands.PathSetting}.");
            return 2;
        }

        var platformConnection = settings.Create.PlatformConnectionString;
        try
        {
            problem = await AdminCommands.PlatformTablesProblemAsync(platformConnection, run.CancellationToken);
            if (problem is not null)
            {
                run.Error.WriteLine(problem);
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            run.Error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return 2;
        }

        settings = settings with
        {
            Create = settings.Create with { CellBackupCipher = cellCipher },
            InCell = true,
            StudioApplicationsPath = AdminCommands.ResolveFolder(run.Configuration[AdminCommands.StudioApplicationsPathSetting]),
        };

        var output = run.Output;
        var cancellationToken = run.CancellationToken;
        using var client = storage.NewClient();
        (string Tenant, string Key)? picked;
        try
        {
            picked = await PickAsync(client, storage.Bucket, platformConnection, now, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or NpgsqlException or InvalidOperationException)
        {
            output.WriteLine($"cell restore-check failed: the archives could not be listed: {CellBackup.SafeUpload(exception)}");
            return 1;
        }

        if (picked is not { } source)
        {
            output.WriteLine("cell restore-check failed: no tenant has an archive of today or yesterday");
            return 1;
        }

        var scratch = $"{ScratchTenantRemoval.IdPrefix}{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

        // The only audit record of the run: the result is not known yet, so it goes to the metric and the output.
        var audit = new TenantAudit(run.Command.Name, platformConnection);
        if (!await audit.WriteAsync(source.Tenant, output, cancellationToken, [$"tenant: {scratch}"]))
        {
            return 1;
        }

        using var http = AdminCommands.IdentityProviderClient(run.IdentityProviderHandler);
        var realm = new TenantRealm(http, settings.Create.IdentityProvider);
        var removal = new ScratchTenantRemoval(settings.Create.Admin, platformConnection, realm, settings.StudioApplicationsPath);
        using var metrics = CellBackupMetrics.Start(run.Configuration);
        var passed = false;
        try
        {
            passed = await CheckAsync(run, settings, release, scenarioServer, cellCipher, pgRestore, client, storage.Bucket, source, scratch, realm, http, removal);
        }
        catch (OperationCanceledException)
        {
            output.WriteLine($"{scratch} check failed: canceled");
        }
        catch (Exception exception)
        {
            output.WriteLine($"{scratch} check failed: {TenantDatabaseSteps.Safe(exception)}");
        }
        finally
        {
            // The scratch tenant goes whatever happened, also after a cancel, so this removal ignores the token of the run.
            passed &= await RemoveAsync(removal, scratch, output);
        }

        AxisMetrics.RecordBackupRestoreCheck(source.Tenant, passed);
        output.WriteLine($"{source.Tenant} restore-check {(passed ? "passed" : "failed")}");
        return passed ? 0 : 1;
    }

    // Picks a tenant at random from those with an archive of today or yesterday, a backup key and no preview, and its newest archive.
    private static async Task<(string Tenant, string Key)?> PickAsync(
        IAmazonS3 client, string bucket, string platformConnection, DateTime now, CancellationToken cancellationToken)
    {
        var dates = new HashSet<string>(StringComparer.Ordinal)
        {
            now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        var newest = new Dictionary<string, string>(StringComparer.Ordinal);
        var request = new ListObjectsV2Request { BucketName = bucket };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response.S3Objects ?? [])
            {
                var match = ArchiveKey().Match(item.Key);
                if (!match.Success || !dates.Contains(match.Groups["date"].Value))
                {
                    continue;
                }

                var id = match.Groups["id"].Value;
                var date = match.Groups["date"].Value;
                if (!newest.TryGetValue(id, out var known) || string.CompareOrdinal(date, known) > 0)
                {
                    newest[id] = date;
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        var ids = newest.Keys.Where(id => !ScratchTenantRemoval.IsScratchId(id)).ToArray();
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnection).Options);
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(tenant => ids.Contains(tenant.Id) && tenant.PreviewOf == null)
            .Select(tenant => tenant.Id)
            .ToArrayAsync(cancellationToken);
        var keyed = await platform.TenantBackupKeys.AsNoTracking()
            .Where(key => ids.Contains(key.TenantId))
            .Select(key => key.TenantId)
            .ToArrayAsync(cancellationToken);
        var candidates = tenants.Intersect(keyed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var tenant = candidates[RandomNumberGenerator.GetInt32(candidates.Length)];
        return (tenant, $"{tenant}/{newest[tenant]}.axisbak");
    }

    // Everything from the removal of leftovers to the scenarios. It returns true when the restore, the counts, the users and the scenarios all passed.
    private static async Task<bool> CheckAsync(
        AdminRun run,
        RestoreSettings settings,
        ReleaseSettings release,
        string? scenarioServer,
        TenantSecretCipher cellCipher,
        string pgRestore,
        IAmazonS3 client,
        string bucket,
        (string Tenant, string Key) source,
        string scratch,
        TenantRealm realm,
        HttpClient http,
        ScratchTenantRemoval removal)
    {
        var output = run.Output;
        var cancellationToken = run.CancellationToken;
        var create = settings.Create;
        var cleaned = true;
        foreach (var leftover in await LeftoversAsync(create, scratch, cancellationToken))
        {
            try
            {
                await removal.RemoveAsync(leftover, cancellationToken);
                output.WriteLine($"{leftover} cleanup ok");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                output.WriteLine($"{leftover} cleanup failed: {TenantDatabaseSteps.Safe(exception)}");
                cleaned = false;
            }
        }

        if (!cleaned)
        {
            return false;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"axis-restore-check-{Guid.NewGuid():N}");
        try
        {
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
            if (!await DownloadAsync(client, bucket, source, file, output, cancellationToken))
            {
                return false;
            }

            using var keyErrors = new StringWriter();
            var key = await AdminCommands.ReadCellBackupKeyAsync(
                create.PlatformConnectionString, source.Tenant, cellCipher, null, "The tenant has no backup key.", keyErrors, cancellationToken);
            if (key is null)
            {
                foreach (var line in keyErrors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    output.WriteLine($"{source.Tenant} key failed: {line}");
                }

                return false;
            }

            var request = new TenantRestoreRequest(file, scratch, [$"{scratch}.{run.Cell.InternalDomain!.Trim()}"], null, source.Tenant, Scratch: true);
            var plan = await TenantRestore.PrepareAsync(settings, request, key, pgRestore, realm, output, run.Error, cancellationToken);
            if (plan is null
                || await new TenantRestore(create, plan, realm, http, audit: null, output, run.Error, cancellationToken).RunAsync() != 0)
            {
                return false;
            }

            TenantRow row;
            await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(create.PlatformConnectionString).Options))
            {
                row = await platform.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == scratch, cancellationToken);
            }

            // The comparisons and the scenarios all run, so one line each tells what was wrong.
            var counts = await CompareCountsAsync(create, row, plan.Manifest.Counts, output, cancellationToken);
            var users = await CompareUsersAsync(realm, scratch, plan.Realm, output, cancellationToken);
            var scenarios = await RunScenariosAsync(run, settings, release, scenarioServer, scratch, row.SchemaVersion);
            return counts && users && scenarios;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be removed must not hide the result of the check.
            }
        }
    }

    // The scratch ids of the directory, of the databases of the server, and today's id, whether or not it exists.
    private static async Task<IEnumerable<string>> LeftoversAsync(CreateSettings create, string scratch, CancellationToken cancellationToken)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal) { scratch };
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(create.PlatformConnectionString).Options))
        {
            ids.UnionWith(await platform.Tenants.AsNoTracking()
                .Where(tenant => tenant.Id.StartsWith(ScratchTenantRemoval.IdPrefix))
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken));
        }

        const string Prefix = "axis_";
        await using var connection = new NpgsqlConnection(create.Admin.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(@"SELECT datname FROM pg_database WHERE datname LIKE 'axis\_restore-check-%'", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0)[Prefix.Length..]);
        }

        return ids.Where(ScratchTenantRemoval.IsScratchId);
    }

    private static async Task<bool> DownloadAsync(
        IAmazonS3 client, string bucket, (string Tenant, string Key) source, string file, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(bucket, source.Key, cancellationToken);
            await using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
            await response.ResponseStream.CopyToAsync(target, cancellationToken);
        }
        catch (Exception exception)
        {
            output.WriteLine($"{source.Tenant} download failed: {CellBackup.SafeUpload(exception)}");
            return false;
        }

        output.WriteLine($"{source.Tenant} download ok");
        return true;
    }

    // The row count of every table that the manifest lists, except the sessions, against the restored database. A table the restore lacks counts as 0.
    private static async Task<bool> CompareCountsAsync(
        CreateSettings create,
        TenantRow row,
        IReadOnlyDictionary<string, long>? expected,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var scratch = row.Id;
        if (expected is null)
        {
            output.WriteLine($"{scratch} counts failed: the archive has no counts");
            return false;
        }

        await using var steps = new TenantDatabaseSteps(create.Cipher, row, create.Admin.Host!, create.Admin.Port, TenantDatabaseTls.From(create.Admin.ConnectionString), cancellationToken);
        var actual = await TenantBackup.ReadTableCountsAsync(await steps.SchemaConnectionAsync(), null, cancellationToken);
        var equal = true;
        foreach (var (table, count) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (table == SessionsTable)
            {
                continue;
            }

            var restored = actual.GetValueOrDefault(table);
            if (restored != count)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scratch} counts failed: {TenantRestore.Printable(table)} has {restored} rows and the archive {count}"));
                equal = false;
            }
        }

        if (equal)
        {
            output.WriteLine($"{scratch} counts ok");
        }

        return equal;
    }

    // The test scenarios of each application that the restore wrote to the scratch tenant's Studio folder, with the publisher's test step. They run in
    // temporary databases built from the Studio files, not against the restored releases. Every application runs, also after one failed.
    private static async Task<bool> RunScenariosAsync(
        AdminRun run, RestoreSettings settings, ReleaseSettings release, string? scenarioServer, string scratch, string schemaVersion)
    {
        if (settings.StudioApplicationsPath is not { } root)
        {
            return true;
        }

        var tenantFolder = Path.Combine(root, scratch);
        if (!Directory.Exists(tenantFolder))
        {
            return true;
        }

        var activation = new TenantReleaseActivation(
            release.InlineRecomputeLimit,
            release.ActivationTimeout,
            new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(run.Configuration))),
            run.Output,
            run.Error,
            run.CancellationToken);
        var passed = true;
        foreach (var folder in ApplicationFolders(tenantFolder))
        {
            var test = await activation.TestAsync(
                scratch,
                schemaVersion,
                scenarioServer,
                ReadFiles(folder),
                null,
                release.Packages,
                release.Modules,
                new List<Diagnostic>(),
                run.PublisherTestLimit,
                run.CancellationToken);
            passed &= test.Succeeded;
        }

        return passed;
    }

    // The folders that Studio would list: not hidden, not a link and holding a manifest, in ordinal order of name.
    private static List<string> ApplicationFolders(string tenantFolder)
    {
        string[] subfolders;
        try
        {
            subfolders = Directory.GetDirectories(tenantFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(subfolders, (left, right) => string.CompareOrdinal(Path.GetFileName(left), Path.GetFileName(right)));
        return [.. subfolders.Where(folder =>
            !Path.GetFileName(folder).StartsWith('.')
            && !IsLink(folder)
            && File.Exists(Path.Combine(folder, ApplicationLoader.ManifestFileName)))];
    }

    private static bool IsLink(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // The JSON files of an application folder, as the application loader lists them. Null means that the folder cannot be read.
    private static List<ResourceContent>? ReadFiles(string folder)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false };
            return Directory.EnumerateFiles(folder, "*.json", options)
                .Select(path => Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
                .Select(file => new ResourceContent(file, File.ReadAllText(Path.Combine(folder, file))))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The usernames of the scratch realm against those of realm.json. A service account is no user of the archive. A username is never printed.
    private static async Task<bool> CompareUsersAsync(
        TenantRealm realm, string scratch, JsonObject? document, TextWriter output, CancellationToken cancellationToken)
    {
        var token = await realm.AdminTokenAsync(cancellationToken);
        var actual = new List<string>();
        for (var first = 0; ; first += UserPageSize)
        {
            using var response = await realm.SendAsync(
                token,
                HttpMethod.Get,
                $"admin/realms/{Uri.EscapeDataString(scratch)}/users?first={first.ToString(CultureInfo.InvariantCulture)}&max={UserPageSize.ToString(CultureInfo.InvariantCulture)}&briefRepresentation=false",
                null,
                cancellationToken);
            TenantRealm.EnsureSuccess(response, "list the users");
            var page = (await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken))!;
            actual.AddRange(page
                .Select(user => user!.AsObject())
                .Where(user => user["serviceAccountClientId"] is null)
                .Select(user => (user["username"]?.GetValue<string>() ?? "").ToLowerInvariant()));
            if (page.Count < UserPageSize)
            {
                break;
            }
        }

        var archived = (document?["users"]?.AsArray() ?? [])
            .Select(user => (user?["username"]?.GetValue<string>() ?? "").ToLowerInvariant())
            .ToList();
        actual.Sort(StringComparer.Ordinal);
        archived.Sort(StringComparer.Ordinal);
        if (actual.SequenceEqual(archived, StringComparer.Ordinal))
        {
            output.WriteLine($"{scratch} users ok");
            return true;
        }

        var differ = actual.Except(archived, StringComparer.Ordinal).Count() + archived.Except(actual, StringComparer.Ordinal).Count();
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{scratch} users failed: the realm has {actual.Count} users and the archive {archived.Count}, {differ} differ"));
        return false;
    }

    private static async Task<bool> RemoveAsync(ScratchTenantRemoval removal, string scratch, TextWriter output)
    {
        try
        {
            await removal.RemoveAsync(scratch, CancellationToken.None);
            output.WriteLine($"{scratch} remove ok");
            return true;
        }
        catch (Exception exception)
        {
            output.WriteLine($"{scratch} remove failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }
    }

    [GeneratedRegex(@"^(?<id>[a-z0-9-]{1,50})/(?<date>\d{4}-\d{2}-\d{2})\.axisbak\z", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveKey();
}
