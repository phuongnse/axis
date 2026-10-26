using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.DataSources;
using Axis.Data.Naming;
using Axis.Data.Seeding;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The asset management sample declares no module, package or connector, so it compiles and activates for a
/// tenant that holds no deployment binding. Its seeds fill the categories and locations, and its dashboard data
/// sources read the assets of two currencies in the application time zone.
/// </summary>
public sealed class AssetManagementActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "asset-management");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sample_activates_and_its_seeds_hold_the_categories_and_the_locations()
    {
        var compiled = await CompileAsync();
        Assert.Empty(compiled.Diagnostics);

        var activated = await ActivateAsync(compiled);
        Assert.Empty(activated.Diagnostics);
        await using var connection = await OpenAsync(pooling: true);
        var seeded = await SeedApplier.ApplyAsync(compiled.Model!, compiled.Release!.Id, connection, CancellationToken);

        Assert.Empty(seeded.Diagnostics);
        Assert.Equal(["Furniture", "Laptop", "Monitor", "Phone"], await ReadNamesAsync(compiled, "Category"));
        Assert.Equal(["Da Nang office", "Hanoi office", "Ho Chi Minh City office"], await ReadNamesAsync(compiled, "Location"));
        await using var reader = database.CreateConfigurationContext();
        var active = await new ActiveReleaseStore(reader).FindByNameAsync("AssetManagement", CancellationToken);
        Assert.NotNull(active);
        Assert.Equal(compiled.Release.Id, active.ReleaseId);
    }

    [Fact]
    public async Task The_dashboard_totals_each_currency_per_location_counts_the_statuses_and_reads_the_warranties_in_the_application_time_zone()
    {
        var compiled = await CompileAsync();
        Assert.Empty(compiled.Diagnostics);
        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);
        await using (var seeding = await OpenAsync(pooling: true))
        {
            Assert.Empty((await SeedApplier.ApplyAsync(compiled.Model!, compiled.Release!.Id, seeding, CancellationToken)).Diagnostics);
        }

        await InsertAssetsAsync(compiled);

        // 18:00 UTC on 31 October is already 1 November in Ho Chi Minh City. The warranties that end on 1 November
        // and 1 December count, because 1 December is 30 days ahead. The ones that end on 2 December and 15 October
        // do not. In UTC, today is 31 October and only the warranty of 1 November would count.
        // The session setting fixes the transaction time of the query, so the connection is not pooled.
        await using var connection = await OpenAsync(pooling: false);
        await using (var setting = new NpgsqlCommand("SELECT set_config('axis.now', '2026-10-31T18:00:00Z', false)", connection))
        {
            await setting.ExecuteScalarAsync(CancellationToken);
        }

        var value = await ListAsync(connection, compiled, "AssetValueByLocation");
        Assert.Equal(
            [
                ("Hanoi office", "USD", 1500m),
                ("Hanoi office", "VND", 20000000m),
                ("Ho Chi Minh City office", "USD", 300m),
            ],
            value.Select(row => (row.Labels["location"], row.Values["currency"]!.GetValue<string>(), Decimal(row, "totalCost"))).Order());

        var statuses = await ListAsync(connection, compiled, "AssetsByStatus");
        Assert.Equal(
            [("assigned", 1L), ("in-stock", 2L), ("retired", 1L)],
            statuses.Select(row => (row.Values["status"]!.GetValue<string>(), row.Values["assetCount"]!.GetValue<long>())).Order());

        var warranties = Assert.Single(await ListAsync(connection, compiled, "WarrantiesEndingSoon"));
        Assert.Equal(2L, warranties.Values["endingCount"]!.GetValue<long>());
    }

    private async Task<ReleaseCompilationResult> CompileAsync()
    {
        await using var context = database.CreateConfigurationContext();
        return await ReleaseCompiler.CompileAsync(_appFolder, context, SamplePackages.Versions, CancellationToken);
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("acme"), OpenInstanceCounter.Instance, CancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(bool pooling)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = pooling }.ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private async Task<List<string>> ReadNamesAsync(ReleaseCompilationResult compiled, string entity)
    {
        var table = Table(compiled, entity);
        await using var connection = await OpenAsync(pooling: true);
        await using var command = new NpgsqlCommand($"SELECT f_name FROM {table} ORDER BY f_name COLLATE \"C\"", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task InsertAssetsAsync(ReleaseCompilationResult compiled)
    {
        var hanoi = await LocationIdAsync(compiled, "Hanoi office");
        var hoChiMinhCity = await LocationIdAsync(compiled, "Ho Chi Minh City office");
        (string Name, Guid Location, string Currency, decimal Cost, string Status, DateOnly WarrantyEnd)[] assets =
        [
            ("Laptop A", hanoi, "USD", 1000m, "in-stock", new DateOnly(2026, 11, 1)),
            ("Laptop B", hanoi, "USD", 500m, "in-stock", new DateOnly(2026, 12, 1)),
            ("Server", hanoi, "VND", 20000000m, "assigned", new DateOnly(2026, 12, 2)),
            ("Monitor", hoChiMinhCity, "USD", 300m, "retired", new DateOnly(2026, 10, 15)),
        ];
        await using var connection = await OpenAsync(pooling: true);
        foreach (var asset in assets)
        {
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {Table(compiled, "Asset")} (id, f_name, f_location, f_currency, f_purchasecost, f_status, f_warrantyend) " +
                "VALUES (@id, @name, @location, @currency, @cost, @status, @warrantyEnd)",
                connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("name", asset.Name);
            command.Parameters.AddWithValue("location", asset.Location);
            command.Parameters.AddWithValue("currency", asset.Currency);
            command.Parameters.AddWithValue("cost", asset.Cost);
            command.Parameters.AddWithValue("status", asset.Status);
            command.Parameters.AddWithValue("warrantyEnd", asset.WarrantyEnd);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    private async Task<Guid> LocationIdAsync(ReleaseCompilationResult compiled, string name)
    {
        await using var connection = await OpenAsync(pooling: true);
        await using var command = new NpgsqlCommand($"SELECT id FROM {Table(compiled, "Location")} WHERE f_name = @name", connection);
        command.Parameters.AddWithValue("name", name);
        return (Guid)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static async Task<IReadOnlyList<DataSourceRow>> ListAsync(NpgsqlConnection connection, ReleaseCompilationResult compiled, string name)
    {
        var model = compiled.Model!;
        var dataSource = model.DataSources.Single(candidate => candidate.Name == name);
        var page = await DataSourceQueries.ListAsync(connection, null, model, dataSource, 1, 100, null, [], cancellationToken: CancellationToken);
        Assert.NotNull(page);
        return page.Items;
    }

    private static decimal Decimal(DataSourceRow row, string measure) => decimal.Parse(row.Values[measure]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);

    private static string Table(ReleaseCompilationResult compiled, string entity) =>
        EntityNaming.QualifiedTable(EntityNaming.Table(compiled.Model!.Entities.Single(candidate => candidate.Name == entity).Id));
}
