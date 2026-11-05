using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed class CellOptionsTests
{
    [Fact]
    public void Every_setting_of_the_cell_settings_table_is_read()
    {
        var options = CellOptions.Load(Settings(
            ("Tenancy:Source", "directory"),
            ("Cell:Id", "vn-1"),
            ("Cell:Region", "vn"),
            ("Cell:TenantDomain", "vn.example.com"),
            ("Cell:InternalDomain", "internal.example.com"),
            ("Cell:TenantCap", "500"),
            ("Cell:ConsoleHost", "console.vn.example.com"),
            ("Cell:ConsoleAllowedNetworks:0", "10.0.0.0/8"),
            ("Cell:ConsoleAllowedNetworks:1", "192.168.0.0/16"),
            ("Cell:InternalUrl", "http://axis-internal:8080"),
            ("Cell:OperatorRealm", "ops"),
            ("Cell:OperatorRole", "ops-role"),
            ("Cell:BackupKeyFile", "/run/secrets/cell-backup-key")));

        Assert.True(options.IsCell);
        Assert.Equal("vn-1", options.Id);
        Assert.Equal("vn", options.Region);
        Assert.Equal("vn.example.com", options.TenantDomain);
        Assert.Equal("internal.example.com", options.InternalDomain);
        Assert.Equal(500, options.TenantCap);
        Assert.Equal("console.vn.example.com", options.ConsoleHost);
        Assert.Equal(["10.0.0.0/8", "192.168.0.0/16"], options.ConsoleAllowedNetworks);
        Assert.Equal("http://axis-internal:8080", options.InternalUrl);
        Assert.Equal("ops", options.OperatorRealm);
        Assert.Equal("ops-role", options.OperatorRole);
        Assert.Equal("/run/secrets/cell-backup-key", options.BackupKeyFile);
    }

    [Fact]
    public void An_empty_section_is_no_cell_with_the_default_operator_realm_and_role()
    {
        var options = CellOptions.Load(Settings());

        Assert.False(options.IsCell);
        Assert.Null(options.Id);
        Assert.Null(options.TenantCap);
        Assert.Empty(options.ConsoleAllowedNetworks);
        Assert.Equal("axis-operators", options.OperatorRealm);
        Assert.Equal("cell-operator", options.OperatorRole);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_operator_realm_and_role_keep_the_defaults(string value)
    {
        var options = CellOptions.Load(Settings(("Cell:OperatorRealm", value), ("Cell:OperatorRole", value)));

        Assert.Equal("axis-operators", options.OperatorRealm);
        Assert.Equal("cell-operator", options.OperatorRole);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void Without_an_id_no_other_setting_is_checked(string source)
    {
        var options = CellOptions.Load(Settings(("Tenancy:Source", source), ("Cell:Region", ""), ("Cell:TenantDomain", "")));

        Assert.False(options.IsCell);
    }

    [Theory]
    [InlineData(null, "Tenancy:Source")]
    [InlineData("configuration", "Tenancy:Source")]
    public void A_cell_without_the_directory_source_is_refused_naming_the_setting(string? source, string setting)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CellOptions.Load(Settings(
            ("Tenancy:Source", source),
            ("Cell:Id", "vn-1"),
            ("Cell:Region", "vn"),
            ("Cell:TenantDomain", "vn.example.com"))));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cell:Region")]
    [InlineData("Cell:TenantDomain")]
    public void A_cell_without_a_required_setting_is_refused_naming_it(string missing)
    {
        var values = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Cell:Id"] = "vn-1",
            ["Cell:Region"] = "vn",
            ["Cell:TenantDomain"] = "vn.example.com",
        };
        values[missing] = " ";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => CellOptions.Load(configuration));

        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-a-range")]
    [InlineData("")]
    public void A_cell_with_a_console_network_that_is_not_a_cidr_range_is_refused_naming_the_setting(string network)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CellOptions.Load(Settings(
            ("Tenancy:Source", "directory"),
            ("Cell:Id", "vn-1"),
            ("Cell:Region", "vn"),
            ("Cell:TenantDomain", "vn.example.com"),
            ("Cell:ConsoleAllowedNetworks:0", "10.0.0.0/8"),
            ("Cell:ConsoleAllowedNetworks:1", network))));

        Assert.Contains("Cell:ConsoleAllowedNetworks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_with_every_requirement_missing_names_each_setting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CellOptions.Load(Settings(("Cell:Id", "vn-1"))));

        Assert.Contains("Tenancy:Source", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cell:Region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cell:TenantDomain", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("eu-1", "vn")]
    [InlineData("vn", "vn")]
    [InlineData("vn-0", "vn")]
    [InlineData("vn-01", "vn")]
    [InlineData("vn-x", "vn")]
    [InlineData("vn-1-2", "vn")]
    [InlineData("vn-1\n", "vn")]
    [InlineData("vn1", "vn")]
    public void An_id_that_is_not_the_region_a_hyphen_and_a_number_is_refused_naming_the_setting(string id, string region)
    {
        var problems = CellOptionsValidator.Validate(Options(id, region), TenancySource.Directory);

        Assert.Contains(problems, problem => problem.Contains("Cell:Id", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("vn-1", "vn")]
    [InlineData("vn-12", "vn")]
    [InlineData("eu2-10", "eu2")]
    public void An_id_of_the_region_a_hyphen_and_a_number_passes(string id, string region)
    {
        Assert.Empty(CellOptionsValidator.Validate(Options(id, region), TenancySource.Directory));
    }

    [Theory]
    [InlineData("VN")]
    [InlineData("v-n")]
    [InlineData("vn_1")]
    public void A_region_with_anything_but_lowercase_letters_and_digits_is_refused_naming_the_setting(string region)
    {
        var problems = CellOptionsValidator.Validate(Options($"{region}-1", region), TenancySource.Directory);

        Assert.Contains(problems, problem => problem.Contains("Cell:Region", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_an_id_the_validator_finds_no_problem()
    {
        Assert.Empty(CellOptionsValidator.Validate(new CellOptions(), TenancySource.Configuration));
    }

    [Fact]
    public void A_cell_tenant_cap_that_is_not_a_number_stops_the_read_naming_the_setting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CellOptions.Load(Settings(("Cell:TenantCap", "many"))));

        Assert.Contains("TenantCap", exception.Message, StringComparison.Ordinal);
    }

    private static CellOptions Options(string id, string region) =>
        new() { Id = id, Region = region, TenantDomain = "vn.example.com" };

    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(row => new KeyValuePair<string, string?>(row.Key, row.Value))).Build();
}
