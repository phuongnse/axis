using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Shared.Infrastructure.Workspaces;

/// <summary>Drops a workspace schema in a module database.</summary>
public static class WorkspaceSchemaDropper
{
    public static async Task DropAsync(
        string connectionString,
        Guid workspaceId,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken)
    {
        string schema = $"workspace_{workspaceId:N}";

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand dropSchema = connection.CreateCommand();
        dropSchema.CommandText = $"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""";
        await dropSchema.ExecuteNonQueryAsync(cancellationToken);

        logger.LogInformation(
            "{Module} workspace schema {Schema} dropped",
            moduleName,
            schema);
    }
}
