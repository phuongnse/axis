using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The first two steps of deleting a tenant, see <c>docs/reference/tenant-lifecycle.md#deleting-a-tenant</c>: the <c>delete</c> job's
/// <c>schedule</c> step suspends the tenant and its preview tenant and sets <c>delete_after</c> 7 days ahead, and the
/// <c>cancel-deletion</c> job's <c>cancel</c> step clears it within that window. Each step changes the tenant and its preview
/// in one transaction, so both end changed or neither does.
/// </summary>
internal static class TenantDeletion
{
    internal const string WindowEndedMessage = "The grace window of the deletion has ended.";

    /// <summary>
    /// Suspends the tenant and its preview tenant, as <c>tenant suspend</c> does, and sets their <c>delete_after</c> to 7 days
    /// from now. A rerun keeps the first date, so a second request never extends the grace window.
    /// </summary>
    public static async Task<string?> ScheduleAsync(string platformConnectionString, string tenantId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ids = new List<string> { tenantId };
        await using (var previews = new NpgsqlCommand("SELECT id FROM platform.tenants WHERE preview_of = @id", connection, transaction))
        {
            previews.Parameters.AddWithValue("id", tenantId);
            await using var reader = await previews.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetString(0));
            }
        }

        foreach (var id in ids)
        {
            var refusal = await TenantStatus.SuspendChangeAsync(connection, id, cancellationToken) switch
            {
                StatusChange.Unknown => TenantStatus.UnknownMessage,
                StatusChange.Provisioning => TenantStatus.ProvisioningMessage,
                StatusChange.Refused => TenantStatus.SuspendedMessage,
                _ => null,
            };
            if (refusal is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return refusal;
            }
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE platform.tenants SET delete_after = coalesce(delete_after, now() + interval '7 days') WHERE id = @id OR preview_of = @id",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("id", tenantId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>
    /// Clears <c>delete_after</c> on the tenant and its preview tenant and leaves them <c>suspended</c>. A tenant with no
    /// scheduled deletion is a no-op. A deletion date that has passed is refused and stays.
    /// </summary>
    public static async Task<string?> CancelAsync(string platformConnectionString, string tenantId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        bool ended;
        await using (var read = new NpgsqlCommand(
            "SELECT delete_after <= now() FROM platform.tenants WHERE id = @id FOR UPDATE",
            connection,
            transaction))
        {
            read.Parameters.AddWithValue("id", tenantId);
            switch (await read.ExecuteScalarAsync(cancellationToken))
            {
                case null:
                    await transaction.RollbackAsync(cancellationToken);
                    return TenantStatus.UnknownMessage;
                case DBNull:
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                case bool value:
                    ended = value;
                    break;
                default:
                    throw new InvalidOperationException("The deletion date could not be read.");
            }
        }

        if (ended)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WindowEndedMessage;
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE platform.tenants SET delete_after = NULL WHERE id = @id OR preview_of = @id",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("id", tenantId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }
}
