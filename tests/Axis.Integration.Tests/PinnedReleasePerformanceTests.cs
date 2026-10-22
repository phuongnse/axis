using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Instances pinned to 20 releases reach their tasks in the worker, and the server lists and reads
/// those tasks, each within the time limit. The server and the worker each hold one model per
/// release, and a second pass does not add to them.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class PinnedReleasePerformanceTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const int Releases = 20;
    private const int InstancesPerRelease = 5;
    private const string Tasks = "/api/apps/StepApp/tasks";

    // The worker runs the first step of 100 instances, which builds 20 models and creates 100 tasks.
    // That took about a second when measured. The bound is far above that, so shared CI runners do
    // not make the test flaky. It still catches a model that is rebuilt for every step.
    private static readonly TimeSpan _workerTime = TimeSpan.FromMinutes(1);

    // The server lists 100 tasks of 20 releases and reads one task of each release. That took about
    // half a second when measured. The bound is far above that, for the same reason as above. It
    // still catches a model that is rebuilt for every task.
    private static readonly TimeSpan _serverTime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Instances_pinned_to_20_releases_reach_their_tasks_and_the_server_serves_them_within_the_time_limits_with_one_model_per_release()
    {
        // The first release is the one the fixture activated. The 19 others reword the label of the
        // Approve step, so every release has its own texts. Each release gets 5 instances.
        var releases = new List<Guid> { fixture.ReleaseId(TenantA) };
        for (var i = 1; i < Releases; i++)
        {
            releases.Add(await fixture.ActivateEditedAsync(TenantA, (path, content) =>
                path == Path.Combine("texts", "en.json")
                    ? content.Replace("Approve the request", $"Approve the request (v{i})", StringComparison.Ordinal)
                    : content));
        }

        var instances = new List<(Guid Instance, Guid Release)>();
        foreach (var release in releases)
        {
            var department = await fixture.InsertDepartmentAsync(TenantA, "maria");
            for (var i = 0; i < InstancesPerRelease; i++)
            {
                var subject = await fixture.InsertRequestAsync(TenantA, department);
                instances.Add((await fixture.StartAsync(TenantA, "Approve", subject, release), release));
            }
        }

        var workerStopwatch = Stopwatch.StartNew();
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        workerStopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The worker ran the steps of {instances.Count} instances in {workerStopwatch.Elapsed}.");
        Assert.True(workerStopwatch.Elapsed < _workerTime, $"The worker ran the steps in {workerStopwatch.Elapsed}.");
        var taskIds = new Dictionary<Guid, Guid>();
        foreach (var (instance, release) in instances)
        {
            taskIds[release] = await fixture.TaskIdAsync(TenantA, instance);
        }

        Assert.Equal(Releases, taskIds.Count);
        Assert.Equal(Releases, fixture.WorkerService<ReleaseModelCache>().Count(TenantA));

        using var maria = fixture.CreateClient().SignedInAs("maria");
        var serverStopwatch = Stopwatch.StartNew();
        var (count, distinct) = await ReadTasksAsync(maria, taskIds);
        serverStopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The server listed and read the tasks in {serverStopwatch.Elapsed}.");
        Assert.True(serverStopwatch.Elapsed < _serverTime, $"The server listed and read the tasks in {serverStopwatch.Elapsed}.");
        Assert.Equal(Releases * InstancesPerRelease, count);
        Assert.Equal(Releases, distinct);
        Assert.Equal(Releases, fixture.ServerService<ActiveModelCache>().Count(TenantA));

        // A second pass starts one more instance on each release and reads the tasks again. Every
        // model is cached by now, so it adds none to either cache.
        foreach (var release in releases)
        {
            var department = await fixture.InsertDepartmentAsync(TenantA, "maria");
            await fixture.StartAsync(TenantA, "Approve", await fixture.InsertRequestAsync(TenantA, department), release);
        }

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        await ReadTasksAsync(maria, taskIds);
        Assert.Equal(Releases, fixture.WorkerService<ReleaseModelCache>().Count(TenantA));
        Assert.Equal(Releases, fixture.ServerService<ActiveModelCache>().Count(TenantA));
    }

    /// <summary>
    /// Lists the first 100 tasks, then reads the task of each release in <paramref name="taskIds"/>.
    /// Returns the number of listed tasks and the number of distinct releases among them.
    /// </summary>
    private static async Task<(int Count, int Distinct)> ReadTasksAsync(HttpClient client, Dictionary<Guid, Guid> taskIds)
    {
        using var list = await GetJsonAsync(client, $"{Tasks}?pageSize=100");
        var items = list.RootElement.GetProperty("items").EnumerateArray().ToList();
        foreach (var (release, task) in taskIds)
        {
            using var read = await GetJsonAsync(client, $"{Tasks}/{task:D}");
            Assert.Equal(release, read.RootElement.GetProperty("releaseId").GetGuid());
        }

        return (items.Count, items.Select(item => item.GetProperty("releaseId").GetGuid()).Distinct().Count());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var request = RecordApiFixture.Request(path, Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
