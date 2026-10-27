using System.Text.RegularExpressions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>Reads and checks the query plans that the performance tests assert on.</summary>
internal static class QueryPlans
{
    /// <summary>The plan contains a scan using <paramref name="index"/> and no full table read.</summary>
    public static void AssertUsesIndex(string plan, string index)
    {
        var escaped = Regex.Escape(index);
        Assert.Matches($@"(Index Scan|Index Only Scan)( Backward)? using {escaped} |Bitmap Index Scan on {escaped}\b", plan);
        Assert.DoesNotContain("Seq Scan", plan, StringComparison.Ordinal);
    }

    /// <summary>The plan contains a scan using <paramref name="index"/> and no full read of <paramref name="table"/>.</summary>
    /// <remarks>Other tables in the plan may be read in full. A small one rightly is.</remarks>
    public static void AssertUsesIndex(string plan, string index, string table)
    {
        var escaped = Regex.Escape(index);
        Assert.Matches($@"(Index Scan|Index Only Scan)( Backward)? using {escaped} |Bitmap Index Scan on {escaped}\b", plan);
        Assert.DoesNotMatch($@"Seq Scan on {Regex.Escape(table)}\b", plan);
    }

    /// <summary>Plans <paramref name="sql"/> with its parameters, writes the plan to the test output and returns it.</summary>
    public static async Task<string> ExplainAsync(NpgsqlDataSource dataSource, string sql, Action<NpgsqlCommand> bind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"EXPLAIN (COSTS OFF) {sql}");
        bind(command);
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }

        var plan = string.Join('\n', lines);
        TestContext.Current.TestOutputHelper?.WriteLine(plan);
        return plan;
    }
}
