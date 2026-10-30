using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// Gives and removes the roles that an administrator gives a person, as rows of <c>axis.given_roles</c>. A null application id
/// is a platform role, whose row holds its kind. Both steps run in the caller's transaction and report whether they changed a
/// row, so a repeat is not a change.
/// </summary>
public static class GivenRoles
{
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
