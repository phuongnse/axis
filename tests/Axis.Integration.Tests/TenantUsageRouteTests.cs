using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Tenancy;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>GET /api/tenant/usage</c> answers an administrator the tenant's quotas with their use and its day rows of the last 30
/// days. The server reads the users from the real <c>axis</c> realm and the usage from a real platform database. The test host
/// treats the role <c>author</c> as the administrator role, and <see cref="StorageQuotaHost.InCell"/> makes it part of a cell.
/// </summary>
public sealed class TenantUsageRouteTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string HostA = StudioPublishTests.HostA;

    private const long StorageLimit = 1_000_000;

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_administrator_gets_every_quota_with_its_use_and_only_the_tenants_own_days_of_the_last_30()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("tenant_usage_read", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main, inCell: true);
        using var author = factory.CreateClient().SignedInAs("author");

        // Creating the client started the server. Seeding the first sample migrates the platform tables.
        await StorageQuotaHost.SeedUsageAsync(main, "a", 100, 20, CancellationToken);
        await StorageQuotaHost.SeedUsageAsync(main, "b", 1, 1, CancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await InsertDayAsync(main, "a", today, databaseBytes: 100, fileBytes: 20, emailsSent: 7, connectorCalls: 3, workItemsRun: 5);
        await InsertDayAsync(main, "a", today.AddDays(-3), databaseBytes: 90, fileBytes: 10, emailsSent: 2, connectorCalls: 0, workItemsRun: 1);
        await InsertDayAsync(main, "a", today.AddDays(-40), databaseBytes: 1, fileBytes: 1, emailsSent: 0, connectorCalls: 0, workItemsRun: 0);
        await InsertDayAsync(main, "b", today, databaseBytes: 1, fileBytes: 1, emailsSent: 99, connectorCalls: 99, workItemsRun: 99);
        await InsertCounterAsync(main, "a", today, 7);
        await InsertCounterAsync(main, "b", today, 99);

        using var usage = await StudioPublishTests.GetJsonAsync(author, "/api/tenant/usage", HostA);

        var quotas = usage.RootElement.GetProperty("quotas");
        Assert.Equal(QuotaKeys.All.Order(StringComparer.Ordinal), quotas.EnumerateObject().Select(quota => quota.Name).Order(StringComparer.Ordinal));
        var storage = quotas.GetProperty(QuotaKeys.Storage);
        Assert.Equal(StorageLimit, storage.GetProperty("limit").GetInt64());
        Assert.Equal(120, storage.GetProperty("used").GetInt64());
        Assert.True(quotas.GetProperty(QuotaKeys.Users).GetProperty("used").GetInt64() > 0);
        Assert.True(quotas.GetProperty(QuotaKeys.Applications).GetProperty("used").GetInt64() > 0);
        Assert.Equal(7, quotas.GetProperty(QuotaKeys.EmailsPerDay).GetProperty("used").GetInt64());
        foreach (var key in new[]
        {
            QuotaKeys.ConcurrentImports,
            QuotaKeys.ConcurrentConnectorCalls,
            QuotaKeys.ConcurrentQueries,
            QuotaKeys.WorkerWeight,
            QuotaKeys.AuthoringRequests,
        })
        {
            Assert.Equal(JsonValueKind.Null, quotas.GetProperty(key).GetProperty("used").ValueKind);
        }

        var days = usage.RootElement.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(2, days.Count);
        Assert.Equal(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), days[0].GetProperty("day").GetString());
        Assert.Equal(100, days[0].GetProperty("databaseBytes").GetInt64());
        Assert.Equal(20, days[0].GetProperty("fileBytes").GetInt64());
        Assert.Equal(7, days[0].GetProperty("emailsSent").GetInt32());
        Assert.Equal(3, days[0].GetProperty("connectorCalls").GetInt64());
        Assert.Equal(5, days[0].GetProperty("workItemsRun").GetInt64());
        Assert.Equal(JsonValueKind.Null, days[0].GetProperty("users").ValueKind);
        Assert.Equal(today.AddDays(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), days[1].GetProperty("day").GetString());
        Assert.Equal(90, days[1].GetProperty("databaseBytes").GetInt64());
    }

    [Fact]
    public async Task A_person_without_the_administrator_role_gets_403_and_nobody_signed_in_gets_401()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("tenant_usage_denied", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main, inCell: true);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var nobody = factory.CreateClient();

        using var forbidden = await StudioPublishTests.SendAsync(requester, HttpMethod.Get, "/api/tenant/usage", HostA);
        using var unauthorized = await StudioPublishTests.SendAsync(nobody, HttpMethod.Get, "/api/tenant/usage", HostA);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task An_administrator_gets_404_on_a_host_outside_a_cell()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("tenant_usage_no_cell", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main, inCell: false);
        using var author = factory.CreateClient().SignedInAs("author");

        using var response = await StudioPublishTests.SendAsync(author, HttpMethod.Get, "/api/tenant/usage", HostA);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A server whose tenant "a" signs in at the real axis realm and holds the secret of its axis-users client.
    private async Task<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>> NewFactoryAsync(string folder, string main, bool inCell)
    {
        var authority = await keycloak.StartAsync();
        return StudioPublishTests.NewFactory(folder, [("a", HostA, main)]).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenants:a:Oidc:Authority", authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:UsersClientSecret", KeycloakFixture.UsersClientSecret);
            builder.UseSetting("Users:AdministratorRole", "author");
            if (inCell)
            {
                builder.InCell(StorageLimit);
            }
        });
    }

    private static async Task InsertDayAsync(
        string connectionString, string tenant, DateOnly day, long databaseBytes, long fileBytes, int emailsSent, long connectorCalls, long workItemsRun)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage (tenant_id, day, database_bytes, file_bytes, emails_sent, connector_calls, work_items_run)
            VALUES (@tenant, @day, @database, @files, @emails, @calls, @items)
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("database", databaseBytes);
        command.Parameters.AddWithValue("files", fileBytes);
        command.Parameters.AddWithValue("emails", emailsSent);
        command.Parameters.AddWithValue("calls", connectorCalls);
        command.Parameters.AddWithValue("items", workItemsRun);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task InsertCounterAsync(string connectionString, string tenant, DateOnly day, long value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO platform.tenant_daily_counters (tenant_id, day, counter, value) VALUES (@tenant, @day, 'emails', @value)",
            connection);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
