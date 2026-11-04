using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant suspend</c>, <c>tenant resume</c> and <c>tenant list</c>. They write and read only the tenant directory.
/// The hosts pick a change up at their next directory refresh.
/// </summary>
internal static class TenantStatus
{
    private const string Active = "active";

    private const string Suspended = "suspended";

    private const string Provisioning = "provisioning";

    internal const string UnknownMessage = "The tenant does not exist.";

    internal const string ProvisioningMessage = "the tenant is still provisioning. Finish tenant create first.";

    private static readonly string[] _header = ["id", "status", "hosts", "schema version", "preview of"];

    public static Task<int> SuspendAsync(string connectionString, string id, TenantAudit audit, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        SetAsync(connectionString, id, Active, Suspended, audit, output, error, cancellationToken);

    public static Task<int> ResumeAsync(string connectionString, string id, TenantAudit audit, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        SetAsync(connectionString, id, Suspended, Active, audit, output, error, cancellationToken);

    /// <summary>
    /// Moves a tenant from one status to the other with one conditional update, so a <c>tenant create</c> that finishes
    /// at the same moment is never overwritten. A read first refuses an unknown tenant, so that a refusal writes no audit record.
    /// When the update changed no row, a second read tells a no-op from a refusal.
    /// </summary>
    private static async Task<int> SetAsync(
        string connectionString,
        string id,
        string from,
        string to,
        TenantAudit audit,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var exists = new NpgsqlCommand("SELECT status FROM platform.tenants WHERE id = @id", connection))
            {
                exists.Parameters.AddWithValue("id", id);
                if (await exists.ExecuteScalarAsync(cancellationToken) is null)
                {
                    error.WriteLine(UnknownMessage);
                    return 2;
                }
            }

            if (!await audit.WriteAsync(id, output, cancellationToken))
            {
                return 1;
            }

            switch (await ChangeAsync(connection, id, from, to, cancellationToken))
            {
                case StatusChange.Unknown:
                    error.WriteLine(UnknownMessage);
                    return 2;
                case StatusChange.Provisioning:
                    output.WriteLine($"{id} status failed: {ProvisioningMessage}");
                    return 1;
                default:
                    // Changed, or unchanged because the tenant already has the wanted status: a no-op that leaves the version alone.
                    output.WriteLine($"{id} status ok");
                    return 0;
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            output.WriteLine($"{id} status failed: {Safe(exception)}");
            return 1;
        }
    }

    /// <summary>
    /// Moves a tenant from <paramref name="from"/> to <paramref name="to"/> with one conditional update, so a <c>tenant create</c>
    /// that finishes at the same moment is never overwritten. When the update changed no row, a read tells an unknown tenant, a
    /// refusal and a no-op apart. The command line and the provisioning jobs both call it.
    /// </summary>
    internal static async Task<StatusChange> ChangeAsync(NpgsqlConnection connection, string id, string from, string to, CancellationToken cancellationToken)
    {
        await using (var update = new NpgsqlCommand(
            "UPDATE platform.tenants SET status = @to, version = version + 1 WHERE id = @id AND status = @from",
            connection))
        {
            update.Parameters.AddWithValue("to", to);
            update.Parameters.AddWithValue("from", from);
            update.Parameters.AddWithValue("id", id);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return StatusChange.Changed;
            }
        }

        await using var read = new NpgsqlCommand("SELECT status FROM platform.tenants WHERE id = @id", connection);
        read.Parameters.AddWithValue("id", id);
        return (string?)await read.ExecuteScalarAsync(cancellationToken) switch
        {
            null => StatusChange.Unknown,
            Provisioning => StatusChange.Provisioning,
            _ => StatusChange.Unchanged,
        };
    }

    /// <summary>Suspends an active tenant, as <c>tenant suspend</c> does.</summary>
    internal static Task<StatusChange> SuspendChangeAsync(NpgsqlConnection connection, string id, CancellationToken cancellationToken) =>
        ChangeAsync(connection, id, Active, Suspended, cancellationToken);

    /// <summary>Resumes a suspended tenant, as <c>tenant resume</c> does.</summary>
    internal static Task<StatusChange> ResumeChangeAsync(NpgsqlConnection connection, string id, CancellationToken cancellationToken) =>
        ChangeAsync(connection, id, Suspended, Active, cancellationToken);

    /// <summary>Sets a provisioning tenant active, as the last step of a create job does. A tenant that already left <c>provisioning</c> is <see cref="StatusChange.Unchanged"/>.</summary>
    internal static Task<StatusChange> ActivateChangeAsync(NpgsqlConnection connection, string id, CancellationToken cancellationToken) =>
        ChangeAsync(connection, id, Provisioning, Active, cancellationToken);

    public static async Task<int> ListAsync(string connectionString, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<(string Id, string Status, string SchemaVersion, string? PreviewOf)> tenants;
        ILookup<string, string> hosts;
        try
        {
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);

            // Only these columns are read, so no secret ever reaches the tool.
            var rows = await platform.Tenants.AsNoTracking()
                .Select(tenant => new { tenant.Id, tenant.Status, tenant.SchemaVersion, tenant.PreviewOf })
                .ToListAsync(cancellationToken);
            var hostRows = await platform.TenantHosts.AsNoTracking()
                .Select(host => new { host.TenantId, host.Host, host.SitePath })
                .ToListAsync(cancellationToken);
            tenants = [.. rows.Select(row => (row.Id, row.Status, row.SchemaVersion, row.PreviewOf)).OrderBy(row => row.Id, StringComparer.Ordinal)];
            hosts = hostRows.OrderBy(row => row.Host, StringComparer.Ordinal).ToLookup(row => row.TenantId, row => row.SitePath is null ? row.Host : $"{row.Host} ({row.SitePath})");
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return 1;
        }

        var table = new List<string[]>()
        {
            _header,
        };
        table.AddRange(tenants.Select(tenant => new[]
        {
            tenant.Id,
            tenant.Status,
            string.Join(',', hosts[tenant.Id]),
            tenant.SchemaVersion.Length == 0 ? "-" : tenant.SchemaVersion,
            tenant.PreviewOf ?? "-",
        }));

        var widths = Enumerable.Range(0, _header.Length).Select(column => table.Max(row => row[column].Length)).ToArray();
        foreach (var row in table)
        {
            output.WriteLine(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }

        return 0;
    }

    // Never ToString(): an Npgsql message can name the host.
    private static string Safe(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        InvalidOperationException invalid => invalid.Message,
        PostgresException postgres => postgres.MessageText,
        _ => exception.GetType().Name,
    };
}

/// <summary>The outcome of <see cref="TenantStatus.ChangeAsync"/>.</summary>
internal enum StatusChange
{
    /// <summary>The tenant had the old status and now has the new one.</summary>
    Changed,

    /// <summary>The tenant already had the new status, so nothing changed and the version stays.</summary>
    Unchanged,

    /// <summary>There is no such tenant.</summary>
    Unknown,

    /// <summary>The tenant is still provisioning, so its status is not changed.</summary>
    Provisioning,
}
