using System.Text;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Subscriptions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The plan that a subscription pins, against a real PostgreSQL, see <c>docs/reference/plans.md#the-catalogue</c>: a newer version of
/// the plan never reaches the subscription, and a catalogue that lacks a pinned version stops the start.
/// </summary>
public sealed class SubscriptionPlanTests : IAsyncLifetime
{
    private const string Quotas = """
        "quotas": {
          "storage": {{storage}},
          "applications": {{applications}},
          "emailsPerDay": 2000,
          "concurrentImports": 2,
          "concurrentConnectorCalls": 10,
          "concurrentQueries": 10,
          "workerWeight": 2,
          "authoringRequests": 200
        }
        """;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), token);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_newer_version_of_the_plan_leaves_the_pinned_quotas_and_price_unchanged()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("pinned", "plan_code, plan_version, currency, period", "'team', 1, 'VND', 'year'");
        var catalogue = Catalogue(version1: true);

        await using var connection = await _dataSource.OpenConnectionAsync(token);

        var pinned = await SubscriptionPlans.ReadAsync(connection, null, subscription, catalogue, token);

        Assert.NotNull(pinned);
        Assert.Equal(1, pinned.Plan.Version);
        Assert.Equal(10_737_418_240, pinned.Plan.Quotas["storage"]);
        Assert.Equal(5, pinned.Plan.Quotas["applications"]);
        Assert.Equal(PlanCurrencies.Vnd, pinned.Currency);
        Assert.Equal(PlanPeriods.Year, pinned.Period);
        Assert.Equal(4_900_000L * 10, pinned.Price);
        Assert.Equal(21_474_836_480, catalogue.Find("team", 2)!.Quotas["storage"]);
    }

    [Fact]
    public async Task A_subscription_with_no_pinned_plan_reads_as_none()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("unpinned", "", "");
        await using var connection = await _dataSource.OpenConnectionAsync(token);

        Assert.Null(await SubscriptionPlans.ReadAsync(connection, null, subscription, Catalogue(version1: true), token));
    }

    [Fact]
    public async Task A_currency_without_a_plan_or_a_period_is_refused_by_the_database()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => SeedAsync("bad-pin", "currency, period", "'VND', 'month'"));
        var half = await Assert.ThrowsAsync<PostgresException>(() => SeedAsync("half-pin", "plan_code", "'team'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, half.SqlState);
    }

    [Fact]
    public async Task The_start_stops_when_the_catalogue_lacks_a_pinned_version_and_names_it()
    {
        var token = TestContext.Current.CancellationToken;
        await SeedAsync("pinned-start", "plan_code, plan_version, currency, period", "'team', 1, 'VND', 'month'");
        var options = new ControlPlaneOptions { ConnectionString = _container.GetConnectionString() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ControlPlaneMigrationStart(options, Catalogue(version1: false)).StartingAsync(token));

        Assert.Contains("team", exception.Message, StringComparison.Ordinal);
        Assert.Contains("version 1", exception.Message, StringComparison.Ordinal);
        await new ControlPlaneMigrationStart(options, Catalogue(version1: true)).StartingAsync(token);
    }

    // A catalogue with version 2 of the team plan, and with version 1 when asked. Version 2 has higher quotas and prices.
    private static PlanCatalogue Catalogue(bool version1)
    {
        var versions = new List<string> { Plan(2, 21_474_836_480, 10, 9_900_000, 3900) };
        if (version1)
        {
            versions.Add(Plan(1, 10_737_418_240, 5, 4_900_000, 1900));
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""{ "ControlPlane": { "plans": [{{string.Join(",", versions)}}] } }""")))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }

    private static string Plan(int version, long storage, int applications, long vnd, long usd) => $$"""
        {
          "code": "team",
          "version": {{version}},
          "name": { "en": "Team", "vi": "Nhóm" },
          "includedUsers": 25,
          {{Quotas.Replace("{{storage}}", storage.ToString(), StringComparison.Ordinal).Replace("{{applications}}", applications.ToString(), StringComparison.Ordinal)}},
          "prices": [{ "currency": "VND", "month": {{vnd}} }, { "currency": "USD", "month": {{usd}} }],
          "addOns": []
        }
        """;

    // A subscription of a verified sign-up, with the columns given on top of the required ones.
    private async Task<Guid> SeedAsync(string tenant, string columns, string values)
    {
        var token = TestContext.Current.CancellationToken;
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """, token);
        await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, trial_ends_at, step_id, state{(columns.Length == 0 ? "" : ", " + columns)})
            VALUES ('{id}', '{tenant}', '{signUp}', now(), '{Guid.CreateVersion7()}', 'trialing'{(values.Length == 0 ? "" : ", " + values)})
            """, token);
        return id;
    }
}
