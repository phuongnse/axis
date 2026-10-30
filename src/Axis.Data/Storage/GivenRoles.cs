using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// Gives and removes the roles that an administrator gives a person, as rows of <c>axis.given_roles</c>. A null application id
/// is a platform role, whose row holds its kind. Both steps run in the caller's transaction and report whether they changed a
/// row, so a repeat is not a change. A read lists the roles of one person.
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
