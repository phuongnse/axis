using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The SearchPerformanceApp fixture and its articles, for the tests of the trigram indexes. Article has
/// a display field <c>title</c> and a searchable <c>note</c>, so it has exactly two trigram indexes.
/// </summary>
internal static class SearchApp
{
    /// <summary>The word that a few articles hold in <c>title</c> or <c>note</c>. No MD5 hash holds it, because it is not hexadecimal.</summary>
    public const string RareWord = "zephyr";

    /// <summary>
    /// Activates the fixture, which creates the trigram index of each searched column on the empty
    /// table, then inserts <paramref name="count"/> articles and returns the model. Article <c>n</c> has the
    /// title <c>Article n</c> and the note <c>md5(n)</c>. The title of every <paramref name="every"/>th article, from the
    /// <paramref name="every"/>th, ends with the rare word, and so does the note of each one that is halfway between two of them.
    /// </summary>
    public static async Task<ApplicationModel> SeedAsync(string connectionString, int count, int every, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "SearchPerformanceApp"), configuration, cancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, cancellationToken);
        Assert.Empty(activated.Diagnostics);
        var model = compiled.Model!;
        Assert.True(model.TryGetEntity("Article", out var article));

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(article.Id));
        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var title = EntityNaming.Quote(EntityNaming.Column("title"));
        var note = EntityNaming.Quote(EntityNaming.Column("note"));
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var insert = dataSource.CreateCommand(
            $"""
            INSERT INTO {table} ({id}, {title}, {note})
            SELECT gen_random_uuid(),
                'Article ' || n || CASE WHEN n % {every} = 0 THEN ' {RareWord}' ELSE '' END,
                md5(n::text) || CASE WHEN n % {every} = {every / 2} THEN ' {RareWord}' ELSE '' END
            FROM generate_series(1, {count}) AS n
            """))
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        // Updates the planner's statistics and flushes the pending list of each GIN index, as
        // autovacuum does in production. VACUUM cannot run in a transaction.
        await using (var vacuum = dataSource.CreateCommand($"VACUUM ANALYZE {table}"))
        {
            await vacuum.ExecuteNonQueryAsync(cancellationToken);
        }

        return model;
    }

    /// <summary>The names of the two trigram indexes of Article: its display field and its searchable field.</summary>
    public static string[] TrigramIndexes(EntityModel article)
    {
        var table = EntityNaming.Table(article.Id);
        return [EntityNaming.TrigramIndex(table, EntityNaming.Column("title")), EntityNaming.TrigramIndex(table, EntityNaming.Column("note"))];
    }
}
