using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Tenancy.Tests;

public sealed class TenantPoolSettingsTests
{
    private const string PoolKeywords = ";Maximum Pool Size=1;Minimum Pool Size=1;Connection Idle Lifetime=300;Max Auto Prepare=20";

    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value))
            .Build();

    [Fact]
    public void Without_settings_a_tenant_pool_is_small_and_never_prepares_statements()
    {
        var connection = new NpgsqlConnectionStringBuilder(TenantPoolSettings.Load(Configure()).Apply(Tenants.ConnectionString));

        Assert.Equal(5, connection.MaxPoolSize);
        Assert.Equal(0, connection.MinPoolSize);
        Assert.Equal(60, connection.ConnectionIdleLifetime);
        Assert.Equal(0, connection.MaxAutoPrepare);
    }

    [Fact]
    public void The_settings_win_over_the_pool_keywords_of_the_connection_string()
    {
        var settings = TenantPoolSettings.Load(Configure(
            (TenantPoolSettings.MaxSizeSetting, "2"),
            (TenantPoolSettings.MinSizeSetting, "1"),
            (TenantPoolSettings.IdleLifetimeSetting, "00:00:05")));

        var applied = settings.Apply(Tenants.ConnectionString + PoolKeywords);

        var connection = new NpgsqlConnectionStringBuilder(applied);
        Assert.Equal(2, connection.MaxPoolSize);
        Assert.Equal(1, connection.MinPoolSize);
        Assert.Equal(5, connection.ConnectionIdleLifetime);
        Assert.Equal(0, connection.MaxAutoPrepare);
        Assert.Equal(5, connection.ConnectionPruningInterval);
        using var dataSource = NpgsqlDataSource.Create(applied);
        var created = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        Assert.Equal(2, created.MaxPoolSize);
        Assert.Equal(0, created.MaxAutoPrepare);
    }

    [Fact]
    public void A_pruning_interval_shorter_than_the_idle_lifetime_stays()
    {
        var applied = TenantPoolSettings.Default.Apply(Tenants.ConnectionString + ";Connection Pruning Interval=3");

        Assert.Equal(3, new NpgsqlConnectionStringBuilder(applied).ConnectionPruningInterval);
    }

    [Theory]
    [InlineData(TenantPoolSettings.MaxSizeSetting, "0")]
    [InlineData(TenantPoolSettings.MaxSizeSetting, "-1")]
    [InlineData(TenantPoolSettings.MaxSizeSetting, "abc")]
    [InlineData(TenantPoolSettings.MaxSizeSetting, "2.5")]
    [InlineData(TenantPoolSettings.MinSizeSetting, "-1")]
    [InlineData(TenantPoolSettings.MinSizeSetting, "abc")]
    [InlineData(TenantPoolSettings.MinSizeSetting, "6")]
    [InlineData(TenantPoolSettings.IdleLifetimeSetting, "00:00:00")]
    [InlineData(TenantPoolSettings.IdleLifetimeSetting, "-00:00:05")]
    [InlineData(TenantPoolSettings.IdleLifetimeSetting, "abc")]
    [InlineData(TenantPoolSettings.IdleLifetimeSetting, "00:00:00.500")]
    [InlineData(TenantPoolSettings.IdleLifetimeSetting, "00:01:00.500")]
    public void An_invalid_value_names_its_setting(string setting, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TenantPoolSettings.Load(Configure((setting, value))));

        Assert.Contains($"'{setting}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_minimum_above_a_configured_maximum_names_the_minimum()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TenantPoolSettings.Load(Configure(
            (TenantPoolSettings.MaxSizeSetting, "2"),
            (TenantPoolSettings.MinSizeSetting, "3"))));

        Assert.Contains($"'{TenantPoolSettings.MinSizeSetting}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_value_takes_the_default()
    {
        var settings = TenantPoolSettings.Load(Configure((TenantPoolSettings.MaxSizeSetting, string.Empty)));

        Assert.Equal(5, settings.MaxSize);
    }
}
