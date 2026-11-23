using Npgsql;

namespace Axis.Configuration.Storage;

/// <summary>
/// The retention rule of an authoring request step's model messages, which the worker applies: the messages are cleared
/// 30 days after the step's <c>finished_at</c> and the step stays, see <c>docs/reference/authoring-requests.md#durable-requests</c>.
/// </summary>
public static class AuthoringMessageRetention
{
    /// <summary>How long a finished step keeps its model messages.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(30);

    /// <summary>How often the worker clears the old model messages of a tenant.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// Sets <c>messages</c> to null on the steps that finished more than <see cref="Period"/> before <paramref name="now"/>
    /// and still have messages. It returns how many steps it cleared. A step that finished exactly at the limit, and a step
    /// that has not finished, keep their messages.
    /// </summary>
    public static async Task<int> ClearExpiredAsync(NpgsqlConnection connection, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.authoring_request_steps SET messages = NULL WHERE messages IS NOT NULL AND finished_at < @cutoff",
            connection);
        command.Parameters.AddWithValue("cutoff", (now - Period).ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
