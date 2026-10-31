using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// Reads a limit setting and sets PostgreSQL's <c>statement_timeout</c> for one transaction. The limit is
/// set with <c>SET LOCAL</c> semantics, so it never reaches the next user of a pooled connection. It also
/// sets a limit for a connection's whole run and resets it afterwards.
/// </summary>
public static class StatementTimeout
{
    /// <summary>The duration in <paramref name="setting"/>, or <paramref name="fallback"/> when it is missing.</summary>
    /// <exception cref="InvalidOperationException">The value is not a duration of at least one millisecond that PostgreSQL accepts.</exception>
    public static TimeSpan Read(IConfiguration configuration, string setting, TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);

        if (configuration[setting] is not { Length: > 0 } text)
        {
            return fallback;
        }

        // A limit of 0 ms means no limit to PostgreSQL, and statement_timeout holds an int of milliseconds.
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value)
            || value < TimeSpan.FromMilliseconds(1)
            || value.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException($"{setting} must be a duration of at least one millisecond, such as 00:00:10.");
        }

        return value;
    }

    /// <summary>Sets <c>statement_timeout</c> to <paramref name="limit"/> until <paramref name="transaction"/> ends.</summary>
    public static async Task SetLocalAsync(NpgsqlTransaction transaction, TimeSpan limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand("SELECT set_config('statement_timeout', @milliseconds, true)", transaction.Connection, transaction);
        command.Parameters.AddWithValue("milliseconds", ((long)limit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Sets <c>statement_timeout</c> to <paramref name="limit"/> for the session, until <see cref="ResetAsync"/>.</summary>
    public static async Task SetForConnectionAsync(NpgsqlConnection connection, TimeSpan limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand("SELECT set_config('statement_timeout', @milliseconds, false)", connection);
        command.Parameters.AddWithValue("milliseconds", ((long)limit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Returns <c>statement_timeout</c> to the role's or database's own default.</summary>
    public static async Task ResetAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand("RESET statement_timeout", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
