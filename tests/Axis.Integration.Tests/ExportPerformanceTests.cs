using System.Diagnostics;
using System.Net;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Npgsql;
using static Axis.Integration.Tests.PerformanceServerFixture;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An export of 100,000 records streams to the client within its time limit and grows the live managed heap by
/// less than 64 MB. The rows are written to a temporary file as they are read, so the heap must not hold them. The
/// heap is measured on separate exports that are not timed. The class runs alone, so no other test competes for
/// CPU with the timed exports.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class ExportPerformanceTests(ExportPerformanceTests.ExportFixture fixture) : IClassFixture<ExportPerformanceTests.ExportFixture>
{
    private const int RequestCount = 100_000;
    private const int DepartmentCount = 1_000;
    private const int Attempts = 3;
    private const long HeapGrowthLimit = 64L * 1024 * 1024;
    private const string ExportPath = "/api/apps/ExportPerformanceApp/entities/Request/records/export";
    private static readonly string[] _header = ["title", "amount", "department", "status", "createdOn"];

    // One export of 100,000 records took about 0.35 s locally as CSV and about 1.2 s as xlsx, at the best of three.
    // The limits are about five times that, rounded up to whole seconds, so shared CI runners do not make the test
    // flaky, and they still catch a slowdown of that size. The best of three exports counts, because a runner can
    // pause the process for a garbage collection or another job. The heap is measured on three more exports that are
    // not timed, because the sampler forces collections.
    private static readonly TimeSpan _csvTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _xlsxTime = TimeSpan.FromSeconds(6);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task An_export_of_a_hundred_thousand_records_finishes_within_its_time_limit_and_grows_the_heap_by_less_than_64_mb(string format)
    {
        // The first export warms up the query plan and the code paths, so it is not timed.
        await ExportAsync(format, measureHeap: false);

        var best = await ExportAsync(format, measureHeap: false);
        for (var attempt = 1; attempt < Attempts; attempt++)
        {
            var next = await ExportAsync(format, measureHeap: false);
            best = next.Elapsed < best.Elapsed ? next : best;
        }

        var smallestGrowth = long.MaxValue;
        var slowestMeasured = TimeSpan.Zero;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var measured = await ExportAsync(format, measureHeap: true);
            smallestGrowth = Math.Min(smallestGrowth, measured.HeapGrowth);
            slowestMeasured = measured.Elapsed > slowestMeasured ? measured.Elapsed : slowestMeasured;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The best of {Attempts} {format} exports took {best.Elapsed}, and the smallest growth of the live heap was {smallestGrowth:N0} bytes, and the slowest measured export took {slowestMeasured}.");
        var limit = format == "csv" ? _csvTime : _xlsxTime;
        Assert.True(best.Elapsed < limit, $"The {format} export took {best.Elapsed} at best of {Attempts} exports.");
        Assert.True(smallestGrowth < HeapGrowthLimit, $"The {format} export grew the live heap by {smallestGrowth:N0} bytes at least.");
    }

    /// <summary>
    /// Exports the records once and checks the file. The file is read only after the measurement. A timed export
    /// runs without the heap sampler, and a measured export is not timed, because the sampler forces collections.
    /// </summary>
    private async Task<Export> ExportAsync(string format, bool measureHeap)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
        };
        await using var file = new FileStream(Path.Combine(Path.GetTempPath(), $"axis-export-performance-{Guid.NewGuid():N}.tmp"), options);

        var sampler = measureHeap ? new LiveHeapSampler() : null;
        TimeSpan elapsed;
        HttpStatusCode status;
        bool truncated;
        long growth = 0;
        try
        {
            using var request = Request($"{ExportPath}?format={format}", Host);
            var stopwatch = Stopwatch.StartNew();
            using var response = await fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken);
            await using (var body = await response.Content.ReadAsStreamAsync(CancellationToken))
            {
                await body.CopyToAsync(file, CancellationToken);
            }

            stopwatch.Stop();
            elapsed = stopwatch.Elapsed;
            status = response.StatusCode;
            truncated = response.Headers.Contains("Axis-Export-Truncated");
        }
        finally
        {
            if (sampler is not null)
            {
                growth = sampler.Stop();
            }
        }

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(truncated);
        if (format == "csv")
        {
            AssertCsv(file);
        }
        else
        {
            AssertXlsx(file);
        }

        return new Export(elapsed, growth);
    }

    /// <summary>A byte order mark, the header, then one CRLF line per record.</summary>
    private static void AssertCsv(FileStream file)
    {
        file.Position = 0;
        var bom = new byte[3];
        file.ReadExactly(bom);
        Assert.Equal([0xEF, 0xBB, 0xBF], bom);

        using var reader = new StreamReader(file, leaveOpen: true);
        Assert.Equal(string.Join(',', _header), reader.ReadLine());
        var rows = 1;
        while (reader.ReadLine() is not null)
        {
            rows++;
        }

        Assert.Equal(RequestCount + 1, rows);
    }

    /// <summary>One worksheet whose first row is the header, then one row per record, read without loading the sheet.</summary>
    private static void AssertXlsx(FileStream file)
    {
        file.Position = 0;
        using var document = SpreadsheetDocument.Open(file, false);
        var sheet = Assert.Single(document.WorkbookPart!.WorksheetParts);
        using var reader = OpenXmlReader.Create(sheet);
        var rows = 0;
        while (reader.Read())
        {
            if (!reader.IsStartElement || reader.ElementType != typeof(Row))
            {
                continue;
            }

            rows++;
            if (rows == 1)
            {
                var header = (Row)reader.LoadCurrentElement()!;
                Assert.Equal(_header, header.Elements<Cell>().Select(cell => cell.InlineString!.Text!.Text));
            }
        }

        Assert.Equal(RequestCount + 1, rows);
    }

    private sealed record Export(TimeSpan Elapsed, long HeapGrowth);

    /// <summary>
    /// The ExportPerformanceApp fixture with 1,000 departments and 100,000 requests. Request n belongs to
    /// department n % 1000, has amount n % 100, and is open, approved or rejected as n % 3 is 0, 1 or 2.
    /// </summary>
    public sealed class ExportFixture : PerformanceServerFixture
    {
        public ExportFixture()
            : base("ExportPerformanceApp", "exporter")
        {
        }

        protected override async Task SeedAsync(NpgsqlDataSource dataSource, ApplicationModel model)
        {
            Assert.True(model.TryGetEntity("Department", out var department));
            Assert.True(model.TryGetEntity("Request", out var request));
            var departments = Enumerable.Range(0, DepartmentCount).Select(_ => Guid.NewGuid()).ToArray();
            var id = EntityNaming.Quote(EntityNaming.IdColumn);
            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(department)} ({id}, {Column("name")})
                SELECT (@departments::uuid[])[k + 1], 'Dept ' || k
                FROM generate_series(0, {DepartmentCount - 1}) AS k
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(request)} ({id}, {Column("title")}, {Column("amount")}, {Column("department")}, {Column("status")}, {Column("createdOn")})
                SELECT gen_random_uuid(), 'Request ' || n, n % 100, (@departments::uuid[])[1 + n % {DepartmentCount}],
                    (ARRAY['open', 'approved', 'rejected'])[1 + n % 3], DATE '2026-01-01' + (n % 365)
                FROM generate_series(1, {RequestCount}) AS n
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            // Updates the planner's statistics, as autovacuum does in production.
            await using (var analyze = dataSource.CreateCommand($"ANALYZE {Table(department)}, {Table(request)}"))
            {
                await analyze.ExecuteNonQueryAsync(CancellationToken);
            }
        }
    }
}
