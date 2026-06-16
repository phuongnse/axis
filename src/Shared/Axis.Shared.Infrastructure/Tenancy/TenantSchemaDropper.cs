using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>Drops a tenant schema in a module database.</summary>
public static class TenantSchemaDropper
{
    public static async Task DropAsync(
        string connectionString,
        Guid teamAccountId,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken)
    {
        string schema = $"tenant_{teamAccountId:N}";

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand dropSchema = connection.CreateCommand();
        dropSchema.CommandText = $"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""";
        await dropSchema.ExecuteNonQueryAsync(cancellationToken);

        logger.LogInformation(
            "{Module} tenant schema {Schema} dropped",
            moduleName,
            schema);
    }
}
