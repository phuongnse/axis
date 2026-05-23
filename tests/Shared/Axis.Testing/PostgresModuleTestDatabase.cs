using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Testing;

public static partial class PostgresModuleTestDatabase
{
    private static readonly Regex DatabaseNamePattern = DatabaseNameRegex();

    public static async Task<string> CreateAsync(string adminConnectionString, string databaseName)
    {
        ValidateDatabaseName(databaseName);

        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = "SELECT 1 FROM pg_catalog.pg_database WHERE datname = @name";
            existsCommand.Parameters.AddWithValue("name", databaseName);
            object? exists = await existsCommand.ExecuteScalarAsync();
            if (exists is not null)
            {
                NpgsqlConnectionStringBuilder builder = new(adminConnectionString) { Database = databaseName };
                return builder.ToString();
            }
        }

        string quotedName = QuoteIdentifier(databaseName);
        await using NpgsqlCommand createCommand = connection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE {quotedName}";
        await createCommand.ExecuteNonQueryAsync();

        NpgsqlConnectionStringBuilder connectionString = new(adminConnectionString) { Database = databaseName };
        return connectionString.ToString();
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

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!DatabaseNamePattern.IsMatch(databaseName))
        {
            throw new ArgumentException(
                "Database name must start with a letter or underscore and contain only letters, digits, and underscores.",
                nameof(databaseName));
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex DatabaseNameRegex();
}
