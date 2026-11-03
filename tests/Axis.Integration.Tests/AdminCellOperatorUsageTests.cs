using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary><c>cell operator invite</c> refuses to run outside a cell and on bad arguments, before it reads a database or an identity provider.</summary>
public sealed class AdminCellOperatorUsageTests
{
    private const string Email = "ops@example.test";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string[]> InvalidArguments() => new()
    {
        { ["cell", "operator", "invite"] },
        { ["cell", "operator", "invite", "not-an-address"] },
        { ["cell", "operator", "invite", Email, "other@example.test"] },
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
}
