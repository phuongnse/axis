using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The limit of three sign-up emails in 24 hours for one address, resends included, see <c>docs/reference/sign-up.md#bots-and-abuse</c>.
/// The count is kept in <c>control_plane.sign_up_emails</c>, so it holds across replicas and restarts, and the table holds only the
/// SHA-256 of the normalised address. The <c>expireSignUps</c> job deletes the rows past 24 hours.
/// </summary>
internal static class SignUpEmailLimit
{
    /// <summary>The most sign-up emails an address gets in 24 hours.</summary>
    public const int Daily = 3;

    /// <summary>
    /// Counts one more email for an address in the transaction and answers true, or answers false when the address already has
    /// <see cref="Daily"/> in the last 24 hours. The lock of the address makes two parallel tries count one after the other, and it
    /// ends with the transaction, so a transaction that rolls back counts nothing.
    /// </summary>
    public static async Task<bool> TryCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(SignUpEmail.Normalise(email)));
        await using (var @lock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('control_plane.sign_up_emails:' || encode(@hash, 'hex')))", connection, transaction))
        {
            @lock.Parameters.AddWithValue("hash", hash);
            await @lock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM control_plane.sign_up_emails WHERE address_hash = @hash AND queued_at > now() - interval '24 hours'",
            connection,
            transaction))
        {
            count.Parameters.AddWithValue("hash", hash);
            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= Daily)
            {
                return false;
            }
        }

        await using var insert = new NpgsqlCommand("INSERT INTO control_plane.sign_up_emails (id, address_hash) VALUES (@id, @hash)", connection, transaction);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("hash", hash);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
