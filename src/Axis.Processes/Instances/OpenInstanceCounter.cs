using System.Data.Common;
using Axis.Data;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>Counts the <c>running</c> and <c>waiting</c> process instances of an application by the release they are pinned to.</summary>
public sealed class OpenInstanceCounter : IOpenInstanceCounter
{
    /// <summary>The query behind <see cref="CountByReleaseAsync"/>, with the parameters <c>@application</c>, <c>@running</c> and <c>@waiting</c>.</summary>
    internal const string CountByReleaseSql =
        """
        SELECT release_id, count(*)
        FROM axis.process_instances
        WHERE application_id = @application AND state IN (@running, @waiting)
        GROUP BY release_id
        """;

    public static OpenInstanceCounter Instance { get; } = new();

    public async Task<IReadOnlyDictionary<Guid, int>> CountByReleaseAsync(DbTransaction transaction, Guid applicationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var npgsql = (NpgsqlTransaction)transaction;
        await using var command = new NpgsqlCommand(
            CountByReleaseSql,
            npgsql.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            npgsql);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);

        var counts = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetGuid(0)] = (int)reader.GetInt64(1);
        }

        return counts;
    }
}
