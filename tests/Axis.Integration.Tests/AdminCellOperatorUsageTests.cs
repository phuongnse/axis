using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>cell operator invite</c> and <c>cell operator client add</c> refuse to run outside a cell and on bad arguments, before they read a
/// database or an identity provider.
/// </summary>
public sealed class AdminCellOperatorUsageTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Email = "ops@example.test";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string[]> InvalidArguments() => new()
    {
        { ["cell", "operator", "invite"] },
        { ["cell", "operator", "invite", "not-an-address"] },
        { ["cell", "operator", "invite", Email, "other@example.test"] },
    };

    public static TheoryData<string[]> InvalidClientArguments() => new()
    {
        { ["cell", "operator", "client", "add"] },
        { ["cell", "operator", "client", "add", "Bad_Name"] },
        { ["cell", "operator", "client", "add", "-bad"] },
        { ["cell", "operator", "client", "add", "bad--name"] },
        { ["cell", "operator", "client", "add", new string('a', 65)] },
        { ["cell", "operator", "client", "add", "one", "two"] },
        { ["cell", "operator", "client", "add", "axis-cell-console"] },
        { ["cell", "operator", "client", "add", "admin-cli"] },
    };

    [Fact]
    public async Task Outside_a_cell_the_command_exits_with_2_prints_nothing_and_does_not_echo_the_email()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["cell", "operator", "invite", Email], new ConfigurationBuilder().Build(), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.Contains("Cell:Id", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task In_a_cell_a_missing_or_invalid_email_is_a_usage_error_and_prints_nothing(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cell:Id"] = "vn-1",
            ["Cell:Region"] = "vn",
            ["Cell:TenantDomain"] = "vn.example.test",
            ["Tenancy:Source"] = "directory",
        }).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(args, configuration, output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.StartsWith("Usage:", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_a_cell_the_client_command_exits_with_2_calls_no_identity_provider_and_writes_no_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);

        var result = await scenario.RunAsync(
            ["cell", "operator", "client", "add", "control-plane"], extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" });

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(result.Error, line => line.Contains("Cell:Id", StringComparison.Ordinal));
        Assert.Empty(scenario.IdentityProvider.Paths);
        Assert.Empty(await scenario.AuditRecordsAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidClientArguments))]
    public async Task In_a_cell_a_missing_invalid_or_reserved_client_name_exits_with_2_and_changes_nothing(string[] args)
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);

        var result = await scenario.RunAsync(
            args,
            extra: new Dictionary<string, string?>
            {
                ["Cell:Id"] = "vn-1",
                ["Cell:Region"] = "vn",
                ["Cell:TenantDomain"] = "vn.example.test",
                ["Tenancy:Source"] = "directory",
            });

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Empty(scenario.IdentityProvider.Paths);
        Assert.Empty(await scenario.AuditRecordsAsync());
    }
}
