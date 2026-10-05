using System.Data.Common;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ReleaseCompilerTests(ConfigurationDatabaseFixture database) : IClassFixture<ConfigurationDatabaseFixture>
{
    private const string Order = """
        {
          "id": "11111111-1111-4111-8111-111111111111",
          "kind": "entity",
          "name": "Order",
          "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "maxLength": 20 } ]
        }
        """;

    private const string CanonicalOrder =
        """{"fields":[{"maxLength":20,"name":"number","type":"text"}],"formatVersion":1,"id":"11111111-1111-4111-8111-111111111111","kind":"entity","name":"Order"}""";

    // Each test uses its own application id, so tests sharing the database do not see each other's releases.
    private readonly Guid _applicationId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_history_is_kept_in_the_module_table()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("""SELECT "MigrationId" FROM axis.__configuration_migrations""");

        var migration = await command.ExecuteScalarAsync(CancellationToken);

        Assert.EndsWith("_CreateReleases", Assert.IsType<string>(migration), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiling_an_unchanged_folder_twice_stores_one_release()
    {
        using var folder = SampleFolder();

        var first = await CompileAsync(folder);
        var second = await CompileAsync(folder);

        Assert.Empty(first.Diagnostics);
        Assert.NotNull(first.Release);
        Assert.NotNull(second.Release);
        Assert.Equal(first.Release.Id, second.Release.Id);
        Assert.Equal(first.Release.ContentHash, second.Release.ContentHash);
        Assert.Equal(1, await CountReleasesAsync());
    }

    [Fact]
    public async Task Formatting_only_copy_returns_the_existing_release()
    {
        using var folder = SampleFolder();
        using var reformatted = new TemporaryFolder()
            .With("application.json", $$"""{"formatVersion":1,"name":"Sample","kind":"application","id":"{{_applicationId}}"}""")
            .With("entities/order.json", Order.Replace("\n", "\r\n    ", StringComparison.Ordinal));

        var original = await CompileAsync(folder);
        var copy = await CompileAsync(reformatted);

        Assert.NotNull(original.Release);
        Assert.Equal(original.Release.Id, copy.Release?.Id);
        Assert.Equal(1, await CountReleasesAsync());
    }

    [Fact]
    public async Task Changed_folder_stores_a_second_release()
    {
        using var folder = SampleFolder();
        using var changed = SampleFolder(Order.Replace("\"maxLength\": 20", "\"maxLength\": 40", StringComparison.Ordinal));

        var original = await CompileAsync(folder);
        var next = await CompileAsync(changed);

        Assert.NotNull(original.Release);
        Assert.NotNull(next.Release);
        Assert.NotEqual(original.Release.Id, next.Release.Id);
        Assert.NotEqual(original.Release.ContentHash, next.Release.ContentHash);
        Assert.Equal(2, await CountReleasesAsync());
    }

    [Fact]
    public async Task Folder_with_an_error_stores_no_release_and_returns_its_diagnostics()
    {
        using var folder = SampleFolder(Order.Replace("\"text\"", "\"reference\"", StringComparison.Ordinal));
        await using var context = database.CreateContext();
        var releasesBefore = await context.Releases.CountAsync(CancellationToken);

        var result = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);

        Assert.Null(result.Release);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(0, await CountReleasesAsync());
        Assert.Equal(releasesBefore, await context.Releases.CountAsync(CancellationToken));
    }

    [Fact]
    public async Task Release_read_back_has_the_same_hash_and_canonical_resources()
    {
        using var folder = SampleFolder();
        var compiled = await CompileAsync(folder);
        Assert.NotNull(compiled.Release);

        var stored = await ReadReleaseAsync(compiled.Release.Id);

        Assert.Equal(_applicationId, stored.ApplicationId);
        Assert.Equal(compiled.Release.ContentHash, stored.ContentHash);
        Assert.Equal(
            compiled.Release.Resources.Select(resource => (resource.Path, resource.Content)),
            stored.Resources.Select(resource => (resource.Path, resource.Content)));
        Assert.Equal(["application.json", "entities/order.json"], stored.Resources.Select(resource => resource.Path));
        Assert.Equal(CanonicalOrder, stored.Resources[1].Content);
        Assert.Equal(ContentHash.Compute([.. stored.Resources.Select(resource => new ResourceContent(resource.Path, resource.Content))]), stored.ContentHash);
    }

    public static TheoryData<string> UpdateAttempts => ["modify release", "remove release", "modify resource", "add resource"];

    [Theory]
    [MemberData(nameof(UpdateAttempts))]
    public async Task Stored_release_cannot_be_changed(string attempt)
    {
        using var folder = SampleFolder();
        var compiled = await CompileAsync(folder);
        Assert.NotNull(compiled.Release);
        var before = await ReadReleaseAsync(compiled.Release.Id);

        await using var context = database.CreateContext();
        var release = await context.Releases.Include(r => r.Resources).SingleAsync(r => r.Id == compiled.Release.Id, CancellationToken);
        switch (attempt)
        {
            case "modify release":
                context.Entry(release).Property(r => r.ContentHash).CurrentValue = new string('0', 64);
                break;
            case "remove release":
                context.Releases.Remove(release);
                break;
            case "modify resource":
                context.Entry(release.Resources[0]).Property(r => r.Content).CurrentValue = "{}";
                break;
            case "add resource":
                release.Resources.Add(new ReleaseResource { ReleaseId = release.Id, Path = "entities/extra.json", Content = "{}" });
                break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(CancellationToken));

        var after = await ReadReleaseAsync(compiled.Release.Id);
        Assert.Equal(before.ContentHash, after.ContentHash);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(2, after.Resources.Count);
        Assert.Equal(
            before.Resources.Select(resource => (resource.Path, resource.Content)),
            after.Resources.Select(resource => (resource.Path, resource.Content)));
    }

    [Fact]
    public async Task Concurrent_compiles_of_the_same_folder_return_one_release()
    {
        using var folder = SampleFolder();
        var contexts = Enumerable.Range(0, 8).Select(_ => database.CreateContext()).ToList();
        try
        {
            var results = await Task.WhenAll(contexts.Select(context => ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken)));

            Assert.All(results, result => Assert.NotNull(result.Release));
            Assert.Single(results.Select(result => result.Release!.Id).Distinct());
            Assert.Equal(1, await CountReleasesAsync());
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Compile_that_loses_the_race_returns_the_stored_release_and_keeps_the_context_usable()
    {
        using var folder = SampleFolder();
        ReleaseCompilationResult? winner = null;

        // Just before this context inserts its release, another compile stores the same release.
        var interceptor = new BeforeReleaseInsert(async () =>
        {
            winner = await CompileAsync(folder);
        });
        await using var context = database.CreateContext(interceptor);

        var loser = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);

        Assert.NotNull(winner?.Release);
        Assert.Equal(winner.Release.Id, loser.Release?.Id);
        Assert.Equal(2, loser.Release?.Resources.Count);
        Assert.Equal(1, await CountReleasesAsync());

        using var changed = SampleFolder(Order.Replace("\"maxLength\": 20", "\"maxLength\": 30", StringComparison.Ordinal));
        var next = await ReleaseCompiler.CompileAsync(changed.Path, context, CancellationToken);

        Assert.NotNull(next.Release);
        Assert.Equal(2, await CountReleasesAsync());
    }

    private TemporaryFolder SampleFolder(string order = Order) =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{_applicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1 }
                """)
            .With("entities/order.json", order);

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateContext();
        return await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
    }

    private async Task<int> CountReleasesAsync()
    {
        await using var context = database.CreateContext();
        return await context.Releases.CountAsync(release => release.ApplicationId == _applicationId, CancellationToken);
    }

    private async Task<Release> ReadReleaseAsync(Guid id)
    {
        await using var context = database.CreateContext();
        var release = await context.Releases
            .AsNoTracking()
            .Include(r => r.Resources)
            .SingleAsync(r => r.Id == id, CancellationToken);
        release.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return release;
    }

    /// <summary>Runs an action once, just before the first insert into <c>axis.releases</c>.</summary>
    private sealed class BeforeReleaseInsert(Func<Task> action) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done && command.CommandText.Contains("INSERT INTO axis.releases", StringComparison.Ordinal))
            {
                _done = true;
                await action();
            }

            return result;
        }
    }
}
