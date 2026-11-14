using System.Text.Json;
using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Reads and writes <c>axis.package_runners</c> of the tenant database, see
/// <c>docs/reference/user-administration.md#package-runners</c>. The database clock is the only clock used.
/// </summary>
public static class PackageRunners
{
    private const string InsertSql =
        """
        INSERT INTO "axis"."package_runners" ("id", "name", "client_id") VALUES (@id, @name, @client)
        """;

    private const string ListActiveSql =
        """
        SELECT "id", "name", "client_id", "packages"::text, "last_call_at", "created_at"
        FROM "axis"."package_runners" WHERE "revoked_at" IS NULL ORDER BY "created_at", "id"
        """;

    private const string FindSql =
        """
        SELECT "client_id", "revoked_at" FROM "axis"."package_runners" WHERE "id" = @id
        """;

    private const string RevokeSql =
        """
        UPDATE "axis"."package_runners" SET "revoked_at" = now() WHERE "id" = @id AND "revoked_at" IS NULL
        """;

    /// <summary>Stores a new runner in <paramref name="transaction"/>, with no packages, no last call and no revoke time.</summary>
    public static async Task InsertAsync(
        NpgsqlTransaction transaction, Guid id, string name, string clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        await using var command = new NpgsqlCommand(InsertSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("client", clientId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads the runners that are not revoked, oldest first.</summary>
    public static async Task<IReadOnlyList<PackageRunner>> ListActiveAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var runners = new List<PackageRunner>();
        await using var command = new NpgsqlCommand(ListActiveSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runners.Add(new PackageRunner(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadPackages(reader.GetString(3)),
                await reader.IsDBNullAsync(4, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken),
                await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)));
        }

        return runners;
    }

    /// <summary>Reads the client id and revoke time of a runner, or <c>null</c> when there is no such runner.</summary>
    public static async Task<(string ClientId, DateTimeOffset? RevokedAt)?> FindAsync(
        NpgsqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(FindSql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetString(0),
            await reader.IsDBNullAsync(1, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken));
    }

    /// <summary>
    /// Sets the revoke time of the runner to now in <paramref name="transaction"/>. It returns whether it changed a row, so a runner that
    /// was revoked keeps its first revoke time and a repeat returns <c>false</c>.
    /// </summary>
    public static async Task<bool> RevokeAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(RevokeSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static IReadOnlyList<PackageRunnerPackage> ReadPackages(string json)
    {
        using var document = JsonDocument.Parse(json);
        return
        [
            .. document.RootElement.EnumerateArray().Select(package => new PackageRunnerPackage(
                package.GetProperty("id").GetString()!,
                package.GetProperty("version").GetString()!)),
        ];
    }
}

/// <summary>A runner that is not revoked, as the list reads it.</summary>
public sealed record PackageRunner(
    Guid Id,
    string Name,
    string ClientId,
    IReadOnlyList<PackageRunnerPackage> Packages,
    DateTimeOffset? LastCallAt,
    DateTimeOffset CreatedAt);

/// <summary>A package a runner registered.</summary>
public sealed record PackageRunnerPackage(string Id, string Version);
