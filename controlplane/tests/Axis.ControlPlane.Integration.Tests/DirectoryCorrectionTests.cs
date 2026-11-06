using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The correction of the region directory on a real PostgreSQL, against cells that a message handler stands in for. Each test class
/// instance has a container of its own, with the region <c>vn</c> and the cells <c>vn-1</c> and <c>vn-2</c>.
/// </summary>
public sealed class DirectoryCorrectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();
    private readonly CellHandler _handler = new();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private DirectoryCorrection _correction = null!;

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

        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await Execute("INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com')");
        await Execute("""
            INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count)
            VALUES ('vn-1', 'vn', 'https://vn-1.test', 100, 0), ('vn-2', 'vn', 'https://vn-2.test', 100, 0)
            """);

        var controlPlane = new ControlPlaneOptions();
        foreach (var id in new[] { "vn-1", "vn-2" })
        {
            controlPlane.Cells.Add(new CellOptions { Id = id, InternalUrl = $"https://{id}.test", TokenUrl = $"https://{id}.test/token", ClientId = "axis-controlplane" });
            controlPlane.CellSecrets[id] = "cell-secret";
        }

        _clients = new CellClients(controlPlane, new HandlerFactory(_handler), new CellClientOptions(), TimeProvider.System);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        _correction = new DirectoryCorrection(_dataSource, _clients, _loggers.CreateLogger<DirectoryCorrection>());
    }

    public async ValueTask DisposeAsync()
    {
        _clients.Dispose();
        _loggers.Dispose();
        _handler.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_run_adds_a_listed_tenant_and_its_hosts_removes_an_unlisted_one_and_sets_the_count_to_the_list()
    {
        await SeedTenantAsync("vn-1", "old", "old.vn.example.com");
        await SeedTenantAsync("vn-1", "keep", "keep.vn.example.com");
        await Execute("UPDATE directory.cells SET tenant_count = 7 WHERE id = 'vn-1'");
        _handler.Lists["vn-1"] = [("keep", ["keep.vn.example.com"]), ("new", ["New.vn.example.com", "shop.new.example"])];
        _handler.Lists["vn-2"] = [];

        await Run();

        Assert.Equal(["keep", "new"], await ColumnAsync("SELECT id FROM directory.tenants WHERE cell_id = 'vn-1' ORDER BY id"));
        Assert.Equal(
            ["keep.vn.example.com", "new.vn.example.com", "shop.new.example"],
            await ColumnAsync("SELECT host FROM directory.hosts WHERE cell_id = 'vn-1' ORDER BY host"));
        Assert.Equal("new", await ScalarAsync<string>("SELECT tenant_id FROM directory.hosts WHERE host = 'shop.new.example'"));
        Assert.Equal("vn", await ScalarAsync<string>("SELECT region_code FROM directory.tenants WHERE id = 'new'"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-2'"));
    }

    [Fact]
    public async Task A_second_run_with_the_same_lists_changes_nothing()
    {
        _handler.Lists["vn-1"] = [("acme", ["acme.vn.example.com"])];
        _handler.Lists["vn-2"] = [];
        await Run();

        await Run();

        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM directory.hosts"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
    }

    [Fact]
    public async Task A_cell_that_answers_an_error_keeps_its_rows_and_the_other_cell_is_still_corrected()
    {
        await SeedTenantAsync("vn-1", "old", "old.vn.example.com");
        await Execute("UPDATE directory.cells SET tenant_count = 5 WHERE id = 'vn-1'");
        _handler.Lists["vn-1"] = null;
        _handler.Lists["vn-2"] = [("fresh", ["fresh.vn.example.com"])];

        await Run();

        Assert.Equal(["old"], await ColumnAsync("SELECT id FROM directory.tenants WHERE cell_id = 'vn-1'"));
        Assert.Equal(["old.vn.example.com"], await ColumnAsync("SELECT host FROM directory.hosts WHERE cell_id = 'vn-1'"));
        Assert.Equal(5, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-1'"));
        Assert.Equal(["fresh"], await ColumnAsync("SELECT id FROM directory.tenants WHERE cell_id = 'vn-2'"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT tenant_count FROM directory.cells WHERE id = 'vn-2'"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_or_host_under_another_cell_is_left_alone_and_a_warning_counts_it()
    {
        await SeedTenantAsync("vn-2", "shared", "shared.vn.example.com");
        await Execute("UPDATE directory.cells SET tenant_count = 1 WHERE id = 'vn-2'");
        _handler.Lists["vn-1"] = [("shared", ["shared.vn.example.com"]), ("own", ["own.vn.example.com"])];
        _handler.Lists["vn-2"] = [("shared", ["shared.vn.example.com"])];

        await Run();

        Assert.Equal("vn-2", await ScalarAsync<string>("SELECT cell_id FROM directory.tenants WHERE id = 'shared'"));
        Assert.Equal("vn-2", await ScalarAsync<string>("SELECT cell_id FROM directory.hosts WHERE host = 'shared.vn.example.com'"));
        Assert.Equal("vn-1", await ScalarAsync<string>("SELECT cell_id FROM directory.tenants WHERE id = 'own'"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shared", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cell_without_a_directory_row_is_skipped_with_a_warning()
    {
        await Execute("DELETE FROM directory.cells WHERE id = 'vn-2'");
        _handler.Lists["vn-1"] = [];
        _handler.Lists["vn-2"] = [("ghost", [])];

        await Run();

        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM directory.tenants"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("vn-2", warning.Message, StringComparison.Ordinal);
    }

    private Task Run() => _correction.RunAsync(new ClaimedJob(Guid.NewGuid(), DirectoryCorrection.JobKind, [], 1), TestContext.Current.CancellationToken);

    private async Task SeedTenantAsync(string cellId, string tenantId, string host)
    {
        await Execute($"INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('{tenantId}', 'vn', '{cellId}')");
        await Execute($"INSERT INTO directory.hosts (host, tenant_id, cell_id) VALUES ('{host}', '{tenantId}', '{cellId}')");
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private async Task<string[]> ColumnAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Stands in for the cells <c>vn-1</c> and <c>vn-2</c>: a token endpoint and a tenant list each. A null list answers <c>500</c>.</summary>
    private sealed class CellHandler : HttpMessageHandler
    {
        public Dictionary<string, (string Id, string[] Hosts)[]?> Lists { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var cell = uri.Host.Replace(".test", string.Empty, StringComparison.Ordinal);
            if (uri.AbsolutePath == "/token")
            {
                return Task.FromResult(Json(new { access_token = $"token-{cell}", expires_in = 3600, token_type = "Bearer" }));
            }

            if (uri.AbsolutePath == "/api/cell/tenants" && request.Method == HttpMethod.Get
                && request.Headers.Authorization?.Parameter == $"token-{cell}")
            {
                if (Lists.GetValueOrDefault(cell) is not { } list)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                return Task.FromResult(Json(new { items = list.Select(t => new { id = t.Id, status = "active", application = (string?)null, hosts = t.Hosts, failedWork = (object?)null }) }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
    }
}
