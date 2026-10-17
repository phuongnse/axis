using Axis.Worker;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker reads the pending computed field names of each application into the
/// <c>axis.computed.pending_fields</c> gauge. The test adds the computed field <c>doubled</c> to the
/// Request in release R1, which computes it inline, and changes its expression in release R2, whose
/// activation leaves the entity pending.
/// </summary>
public sealed class WorkerPendingGaugeTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_activation_that_leaves_an_entity_pending_shows_its_pending_field_names_until_the_backfill_ends()
    {
        await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await fixture.ActivateEditedAsync(TenantA, Edit("divisor * 2"));
        await fixture.ActivateEditedAsync(TenantA, Edit("divisor * 3"), inlineRecomputeLimit: 0);
        var expected = await fixture.SumPendingComputedFieldNamesAsync(TenantA);
        Assert.True(expected > 0);
        using var collector = new MetricCollector();
        var gauges = fixture.WorkerService<WorkerGauges>();
        var tags = new[] { ("axis.tenant", TenantA), ("axis.application", "StepApp") };

        await gauges.RefreshAsync(CancellationToken);

        Assert.Equal(expected, collector.GaugeValue("axis.computed.pending_fields", tags));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        Assert.Equal(0, await fixture.SumPendingComputedFieldNamesAsync(TenantA));

        // A new collector holds only the exports after the refresh, so it never shows a value from the first one.
        using var afterBackfill = new MetricCollector();
        await gauges.RefreshAsync(CancellationToken);

        Assert.True(afterBackfill.HasPoint("axis.computed.pending_fields", tags), "The refresh after the backfill published no point for the application.");
        Assert.Equal(0, afterBackfill.GaugeValue("axis.computed.pending_fields", tags));
    }

    private static Func<string, string, string?> Edit(string expression) =>
        (path, content) => path == Path.Combine("entities", "request.json")
            ? content.Replace(
                "\"fields\": [",
                $"\"fields\": [\n    {{ \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"{expression}\" }},",
                StringComparison.Ordinal)
            : content;
}
