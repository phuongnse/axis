using System.Diagnostics;
using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;
using Axis.Processes.Storage;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A worker polls an idle tenant with gaps that double from twice <c>Worker:PollInterval</c> up to <c>Worker:IdlePollCeiling</c>
/// and then stay there, see <c>docs/reference/worker-scheduling.md#waking-a-tenant</c>. The polls are counted through
/// <c>axis.worker.polls</c> with <c>axis.poll.found</c> set to <c>false</c>.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class WorkerPollPerformanceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    // The wait is a timer, so a gap is never shorter than its wait, up to the clock's tick. The bound above it is wide, so
    // shared CI runners do not make the test flaky, and still far below the next wait of the doubling.
    private static readonly TimeSpan _early = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _late = TimeSpan.FromMilliseconds(250);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_idle_tenant_is_polled_with_gaps_that_double_up_to_the_ceiling()
    {
        var connectionString = await database.CreateDatabaseAsync("worker_idle_polls", CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:00.1",
            ["Worker:IdlePollCeiling"] = "00:00:00.8",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
        });
        builder.Logging.ClearProviders();
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        // Only the polls of this host count: the meter of each host belongs to its own meter factory.
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        var clock = Stopwatch.StartNew();
        var idlePolls = new List<TimeSpan>();
        var foundPolls = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == AxisMetrics.WorkerPolls && ReferenceEquals(instrument.Meter.Scope, meterFactory))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var found = tags.ToArray().Single(tag => tag.Key == AxisTelemetry.PollFoundTag).Value is true;
            lock (idlePolls)
            {
                if (found)
                {
                    foundPolls++;
                }
                else
                {
                    idlePolls.Add(clock.Elapsed);
                }
            }
        });
        listener.Start();

        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(() => { lock (idlePolls) { return idlePolls.Count >= 7; } }, "The idle tenant was not polled seven times.");
        await host.StopAsync(CancellationToken);

        TimeSpan[] polls;
        lock (idlePolls)
        {
            Assert.Equal(0, foundPolls);
            polls = [.. idlePolls.Take(7)];
        }

        double[] expected = [0.2, 0.4, 0.8, 0.8, 0.8, 0.8];
        for (var i = 0; i < expected.Length; i++)
        {
            var gap = polls[i + 1] - polls[i];
            var wait = TimeSpan.FromSeconds(expected[i]);
            Assert.True(gap >= wait - _early && gap <= wait + _late, $"Gap {i + 1} was {gap.TotalSeconds:0.000} s, expected {expected[i]:0.0} s.");
        }
    }
}
