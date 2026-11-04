using Axis.ControlPlane.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>Helpers for the tests that use the control plane's tables directly.</summary>
internal static class ControlPlaneDatabase
{
    /// <summary>Applies the migrations as the start of the service does.</summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString)
            .Options;
        await using var context = new ControlPlaneDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public static async Task<int> ExecuteAsync(NpgsqlDataSource dataSource, string sql, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(NpgsqlDataSource dataSource, string sql, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
