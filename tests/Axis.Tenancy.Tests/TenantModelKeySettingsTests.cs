using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed class TenantModelKeySettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("false")]
    public void Tenant_keys_are_off_by_default_and_when_false(string? value)
    {
        Assert.False(TenantModelKeySettings.Load(Settings(value)).Allowed);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void True_allows_tenant_keys(string value)
    {
        Assert.True(TenantModelKeySettings.Load(Settings(value)).Allowed);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void Any_other_value_is_refused_naming_the_setting_and_not_the_value(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TenantModelKeySettings.Load(Settings(value)));

        Assert.Contains(TenantModelKeySettings.AllowSetting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Settings(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>(TenantModelKeySettings.AllowSetting, value)])
            .Build();
}
