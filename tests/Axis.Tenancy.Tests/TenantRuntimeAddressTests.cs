using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Tenancy.Tests;

public sealed class TenantRuntimeAddressTests
{
    private static readonly (string, string?)[] _cell =
    [
        ("Tenancy:Source", "directory"),
        ("Cell:Id", "vn-1"),
        ("Cell:Region", "vn"),
        ("Cell:TenantDomain", "vn.example.com"),
    ];

    [Fact]
    public void Outside_a_cell_the_address_is_ignored_even_with_a_bad_port()
    {
        var configuration = Settings(("Cell:RuntimeDatabaseHost", "pgbouncer"), ("Cell:RuntimeDatabasePort", "many"));

        Assert.Null(TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration)));
    }

    [Fact]
    public void A_cell_without_either_setting_has_no_address()
    {
        var configuration = Settings(_cell);

        Assert.Null(TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration)));
    }

    [Fact]
    public void A_cell_with_host_and_port_reads_both()
    {
        var configuration = Settings([.. _cell, ("Cell:RuntimeDatabaseHost", "pgbouncer.internal"), ("Cell:RuntimeDatabasePort", "6432")]);

        var address = TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration));

        Assert.Equal(new TenantRuntimeAddress("pgbouncer.internal", 6432), address);
    }

    [Fact]
    public void A_host_alone_keeps_the_port_of_the_row()
    {
        var configuration = Settings([.. _cell, ("Cell:RuntimeDatabaseHost", "pgbouncer.internal")]);

        var address = TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration));

        Assert.Equal(new TenantRuntimeAddress("pgbouncer.internal", null), address);
    }

    [Fact]
    public void A_port_without_a_host_stops_the_start_and_names_the_host_setting()
    {
        var configuration = Settings([.. _cell, ("Cell:RuntimeDatabasePort", "6432")]);

        var exception = Assert.Throws<InvalidOperationException>(() => TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration)));

        Assert.Contains("Cell:RuntimeDatabaseHost", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    [InlineData("64.32")]
    public void A_port_that_is_not_a_port_number_stops_the_start_and_names_the_port_setting(string port)
    {
        var configuration = Settings([.. _cell, ("Cell:RuntimeDatabaseHost", "pgbouncer.internal"), ("Cell:RuntimeDatabasePort", port)]);

        var exception = Assert.Throws<InvalidOperationException>(() => TenantRuntimeAddress.Load(configuration, CellOptions.Load(configuration)));

        Assert.Contains("Cell:RuntimeDatabasePort", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_the_address_sets_host_port_and_the_pool_values()
    {
        var builder = new NpgsqlConnectionStringBuilder { Host = "pg", Port = 5432 };

        new TenantRuntimeAddress("pgbouncer.internal", null).ApplyTo(builder);

        Assert.Equal("pgbouncer.internal", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal(5, builder.MaxPoolSize);
        Assert.Equal(0, builder.MinPoolSize);
        Assert.Equal(60, builder.ConnectionIdleLifetime);
        Assert.Equal(0, builder.MaxAutoPrepare);
    }

    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(row => new KeyValuePair<string, string?>(row.Key, row.Value))).Build();
}
