using System.Diagnostics;
using Axis.Configuration.Compilation;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

/// <summary>
/// A data source filter that calls rules deeply is refused after a bounded amount of work, not an
/// exponential one. These run through the whole compiler, which reports no count, so they time
/// it. The inlining cap itself is proven by a count in the expression tests.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class DataSourceFilterPerformanceTests
{
    [Fact]
    public void Filter_whose_rule_calls_inline_past_the_cap_is_outside_the_subset()
    {
        // L7 calls L6 four times, and so on down to L0, so inlined it is far past 2,000 nodes. The
        // chain is 8 rules deep, the most the rule call depth limit allows.
        using var folder = DataSourceCompilerTests.Folder().With("rules/l0.json", """
            { "id": "77777777-7777-4777-8777-777777777700", "kind": "rule", "name": "L0", "formatVersion": 1,
              "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean", "expression": "value > 0" }
            """);
        for (var k = 1; k <= 7; k++)
        {
            var call = $"L{k - 1}(value)";
            folder.With($"rules/l{k}.json", $$"""
                { "id": "77777777-7777-4777-8777-7777777777{{k:00}}", "kind": "rule", "name": "L{{k}}", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean",
                  "expression": "{{call}} and {{call}} and {{call}} and {{call}}" }
                """);
        }

        folder.With("data-sources/orders.json", DataSourceCompilerTests.DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            """, "filter": "L7(amount)" """));
        var stopwatch = Stopwatch.StartNew();

        var result = ApplicationCompiler.Compile(folder.Path);

        stopwatch.Stop();
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.OutsideSqlSubset, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'L7'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Filter_whose_rule_calls_fan_out_wide_is_checked_in_linear_time()
    {
        // Each rule calls the one below it 20 times, so a walk per call would visit 20^7 bodies. The
        // chain is 8 rules deep, the most the rule call depth limit allows.
        using var folder = DataSourceCompilerTests.Folder().With("rules/w0.json", """
            { "id": "77777777-7777-4777-8777-777777777800", "kind": "rule", "name": "W0", "formatVersion": 1,
              "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean", "expression": "value > 0" }
            """);
        for (var k = 1; k <= 7; k++)
        {
            var calls = string.Join(" and ", Enumerable.Repeat($"W{k - 1}(value)", 20));
            folder.With($"rules/w{k}.json", $$"""
                { "id": "77777777-7777-4777-8777-7777777778{{k:00}}", "kind": "rule", "name": "W{{k}}", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean",
                  "expression": "{{calls}}" }
                """);
        }

        folder.With("data-sources/orders.json", DataSourceCompilerTests.DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            """, "filter": "W7(amount)" """));
        var stopwatch = Stopwatch.StartNew();

        var result = ApplicationCompiler.Compile(folder.Path);

        stopwatch.Stop();
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.OutsideSqlSubset, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'W7'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Took {stopwatch.Elapsed}.");
    }
}
