using System.Diagnostics;
using Axis.Processes.Instances;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// A thousand started instances of a short process all reach their end step within the time limit
/// with two worker hosts sharing the work, and no work item runs twice.
/// </summary>
public sealed class ProcessEnginePerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int Instances = 1_000;

    // Each instance runs two short steps, so the run is expected to take seconds. Well above that,
    // so shared CI runners do not make the test flaky, and still low enough to catch a slowdown of
    // an order of magnitude.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thousand_instances_complete_with_two_workers_within_the_time_limit_and_no_item_runs_twice()
    {
        // Half the requests take the first branch and half take otherwise. The workers are not
        // running yet, so this part is not timed.
        var ids = new Guid[Instances];
        for (var i = 0; i < Instances; i++)
        {
            var subjectId = await fixture.InsertRequestAsync(i % 2 == 0 ? 50m : 5000m, 1);
            ids[i] = await fixture.StartAsync("Review", subjectId, fixture.FirstReleaseId);
        }

        var logs = new LogCollector();
        using var hostOne = BuildHost(logs);
        using var hostTwo = BuildHost(logs);
        var stopwatch = Stopwatch.StartNew();
        await hostOne.StartAsync(CancellationToken);
        await hostTwo.StartAsync(CancellationToken);
        var deadline = _runTime + TimeSpan.FromSeconds(30);
        while (await fixture.CountAsync(
            $"SELECT count(*) FROM axis.process_instances WHERE id = ANY(@ids) AND state = '{ProcessStarts.Completed}'",
            ("ids", ids)) != Instances)
        {
            Assert.True(stopwatch.Elapsed < deadline, $"The instances did not complete within {deadline}.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        }

        stopwatch.Stop();
        await hostOne.StopAsync(CancellationToken);
        await hostTwo.StopAsync(CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"{Instances} instances completed in {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The instances completed in {stopwatch.Elapsed}.");

        // Each instance ran its decision step at revision 1 and its end step at revision 2, once each.
        await using (var command = fixture.DataSource.CreateCommand(
            """
            SELECT count(*), count(DISTINCT (process_instance_id, revision)), count(*) FILTER (WHERE revision NOT IN (1, 2))
            FROM axis.process_step_history WHERE process_instance_id = ANY(@ids)
            """))
        {
            command.Parameters.AddWithValue("ids", ids);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));
            Assert.Equal((2L * Instances, 2L * Instances, 0L), (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = ANY(@ids)", ("ids", ids)));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("claimed by another worker", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("changed since", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("is missing or not running", StringComparison.Ordinal));
    }

    /// <summary>A worker host as the worker program builds it, polling the fixture's tenant every 50 ms.</summary>
    private IHost BuildHost(LogCollector logs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:00.05",
            [$"Tenants:{ProcessStepFixture.TenantId}:Hosts:0"] = $"{ProcessStepFixture.TenantId}.example.test",
            [$"Tenants:{ProcessStepFixture.TenantId}:ConnectionString"] = fixture.ConnectionString,
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        return builder.Build();
    }
}
