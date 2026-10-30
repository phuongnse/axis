using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

public sealed class AdminTenantCreateUsageTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string[]> InvalidAdministratorArguments() => new()
    {
        { ["tenant", "create", "acme", "--host", "acme.test", "--preview-of", "main", "--administrator", "boss@example.test"] },
        { ["tenant", "create", "acme", "--host", "acme.test", "--administrator", "not-an-address"] },
        { ["tenant", "create", "acme", "--host", "acme.test", "--administrator", "boss@example.test", "--administrator", "other@example.test"] },
        { ["tenant", "create", "acme", "--host", "acme.test", "--administrator"] },
    };

    [Theory]
    [MemberData(nameof(InvalidAdministratorArguments))]
    public async Task Tenant_create_with_an_invalid_administrator_is_a_usage_error_and_prints_no_step(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(args, new ConfigurationBuilder().Build(), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.StartsWith("Usage:", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", error.ToString(), StringComparison.Ordinal);
    }
}
