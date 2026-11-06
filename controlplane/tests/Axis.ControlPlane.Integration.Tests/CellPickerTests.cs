using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The cell picker on the region directory's own database on a real PostgreSQL. Each test class instance has a container of its
/// own, with the region <c>vn</c> and the cells that the test seeds.
/// </summary>
public sealed class CellPickerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellPicker _picker = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString)
            .Options;
        await using (var context = new DirectoryDbContext(options))
        {
            await context.Database.MigrateAsync(token);
        }

        // The two schemas have their own history tables, so the directory and the control plane share this database.
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), token);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await ControlPlaneDatabase.ExecuteAsync(
            _dataSource, "INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com')", token);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        _picker = new CellPicker(_dataSource, _dataSource, _loggers.CreateLogger<CellPicker>());
    }

    public async ValueTask DisposeAsync()
    {
        _loggers.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_least_full_cell_is_picked_and_the_lower_id_wins_a_tie()
    {
        // vn-1 is at 50%, vn-2 and vn-3 are both at 20%, so vn-2 wins the tie.
        await SeedAsync(("vn-1", 100, 50, true), ("vn-2", 10, 2, true), ("vn-3", 100, 20, true));
        var asked = new List<string>();

        var placement = await _picker.PlaceAsync("vn", "acme", Accept(asked), TestContext.Current.CancellationToken);

        Assert.Equal(new CellPlacement(CellPlacement.Placed, "vn-2"), placement);
        Assert.Equal(["vn-2"], asked);
    }

    [Fact]
    public async Task A_cell_at_80_percent_or_not_taking_new_tenants_is_skipped()
    {
        await SeedAsync(("vn-1", 100, 80, true), ("vn-2", 100, 10, false), ("vn-3", 100, 30, true));
        var asked = new List<string>();

        var placement = await _picker.PlaceAsync("vn", "acme", Accept(asked), TestContext.Current.CancellationToken);

        Assert.Equal("vn-3", placement.CellId);
        Assert.Equal(["vn-3"], asked);
    }

    [Fact]
    public async Task A_cell_over_80_percent_is_skipped()
    {
        await SeedAsync(("vn-1", 100, 95, true), ("vn-2", 100, 79, true));

        var placement = await _picker.PlaceAsync("vn", "acme", Accept([]), TestContext.Current.CancellationToken);

        Assert.Equal("vn-2", placement.CellId);
    }

    [Fact]
    public async Task A_placed_tenant_has_one_row_naming_its_cell_and_the_cell_count_goes_up_by_one()
    {
        await SeedAsync(("vn-1", 100, 50, true), ("vn-2", 100, 5, true));

        var placement = await _picker.PlaceAsync("vn", "acme", Accept([]), TestContext.Current.CancellationToken);

        Assert.Equal(CellPlacement.Placed, placement.State);
        Assert.Equal("vn-2", await ScalarAsync<string>("SELECT cell_id FROM directory.tenants WHERE id = 'acme'"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(6, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-2'"));
        Assert.Equal(50, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
    }

    [Fact]
    public async Task A_tenant_that_the_correction_already_added_is_placed_without_a_second_row_or_count()
    {
        await SeedAsync(("vn-1", 100, 1, true));
        await ControlPlaneDatabase.ExecuteAsync(
            _dataSource, "INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('acme', 'vn', 'vn-1')", TestContext.Current.CancellationToken);

        var placement = await _picker.PlaceAsync("vn", "acme", Accept([]), TestContext.Current.CancellationToken);

        Assert.Equal(new CellPlacement(CellPlacement.Placed, "vn-1"), placement);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants WHERE id = 'acme'"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
    }

    [Fact]
    public async Task A_full_answer_marks_the_cell_and_the_next_cell_gets_the_tenant()
    {
        await SeedAsync(("vn-1", 100, 10, true), ("vn-2", 100, 20, true));
        var asked = new List<string>();

        var placement = await _picker.PlaceAsync("vn", "acme", async (cell, _) =>
        {
            asked.Add(cell);
            await Task.CompletedTask;
            if (cell == "vn-1")
            {
                throw new CellCallException("The cell is full.", 409, cellFull: true);
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal("vn-2", placement.CellId);
        Assert.Equal(["vn-1", "vn-2"], asked);
        Assert.False(await ScalarAsync<bool>("SELECT takes_new_tenants FROM directory.cells WHERE id = 'vn-1'"));
        Assert.Equal(10, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
        Assert.Equal("vn-2", await ScalarAsync<string>("SELECT cell_id FROM directory.tenants WHERE id = 'acme'"));
        Assert.Equal(21, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-2'"));
    }

    [Fact]
    public async Task Another_409_is_rethrown_and_marks_nothing()
    {
        await SeedAsync(("vn-1", 100, 10, true), ("vn-2", 100, 20, true));
        var taken = new CellCallException("The tenant id is taken.", 409);

        var thrown = await Assert.ThrowsAsync<CellCallException>(() =>
            _picker.PlaceAsync("vn", "acme", (_, _) => throw taken, TestContext.Current.CancellationToken));

        Assert.Same(taken, thrown);
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM directory.cells WHERE takes_new_tenants"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(10, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
    }

    [Fact]
    public async Task With_every_cell_full_the_answer_is_queued_with_no_row_and_an_error_log_names_the_region()
    {
        await SeedAsync(("vn-1", 100, 10, true), ("vn-2", 100, 20, true));

        var placement = await _picker.PlaceAsync("vn", "acme", (_, _) => throw new CellCallException("The cell is full.", 409, cellFull: true), TestContext.Current.CancellationToken);

        Assert.Equal(new CellPlacement(CellPlacement.Queued, null), placement);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM directory.cells WHERE takes_new_tenants"));
        var error = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("vn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_region_without_cells_is_queued_and_the_create_call_is_never_made()
    {
        var asked = new List<string>();

        var placement = await _picker.PlaceAsync("vn", "acme", Accept(asked), TestContext.Current.CancellationToken);

        Assert.Equal(CellPlacement.Queued, placement.State);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Placing_a_tenant_queues_a_job_that_writes_its_record_to_its_cells_ingress_and_a_second_run_changes_nothing()
    {
        await SeedAsync(("vn-1", 100, 0, true));
        var dns = new InMemoryDnsRecords();
        await _picker.PlaceAsync("vn", "acme", Accept([]), Token);

        var job = await ClaimAsync(TenantRecordJobs.WriteKind);
        var handler = new WriteTenantRecordJobHandler(_dataSource, dns);
        await handler.RunAsync(job, Token);
        await handler.RunAsync(job, Token);

        var record = Assert.Single(dns.Records);
        Assert.Equal("acme.vn.example.com", record.Key);
        Assert.Equal(new InMemoryCname("ingress.vn-1.vn.example.com", 300), record.Value);
    }

    [Fact]
    public async Task A_placed_tenant_has_its_subdomain_as_a_host_under_its_cell()
    {
        await SeedAsync(("vn-1", 100, 50, true), ("vn-2", 100, 5, true));

        await _picker.PlaceAsync("vn", "acme", Accept([]), Token);

        Assert.Equal("vn-2", await ScalarAsync<string>("SELECT cell_id FROM directory.hosts WHERE host = 'acme.vn.example.com'"));
        Assert.Equal("acme", await ScalarAsync<string>("SELECT tenant_id FROM directory.hosts WHERE host = 'acme.vn.example.com'"));
    }

    [Fact]
    public async Task A_write_job_for_a_tenant_with_no_directory_row_writes_no_record()
    {
        var dns = new InMemoryDnsRecords();
        var job = new ClaimedJob(Guid.NewGuid(), TenantRecordJobs.WriteKind, new() { ["tenantId"] = "ghost" }, 1);

        await new WriteTenantRecordJobHandler(_dataSource, dns).RunAsync(job, Token);

        Assert.Empty(dns.Records);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ClaimedJob> ClaimAsync(string kind)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(Token);
        return Assert.IsType<ClaimedJob>(await JobQueue.ClaimAsync(connection, "test", [kind], TimeSpan.FromMinutes(5), Token));
    }

    private static Func<string, CancellationToken, Task> Accept(List<string> asked) => (cell, _) =>
    {
        asked.Add(cell);
        return Task.CompletedTask;
    };

    private async Task SeedAsync(params (string Id, int Cap, int Count, bool TakesNewTenants)[] cells)
    {
        foreach (var (id, cap, count, takes) in cells)
        {
            await using var command = _dataSource.CreateCommand("""
                INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count, takes_new_tenants)
                VALUES (@id, 'vn', @url, @cap, @count, @takes)
                """);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("url", $"https://{id}.internal");
            command.Parameters.AddWithValue("cap", cap);
            command.Parameters.AddWithValue("count", count);
            command.Parameters.AddWithValue("takes", takes);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);
}
