using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>The release history: a row for each activation that changes the active release, written with it.</summary>
public sealed class ReleaseHistoryTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string BeforeReleaseHistory = "20261017052526_CreateReleaseComputedFingerprints";

    private static readonly Guid _textId = Guid.NewGuid();

    // Each test uses its own application id and names, so tests sharing the database do not see each other's rows.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _sitePath = $"site-{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_number_is_kept_for_a_release_and_the_active_release_adds_no_row()
    {
        using var folder = Folder("Sales");
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first, ActivationActor.Startup)).Diagnostics);

        // The release that is already active: no copy.
        Assert.Empty((await ActivateAsync(first, ActivationActor.Startup)).Diagnostics);

        var rows = await ListAsync();
        var only = Assert.Single(rows);
        Assert.Equal((first.Release!.Id, 1, first.Release.ContentHash, "startup"), (only.ReleaseId, only.Number, only.ContentHash, only.Actor));
        Assert.Null(only.RequestId);
        Assert.Null(only.ActorName);

        folder.With("texts/en.json", Texts("Shop"));
        var second = await CompileAsync(folder);
        Assert.NotEqual(first.Release.Id, second.Release!.Id);
        Assert.Empty((await ActivateAsync(second, ActivationActor.Operator)).Diagnostics);

        // The first release again, after another one: a row that keeps its number.
        Assert.Empty((await ActivateAsync(first, ActivationActor.Startup)).Diagnostics);

        rows = await ListAsync();
        Assert.Equal([1, 2, 1], rows.Select(row => row.Number));
        Assert.Equal([first.Release.Id, second.Release.Id, first.Release.Id], rows.Select(row => row.ReleaseId));
        Assert.Equal(["startup", "operator", "startup"], rows.Select(row => row.Actor));
        Assert.Equal(first.Release.Id, (await FindActiveAsync())?.ReleaseId);

        // A third release takes the next number.
        folder.With("texts/en.json", Texts("Store"));
        var third = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(third, ActivationActor.Startup)).Diagnostics);
        Assert.Equal([3, 1, 2, 1], (await ListAsync()).Select(row => row.Number));
    }

    [Fact]
    public async Task A_user_actor_stores_its_name_label_note_and_request()
    {
        using var folder = Folder("Sales");
        var compiled = await CompileAsync(folder);
        var requestId = Guid.NewGuid();
        var actor = ActivationActor.User("hoa", "Hoa Author", "Spring", "Fixes the title.", requestId);

        Assert.Empty((await ActivateAsync(compiled, actor)).Diagnostics);

        var row = Assert.Single(await ListAsync());
        Assert.Equal(
            ("hoa", "Hoa Author", "Spring", "Fixes the title.", requestId, 1),
            (row.Actor, row.ActorName, row.Label, row.Note, row.RequestId, row.Number));
        Assert.Equal(compiled.Release!.Id, row.ReleaseId);
    }

    [Fact]
    public async Task Numbers_belong_to_one_application()
    {
        using var folder = Folder("Sales");
        Assert.Empty((await ActivateAsync(await CompileAsync(folder), ActivationActor.Startup)).Diagnostics);
        folder.With("texts/en.json", Texts("Shop"));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder), ActivationActor.Startup)).Diagnostics);

        var otherId = Guid.NewGuid();
        using var other = new TemporaryFolder()
            .With("application.json", Manifest(otherId, $"App{Guid.NewGuid():N}"));
        Assert.Empty((await ActivateAsync(await CompileAsync(other), ActivationActor.Startup)).Diagnostics);

        Assert.Equal([2, 1], (await ListAsync()).Select(row => row.Number));
        Assert.Equal([1], (await ListAsync(otherId)).Select(row => row.Number));
    }

    [Fact]
    public async Task A_site_path_refused_inside_the_write_adds_no_history_row()
    {
        var otherId = Guid.NewGuid();
        using var other = Folder("Other", otherId, $"App{Guid.NewGuid():N}");
        Assert.Empty((await ActivateAsync(await CompileAsync(other), ActivationActor.Startup)).Diagnostics);
        using var folder = Folder("Sales");
        var compiled = await CompileAsync(folder);

        // A store that does not see the other application's path is refused it by the unique index.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var result = await ReleaseActivator.ActivateAsync(
            compiled, new ConflictHidingStore(new ActiveReleaseStore(configuration)), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);

        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(await ListAsync());
        Assert.Null(await FindActiveAsync());
        Assert.Single(await ListAsync(otherId));
    }

    [Fact]
    public async Task A_history_row_cannot_be_changed_or_removed()
    {
        using var folder = Folder("Sales");
        Assert.Empty((await ActivateAsync(await CompileAsync(folder), ActivationActor.Startup)).Diagnostics);

        var update = await Assert.ThrowsAsync<PostgresException>(
            () => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"UPDATE axis.release_activations SET actor = 'x' WHERE application_id = '{_applicationId}'", CancellationToken));
        var delete = await Assert.ThrowsAsync<PostgresException>(
            () => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"DELETE FROM axis.release_activations WHERE application_id = '{_applicationId}'", CancellationToken));
        var truncate = await Assert.ThrowsAsync<PostgresException>(
            () => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, "TRUNCATE axis.release_activations", CancellationToken));

        Assert.Equal(["23001", "23001", "23001"], [update.SqlState, delete.SqlState, truncate.SqlState]);
        Assert.Equal("startup", Assert.Single(await ListAsync()).Actor);
    }

    [Fact]
    public async Task Migrating_a_database_with_an_active_release_adds_one_row_for_it_with_number_1_and_the_actor_unknown()
    {
        var connectionString = await PostgreSqlServer.CreateDatabaseAsync("release_history_backfill", CancellationToken);
        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        await context.GetService<IMigrator>().MigrateAsync(BeforeReleaseHistory, CancellationToken);
        var releaseId = Guid.NewGuid();
        var activatedAt = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);
        var hash = new string('a', 64);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.releases (id, application_id, content_hash, created_at)
            VALUES ({releaseId}, {_applicationId}, {hash}, {activatedAt})
            """,
            CancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.active_releases (application_id, name, release_id, activated_at)
            VALUES ({_applicationId}, {_name}, {releaseId}, {activatedAt})
            """,
            CancellationToken);

        await context.Database.MigrateAsync(CancellationToken);

        var row = Assert.Single(await new ActiveReleaseStore(context).ListActivationsAsync(_applicationId, CancellationToken));
        Assert.Equal((releaseId, 1, hash, activatedAt, "unknown"), (row.ReleaseId, row.Number, row.ContentHash, row.ActivatedAt, row.Actor));
        Assert.Null(row.ActorName);
        Assert.Null(row.RequestId);
        Assert.Equal(1L, (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, "SELECT count(*) FROM axis.release_activations", CancellationToken))!);
    }

    private async Task<IReadOnlyList<ReleaseActivation>> ListAsync(Guid? applicationId = null)
    {
        await using var configuration = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(configuration).ListActivationsAsync(applicationId ?? _applicationId, CancellationToken);
    }

    private async Task<ActiveRelease?> FindActiveAsync()
    {
        await using var configuration = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(configuration).FindByApplicationIdAsync(_applicationId, CancellationToken);
    }

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, ActivationActor actor)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance,
            ReleaseActivator.DefaultInlineRecomputeLimit, actor, CancellationToken);
    }

    // An application with a site at the test's path and a text, so a text edit makes another release.
    private TemporaryFolder Folder(string title, Guid? applicationId = null, string? name = null) =>
        new TemporaryFolder()
            .With("application.json", Manifest(applicationId ?? _applicationId, name ?? _name))
            .With("texts/en.json", Texts(title))
            .With("pages/orders.json", Page(Guid.NewGuid()))
            .With("entities/order.json", Order(Guid.NewGuid()))
            .With("sites/main.json", Site(Guid.NewGuid(), _sitePath));

    private static string Manifest(Guid id, string name) =>
        $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""";

    private static string Order(Guid id) =>
        $$"""{ "id": "{{id}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ { "name": "number", "type": "text" } ] }""";

    private static string Texts(string title) =>
        $$"""
        { "id": "{{_textId}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "{{title}}", "nav.orders": "Orders", "orders.title": "Orders" } }
        """;

    private static string Page(Guid id) =>
        $$"""
        { "id": "{{id}}", "kind": "page", "name": "Orders", "formatVersion": 1,
          "title": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "entity": "Order" } ] }
        """;

    private static string Site(Guid id, string path) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "Main", "formatVersion": 1, "path": "{{path}}",
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Orders", "label": { "textKey": "nav.orders" } } ] }
        """;
}
