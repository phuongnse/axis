using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class PackagePinningTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_instance_keeps_the_package_version_its_release_pins_after_a_release_pinning_a_newer_one_is_activated()
    {
        var tenant = fixture.Tenant;
        await tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await tenant.InsertRequestAsync(500m, 2);
        var instanceId = await tenant.StartAsync("PackageDescribe", subjectId, fixture.ReleaseId, fixture.Model);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);

        await fixture.ActivatePinnedAsync("1.1.0");
        await fixture.CompleteTaskAsync(instanceId, CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Waiting, "describe"), await InstanceStateAsync(instanceId));
        Assert.Equal("1.0.0", await tenant.ScalarAsync("SELECT version FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));
        Assert.Equal("1.0.0", (await fixture.RequestAsync(subjectId)).Values["packageVersion"]!.GetValue<string>());
        Assert.Equal("done", (await InstanceStateAsync(instanceId)).Step);
    }

    [Fact]
    public async Task An_instance_started_on_a_release_pinning_a_newer_version_runs_that_version()
    {
        var tenant = fixture.Tenant;
        await tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var (releaseId, model) = await fixture.ActivatePinnedAsync("1.1.0");
        var subjectId = await tenant.InsertRequestAsync(500m, 2);
        var instanceId = await tenant.StartAsync("PackageDescribe", subjectId, releaseId, model);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));

        await fixture.CompleteTaskAsync(instanceId, CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));
        Assert.Equal("1.1.0", await tenant.ScalarAsync("SELECT version FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal("1.1.0", (await fixture.RequestAsync(subjectId)).Values["packageVersion"]!.GetValue<string>());
    }

    private async Task<(string State, string Step)> InstanceStateAsync(Guid instanceId)
    {
        var instance = await fixture.Tenant.InstanceAsync(instanceId);
        return (instance.State, instance.Step);
    }
}
