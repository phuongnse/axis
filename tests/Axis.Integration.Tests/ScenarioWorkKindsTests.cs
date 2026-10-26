using Axis.Configuration.Packages;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Scenarios.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>A test scenario's <c>advanceTime</c> runs every kind of work item that process work queues.</summary>
public sealed class ScenarioWorkKindsTests
{
    /// <summary>
    /// The kinds that <c>advanceTime</c> does not run on purpose. <c>computed.backfill</c> is data maintenance,
    /// not process work, and scenarios have never run it. <c>records.importCheck</c> is the dry run of an
    /// uploaded import file, not process work, and scenarios have no import step. A new kind fails the test
    /// until <c>advanceTime</c> runs it or it is added here on purpose.
    /// </summary>
    private static readonly string[] _excluded = [ComputedBackfill.WorkItemKind, RecordImports.CheckWorkItemKind];

    [Fact]
    public void Advance_time_runs_every_work_item_kind_named_in_the_code_except_the_excluded_ones()
    {
        var kinds = new[] { typeof(ProcessStarts).Assembly, typeof(ComputedBackfill).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string) && field.Name.EndsWith("WorkItemKind", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct()
            .ToList();
        using var work = new ScenarioWork("Host=localhost;Database=unused", NullLoggerFactory.Instance, 10, PackageCatalog.Empty, new ScenarioStubs([]));

        // The search finds the kinds, so a rename of the constants cannot make the test pass with none.
        Assert.Contains(ProcessStarts.TimerWorkItemKind, kinds);
        Assert.Contains(ComputedBackfill.WorkItemKind, kinds);
        Assert.Equal(
            [],
            kinds.Where(kind => !work.TimeKinds.Contains(kind) && !_excluded.Contains(kind)).Order(StringComparer.Ordinal));
        Assert.All(_excluded, kind => Assert.DoesNotContain(kind, work.TimeKinds));
    }
}
