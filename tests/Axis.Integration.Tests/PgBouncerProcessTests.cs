using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A worker running <c>PackageDescribe</c>, a task step and then an operation step that calls a package, with its tenant connection
/// going through <see cref="PgBouncerServer"/> in transaction mode, as a cell runs it.
/// </summary>
public sealed class PgBouncerProcessTests : IAsyncLifetime
{
    private readonly PackageCallFixture _fixture = new(throughPgBouncer: true);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task The_worker_runs_a_task_and_an_operation_step_through_PgBouncer_and_leaves_no_transaction_open()
    {
        var tenant = _fixture.Tenant;
        await tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await tenant.InsertRequestAsync(500m, 2);
        var instanceId = await tenant.StartAsync("PackageDescribe", subjectId, _fixture.ReleaseId, _fixture.Model);
        Assert.Equal(WorkItemOutcome.Completed, await _fixture.RunStepAsync(CancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);

        await _fixture.CompleteTaskAsync(instanceId, CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await _fixture.RunStepAsync(CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await _fixture.RunCallAsync(CancellationToken));

        Assert.Equal("1.0.0", (await _fixture.RequestAsync(subjectId)).Values["packageVersion"]!.GetValue<string>());
        Assert.Equal("done", (await tenant.InstanceAsync(instanceId)).Step);
        await PgBouncerServer.AssertNoRuntimeTransactionLeftOpenAsync(tenant.ConnectionString, onlyThroughPgBouncer: true);
    }
}
