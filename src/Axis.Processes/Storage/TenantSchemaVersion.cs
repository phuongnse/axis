using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Processes.Storage;

/// <summary>
/// The schema version of a tenant database: the newest migration of the configuration, data and processes
/// contexts, compared as ordinal text. A host serves a tenant only when the version recorded for it is the
/// <see cref="Current"/> one of the host's build.
/// </summary>
public static class TenantSchemaVersion
{
    // Declared first: the static initializers run in the order of the declarations.
    private static readonly string[] _migrations = BuiltInMigrations();

    private static readonly HashSet<string> _known = [.. _migrations];

    /// <summary>The newest migration this build holds. It needs no database.</summary>
    public static string Current { get; } = Newest(_migrations);

    /// <summary>The newest migration applied to the database of <paramref name="connection"/>, or empty when none is.</summary>
    public static async Task<string> ReadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        return Newest(
            (await configuration.Database.GetAppliedMigrationsAsync(cancellationToken))
                .Concat(await data.Database.GetAppliedMigrationsAsync(cancellationToken))
                .Concat(await processes.Database.GetAppliedMigrationsAsync(cancellationToken)));
    }

    /// <summary>Whether <paramref name="version"/> is the id of a migration of the configuration, data or processes context of this build.</summary>
    public static bool IsKnown(string version) => _known.Contains(version);

    private static string[] BuiltInMigrations()
    {
        using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql().Options);
        using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql().Options);
        using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql().Options);
        return [.. configuration.Database.GetMigrations().Concat(data.Database.GetMigrations()).Concat(processes.Database.GetMigrations())];
    }

    private static string Newest(IEnumerable<string> migrations) => migrations.Max(StringComparer.Ordinal) ?? "";
}
