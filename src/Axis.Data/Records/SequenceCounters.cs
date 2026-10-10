using Axis.Configuration.Model;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Hands out the next number of a sequence from its counter row in <c>axis.sequence_counters</c>
/// of the tenant database.
/// </summary>
internal static class SequenceCounters
{
    // The alias keeps the column in SET apart from the one in the conflicting row.
    private const string NextValueSql =
        """
        INSERT INTO "axis"."sequence_counters" AS c ("sequence_id", "application_id", "period", "last_value")
        VALUES (@sequence, @application, @period, 1)
        ON CONFLICT ("sequence_id", "period") DO UPDATE SET "last_value" = c."last_value" + 1
        RETURNING "last_value"
        """;

    /// <summary>
    /// Increments the counter of <paramref name="sequence"/> for the period of
    /// <paramref name="utcNow"/> and returns the formatted number. The row stays locked until the
    /// caller's transaction ends, so concurrent creates wait, and a rollback hands the number back.
    /// </summary>
    internal static async Task<string> NextAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        SequenceModel sequence,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var year = utcNow.UtcDateTime.Year;
        await using var command = new NpgsqlCommand(NextValueSql, connection);
        command.Parameters.AddWithValue("sequence", sequence.Id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("period", SequenceFormat.Period(sequence.Format, year));
        var lastValue = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return SequenceFormat.Format(sequence.Format, year, lastValue);
    }
}
