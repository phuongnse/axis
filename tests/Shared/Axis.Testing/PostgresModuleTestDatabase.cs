using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Testing;

public static class PostgresModuleTestDatabase
{
    public static async Task<string> CreateAsync(string adminConnectionString, string databaseName)
    {
        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""CREATE DATABASE "{databaseName}" """;
        await command.ExecuteNonQueryAsync();

        NpgsqlConnectionStringBuilder builder = new(adminConnectionString) { Database = databaseName };
        return builder.ToString();
    }

    public static async Task MigrateAsync<TContext>(
        string connectionString,
        Func<DbContextOptions<TContext>, TContext> contextFactory)
        where TContext : DbContext
    {
        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using TContext context = contextFactory(options);
        await context.Database.MigrateAsync();
    }
}
