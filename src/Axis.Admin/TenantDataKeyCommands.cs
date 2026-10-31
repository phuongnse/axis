using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant data-key rotate</c> and <c>retire</c>. Rotate adds the next data key and lookup key, then moves every stored
/// sensitive value and lookup hash to them. Retire deletes the older keys once no value or hash names one. The audit record holds
/// key labels such as <c>data_key:2</c>, and no output or record holds a key.
/// </summary>
internal static class TenantDataKeyCommands
{
    private sealed record Target(string Connection, TenantSecretCipher Cipher, TenantRow Row);

    public static Task<int> RotateAsync(AdminRun run) => RunAsync(run, rotate: true);

    public static Task<int> RetireAsync(AdminRun run) => RunAsync(run, rotate: false);

    private static async Task<int> RunAsync(AdminRun run, bool rotate)
    {
        if (run.Args.Length != 4 || run.Args[3].StartsWith("--", StringComparison.Ordinal))
        {
            run.Error.WriteLine(AdminCommands.Usage);
            return 2;
        }

        var (target, exit) = await PrepareAsync(run, run.Args[3]);
        if (target is null)
        {
            return exit;
        }

        var verb = rotate ? "rotate" : "retire";
        try
        {
            return rotate ? await RotateAsync(run, target) : await RetireAsync(run, target);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException or DbUpdateException)
        {
            run.Output.WriteLine($"{target.Row.Id} data-key {verb} failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }
    }

    private static async Task<int> RotateAsync(AdminRun run, Target target)
    {
        var id = target.Row.Id;
        int data;
        int lookup;
        await using (var platform = NewContext(target.Connection))
        {
            (data, lookup) = await NewestAsync(platform, id, run.CancellationToken);
            data++;
            lookup++;
        }

        // The record comes first: it, the keys and the values are in different places, so they cannot share a transaction.
        var audit = new TenantAudit(run.Command.Name, target.Connection);
        if (!await audit.WriteAsync(id, run.Output, run.CancellationToken, [TenantSecretFields.DataKey(data), TenantSecretFields.LookupKey(lookup)]))
        {
            return 1;
        }

        await using (var platform = NewContext(target.Connection))
        {
            TenantKeyRows.Add(platform, id, target.Cipher, TenantKeyKinds.Data, data);
            TenantKeyRows.Add(platform, id, target.Cipher, TenantKeyKinds.Lookup, lookup);
            await platform.SaveChangesAsync(run.CancellationToken);
        }

        // Built after the save, so it holds the new keys as the newest and still finds the older ones by number.
        var values = new SensitiveValues(new TenantKeys(new DirectoryTenantKeySource(target.Connection, target.Cipher)));
        await using var steps = Steps(run, target);
        var updated = await SensitiveKeyRotation.RotateAsync(await steps.SchemaConnectionAsync(), values, id, data, lookup, run.CancellationToken);
        run.Output.WriteLine($"{id} data-key rotate ok: data key {data}, lookup key {lookup}, {updated} values updated");
        return 0;
    }

    private static async Task<int> RetireAsync(AdminRun run, Target target)
    {
        var id = target.Row.Id;
        int data;
        int lookup;
        await using (var platform = NewContext(target.Connection))
        {
            (data, lookup) = await NewestAsync(platform, id, run.CancellationToken);
        }

        await using var steps = Steps(run, target);
        var remaining = await SensitiveKeyRotation.CountOlderAsync(await steps.SchemaConnectionAsync(), data, lookup, run.CancellationToken);
        if (remaining > 0)
        {
            run.Output.WriteLine($"{id} data-key retire refused: {remaining} stored values still name an older key. Run tenant data-key rotate again.");
            return 1;
        }

        await using var context = NewContext(target.Connection);
        var older = await context.TenantDataKeys
            .Where(key => key.TenantId == id && ((key.Kind == TenantKeyKinds.Data && key.Number < data) || (key.Kind == TenantKeyKinds.Lookup && key.Number < lookup)))
            .OrderBy(key => key.Kind).ThenBy(key => key.Number)
            .ToListAsync(run.CancellationToken);
        var dataKeys = older.Where(key => key.Kind == TenantKeyKinds.Data).ToList();
        var lookupKeys = older.Where(key => key.Kind == TenantKeyKinds.Lookup).ToList();
        if (older.Count > 0)
        {
            string[] changed =
            [
                .. dataKeys.Select(key => TenantSecretFields.DataKey(key.Number)),
                .. lookupKeys.Select(key => TenantSecretFields.LookupKey(key.Number)),
            ];
            if (!await new TenantAudit(run.Command.Name, target.Connection).WriteAsync(id, run.Output, run.CancellationToken, changed))
            {
                return 1;
            }

            context.TenantDataKeys.RemoveRange(older);
            await context.SaveChangesAsync(run.CancellationToken);
        }

        run.Output.WriteLine($"{id} data-key retire ok: {dataKeys.Count} data keys, {lookupKeys.Count} lookup keys deleted");
        return 0;
    }

    /// <summary>The highest number of each kind the tenant holds, or 0 for a kind it has none of.</summary>
    private static async Task<(int Data, int Lookup)> NewestAsync(PlatformDbContext platform, string id, CancellationToken cancellationToken)
    {
        var numbers = await platform.TenantDataKeys.AsNoTracking()
            .Where(key => key.TenantId == id)
            .GroupBy(key => key.Kind)
            .Select(group => new { Kind = group.Key, Newest = group.Max(key => key.Number) })
            .ToListAsync(cancellationToken);
        return (
            numbers.FirstOrDefault(row => row.Kind == TenantKeyKinds.Data)?.Newest ?? 0,
            numbers.FirstOrDefault(row => row.Kind == TenantKeyKinds.Lookup)?.Newest ?? 0);
    }

    private static TenantDatabaseSteps Steps(AdminRun run, Target target) =>
        new(target.Cipher, target.Row, target.Row.DatabaseHost, target.Row.DatabasePort, TenantDatabaseTls.From(target.Connection), run.CancellationToken);

    /// <summary>
    /// Checks the source, reads the settings and the platform tables, and finds the tenant. When the command cannot run, the target is
    /// <see langword="null"/>, the reason is written and the exit code is returned.
    /// </summary>
    private static async Task<(Target? Target, int Exit)> PrepareAsync(AdminRun run, string id)
    {
        // A setting's own message holds its value, so this says only which setting is wrong.
        TenancySource source;
        try
        {
            source = TenancySettings.Source(run.Configuration);
        }
        catch (InvalidOperationException)
        {
            run.Error.WriteLine($"The setting {TenancySettings.SourceSetting} must be 'configuration' or 'directory'.");
            return (null, 2);
        }

        if (source != TenancySource.Directory)
        {
            run.Error.WriteLine("The tenant data-key commands need Tenancy:Source=directory.");
            return (null, 2);
        }

        var settings = AdminCommands.ReadPlatformSettings(run.Configuration, out var problem);
        if (settings is null)
        {
            run.Error.WriteLine(problem);
            return (null, 2);
        }

        try
        {
            var tablesProblem = await AdminCommands.PlatformTablesProblemAsync(settings.PlatformConnectionString, run.CancellationToken);
            if (tablesProblem is not null)
            {
                run.Error.WriteLine(tablesProblem);
                return (null, 2);
            }

            TenantRow? row;
            await using (var platform = NewContext(settings.PlatformConnectionString))
            {
                row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id, run.CancellationToken);
            }

            if (row is null)
            {
                run.Error.WriteLine("The tenant does not exist.");
                return (null, 2);
            }

            if (row.Status == "provisioning")
            {
                run.Error.WriteLine($"The tenant '{id}' is still provisioning. Finish it with tenant create.");
                return (null, 2);
            }

            return (new Target(settings.PlatformConnectionString, settings.Cipher, row), 0);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            run.Error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return (null, 1);
        }
    }

    private static PlatformDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);
}
