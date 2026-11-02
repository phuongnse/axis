using System.Reflection;
using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed class QuotaDefaultsTests
{
    [Fact]
    public void Every_quota_key_of_the_code_has_a_built_in_default()
    {
        var keys = typeof(QuotaKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Contains(key, QuotaKeys.All);
            Assert.True(QuotaDefaults.BuiltIn.ContainsKey(key), $"'{key}' has no built-in default.");
        }

        Assert.Equal(keys.Count, QuotaKeys.All.Count);
        Assert.Equal(keys.Count, QuotaDefaults.BuiltIn.Count);
    }

    [Theory]
    [InlineData("storage", 10737418240L)]
    [InlineData("users", 50L)]
    [InlineData("applications", 10L)]
    [InlineData("emailsPerDay", 1000L)]
    [InlineData("concurrentImports", 2L)]
    [InlineData("concurrentConnectorCalls", 10L)]
    [InlineData("concurrentQueries", 10L)]
    [InlineData("workerWeight", 1L)]
    [InlineData("authoringRequests", null)]
    public void The_built_in_defaults_are_the_values_of_the_quotas_table(string quota, long? expected)
    {
        Assert.Equal(expected, QuotaDefaults.BuiltIn[quota]);
        Assert.Equal(expected, QuotaDefaults.Load(Settings(), Cell())[quota]);
    }

    [Fact]
    public void A_cell_with_no_settings_gets_the_built_in_defaults()
    {
        var defaults = QuotaDefaults.Load(Settings(), Cell());

        foreach (var (quota, expected) in QuotaDefaults.BuiltIn)
        {
            Assert.Equal(expected, defaults[quota]);
        }
    }

    [Fact]
    public void A_setting_overrides_the_default_of_one_quota_in_a_cell()
    {
        var defaults = QuotaDefaults.Load(Settings(("Quotas:Defaults:users", "5"), ("Quotas:Defaults:authoringRequests", "0")), Cell());

        Assert.Equal(5, defaults["users"]);
        Assert.Equal(0, defaults["authoringRequests"]);
        Assert.Equal(10, defaults["applications"]);
    }

    [Theory]
    [InlineData("users", "many")]
    [InlineData("users", "")]
    [InlineData("users", "1.5")]
    [InlineData("users", "-1")]
    [InlineData("applications", "+3")]
    [InlineData("seats", "5")]
    public void A_wrong_setting_in_a_cell_is_refused_naming_the_setting_and_not_the_value(string quota, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QuotaDefaults.Load(Settings(($"Quotas:Defaults:{quota}", value)), Cell()));

        Assert.Contains($"Quotas:Defaults:{quota}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_wrong_setting_is_named()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QuotaDefaults.Load(
            Settings(("Quotas:Defaults:users", "many"), ("Quotas:Defaults:seats", "5")),
            Cell()));

        Assert.Contains("Quotas:Defaults:users", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Quotas:Defaults:seats", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_cell_the_settings_are_ignored()
    {
        var defaults = QuotaDefaults.Load(Settings(("Quotas:Defaults:users", "many"), ("Quotas:Defaults:seats", "-1")), new CellOptions());

        Assert.Equal(50, defaults["users"]);
    }

    [Fact]
    public void An_unknown_quota_is_refused()
    {
        var defaults = QuotaDefaults.Load(Settings(), Cell());

        Assert.Throws<ArgumentException>(() => defaults["seats"]);
    }

    private static CellOptions Cell() => new() { Id = "vn-1", Region = "vn", TenantDomain = "vn.example.com" };

    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(row => new KeyValuePair<string, string?>(row.Key, row.Value))).Build();
}
