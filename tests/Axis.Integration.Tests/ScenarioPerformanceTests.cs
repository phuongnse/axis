using System.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Scenarios;
using Axis.Scenarios.Runner;

namespace Axis.Integration.Tests;

/// <summary>The scenarios of the purchase request sample pass within the time limit.</summary>
public sealed class ScenarioPerformanceTests
{
    // Each scenario creates a database, activates the sample and runs its steps and processes. The
    // five scenarios took about 10 seconds when measured. The bound is several times that, so
    // shared CI runners do not make the test flaky. It still catches a slowdown of an order of
    // magnitude, such as a scenario that waits for its work to be polled.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(3);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_purchase_request_scenarios_pass_within_the_time_limit()
    {
        using var results = new TemporaryFolder();
        var output = new StringWriter();

        var stopwatch = Stopwatch.StartNew();
        var exitCode = await ScenarioCommand.RunAsync(
            [
                Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"),
                "--server", await PostgreSqlServer.AdminConnectionStringAsync(),
                "--extensions", SamplePackages.Path,
                "--results", results.Path,
            ],
            output,
            CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The scenarios took {stopwatch.Elapsed}.");
        Assert.True(exitCode == 0, output.ToString());
        Assert.True(stopwatch.Elapsed < _runTime, $"The scenarios took {stopwatch.Elapsed}.");
    }
}
