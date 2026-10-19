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

    private static readonly string[] _header = ["id", "status", "hosts", "schema version", "preview of"];

    public static Task<int> SuspendAsync(string connectionString, string id, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        SetAsync(connectionString, id, Active, Suspended, output, error, cancellationToken);

    public static Task<int> ResumeAsync(string connectionString, string id, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        SetAsync(connectionString, id, Suspended, Active, output, error, cancellationToken);

    /// <summary>
    /// Moves a tenant from one status to the other with one conditional update, so a <c>tenant create</c> that finishes
    /// at the same moment is never overwritten. When no row changed, a read tells a no-op from a refusal.
    /// </summary>
    private static async Task<int> SetAsync(
        string connectionString,
        string id,
        string from,
        string to,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var update = new NpgsqlCommand(
                "UPDATE platform.tenants SET status = @to, version = version + 1 WHERE id = @id AND status = @from",
                connection))
            {
                update.Parameters.AddWithValue("to", to);
                update.Parameters.AddWithValue("from", from);
                update.Parameters.AddWithValue("id", id);
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
                {
                    output.WriteLine($"{id} status ok");
                    return 0;
                }
            }

            await using var read = new NpgsqlCommand("SELECT status FROM platform.tenants WHERE id = @id", connection);
            read.Parameters.AddWithValue("id", id);
            var status = (string?)await read.ExecuteScalarAsync(cancellationToken);
            if (status is null)
            {
                error.WriteLine("The tenant does not exist.");
                return 2;
            }

            if (status == Provisioning)
            {
                output.WriteLine($"{id} status failed: the tenant is still provisioning. Finish tenant create first.");
                return 1;
            }

            // The tenant already has the wanted status: a no-op that leaves the version alone.
            output.WriteLine($"{id} status ok");
            return 0;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            output.WriteLine($"{id} status failed: {Safe(exception)}");
            return 1;
        }
    }

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
                .Select(host => new { host.TenantId, host.Host })
                .ToListAsync(cancellationToken);
            tenants = [.. rows.Select(row => (row.Id, row.Status, row.SchemaVersion, row.PreviewOf)).OrderBy(row => row.Id, StringComparer.Ordinal)];
            hosts = hostRows.OrderBy(row => row.Host, StringComparer.Ordinal).ToLookup(row => row.TenantId, row => row.Host);
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
