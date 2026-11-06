using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// Gives and removes the roles that an administrator gives a person, as rows of <c>axis.given_roles</c>. A null application id
/// is a platform role, whose row holds its kind. Both steps run in the caller's transaction and report whether they changed a
/// row, so a repeat is not a change. A move gives every role of one person to another id. A read lists the roles of one person, and another lists the roles of every
/// person in a transaction. A third lists the people who hold one platform role.
/// </summary>
public static class GivenRoles
{
    /// <summary>Lists the roles given to the person: platform roles first, then application roles by application and role name.</summary>
    public static async Task<IReadOnlyList<GivenRole>> ListAsync(NpgsqlConnection connection, string userId, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT application_id, role FROM axis.given_roles WHERE user_id = @user ORDER BY application_id NULLS FIRST, role",
            connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roles = new List<GivenRole>();
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new GivenRole(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1)));
        }

        return roles;
    }

    /// <summary>Lists the roles of every person in the transaction's snapshot, ordered by person, then platform roles first, then by application and role name.</summary>
    public static async Task<IReadOnlyList<(string UserId, GivenRole Role)>> ListAllAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT user_id, application_id, role FROM axis.given_roles ORDER BY user_id, application_id NULLS FIRST, role",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roles = new List<(string, GivenRole)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add((reader.GetString(0), new GivenRole(reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2))));
        }

        return roles;
    }

    /// <summary>Lists the ids of the people who hold the platform role of this kind, ordered by id.</summary>
    public static async Task<IReadOnlyList<string>> ListPlatformRoleHoldersAsync(
        NpgsqlConnection connection, string kind, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT user_id FROM axis.given_roles WHERE application_id IS NULL AND role = @kind ORDER BY user_id",
            connection);
        command.Parameters.AddWithValue("kind", kind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var holders = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            holders.Add(reader.GetString(0));
        }

        return holders;
    }

    /// <summary>Gives the person the role in the application, or the platform role with no application, and returns whether it was new.</summary>
    public static async Task<bool> GiveAsync(
        NpgsqlTransaction transaction, string userId, Guid? applicationId, string role, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.given_roles (id, user_id, application_id, role)
            VALUES (@id, @user, @application, @role)
            ON CONFLICT (user_id, application_id, role) DO NOTHING
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlDbType.Uuid) { Value = (object?)applicationId ?? DBNull.Value });
        command.Parameters.AddWithValue("role", role);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Gives every role of <paramref name="fromUserId"/> to <paramref name="toUserId"/>, which holds none, and returns how many rows moved.</summary>
    public static async Task<int> MoveAsync(
        NpgsqlTransaction transaction, string fromUserId, string toUserId, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.given_roles SET user_id = @to WHERE user_id = @from",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("from", fromUserId);
        command.Parameters.AddWithValue("to", toUserId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Removes the role of the person in the application, or the platform role with no application, and returns whether they held it.</summary>
    public static async Task<bool> RemoveAsync(
        NpgsqlTransaction transaction, string userId, Guid? applicationId, string role, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.given_roles WHERE user_id = @user AND application_id IS NOT DISTINCT FROM @application AND role = @role",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlDbType.Uuid) { Value = (object?)applicationId ?? DBNull.Value });
        command.Parameters.AddWithValue("role", role);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}

/// <summary>A role given to a person. A null <c>ApplicationId</c> means a platform role, whose <c>Role</c> is its kind.</summary>
public sealed record GivenRole(Guid? ApplicationId, string Role);
