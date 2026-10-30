using System.IO.Compression;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Expressions.Tests;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

/// <summary>
/// Fuzz tests of the CSV and the xlsx import readers, as docs/reference/fuzz-tests.md describes. A file of any bytes
/// ends as a header, rows and row errors, or as a refusal, and never as an exception or a hang. A workbook that
/// passes the import limits is refused before anything is unpacked, and the test measures the bytes the reader
/// allocated to show it.
/// </summary>
public sealed class RecordImportFuzzTests
{
    private const string TooLarge = "Unpacks to more than the import limit.";

    private const long MaxEntries = 1_000;

    private const long MaxUnpackedBytes = 100L * 1024 * 1024;

    private static readonly IReadOnlyList<string> _header = ["name", "quantity", "ratio", "price", "active", "day", "moment"];

    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1c01"),
        "Fuzzed",
        "entities/fuzzed.json",
        Field("name", FieldType.Text),
        Field("quantity", FieldType.Integer),
        Field("ratio", FieldType.Decimal),
        Field("price", FieldType.Money, precision: 12, scale: 2),
        Field("active", FieldType.Boolean),
        Field("day", FieldType.Date),
        Field("moment", FieldType.DateTime));

    private static readonly ApplicationModel _application = Application(_entity);

    [Fact]
    public void Csv_reader_returns_rows_or_refusals_for_any_bytes() =>
        Fuzz.Run(
            random => ImportFuzzInputs.Csv(random, _header),
            input => Read(input, xlsx: false),
            ImportFuzzInputs.Show);

    [Fact]
    public void Xlsx_reader_returns_rows_or_refusals_for_any_workbook() =>
        Fuzz.Run(
            random => ImportFuzzInputs.Xlsx(random, _header),
            input => Read(input, xlsx: true),
            ImportFuzzInputs.Show);

    [Fact]
    public async Task The_read_measure_sees_a_workbook_that_is_read()
    {
        const int Characters = 20 * 1024 * 1024;
        var workbook = ImportFuzzInputs.WorkbookWithBigCell(Characters);

        var (xlsx, allocated) = await OpenAsync(workbook);

        Assert.Null(xlsx.HeaderError);
        Assert.Equal(Characters, Assert.Single(xlsx.Header!).Length);
        Assert.True(allocated > 32L * 1024 * 1024, $"The read allocated only {allocated} bytes.");
    }

    [Fact]
    public async Task A_workbook_that_unpacks_past_the_limit_is_refused_at_the_header_without_reading_it()
    {
        var (xlsx, allocated) = await OpenAsync(ImportFuzzInputs.SheetBomb());

        Assert.Equal(TooLarge, xlsx.HeaderError);
        Assert.Null(xlsx.Header);
        Assert.True(allocated < 16L * 1024 * 1024, $"The refusal allocated {allocated} bytes.");
    }

    // Opens the workbook on a thread of its own, so the allocation count is the reader's alone.
    private static Task<(RecordImportXlsx Xlsx, long Allocated)> OpenAsync(byte[] workbook) =>
        Task.Run(
            () =>
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var xlsx = RecordImportXlsx.Open(workbook);
                return (xlsx, GC.GetAllocatedBytesForCurrentThread() - before);
            },
            TestContext.Current.CancellationToken);

    // Reads the whole file and checks it. It runs on its own thread, so the allocation count is its own.
    private static void Read(ImportFuzzInput input, bool xlsx)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        IRecordImportFile file = xlsx ? RecordImportXlsx.Open(input.File) : RecordImportCsv.Open(input.File);
        var rows = new List<RecordImportFileRow>();
        if (file.Header is { } header)
        {
            Assert.Null(file.HeaderError);
            Assert.NotEmpty(header);
            rows.AddRange(file.Rows());
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        CheckRows(file, rows);
        if (xlsx)
        {
            CheckLimits(input, file, allocated);
        }

        if (file.Header is { } readable)
        {
            CheckColumns(readable, rows);
        }
    }

    private static void CheckRows(IRecordImportFile file, List<RecordImportFileRow> rows)
    {
        var previous = 0;
        foreach (var row in rows)
        {
            Assert.True((row.Cells is null) != (row.Error is null), $"Row {row.Number} has {(row.Cells is null ? "neither cells nor an error" : "both cells and an error")}.");
            Assert.True(row.Number > previous, $"Row {row.Number} follows row {previous}.");
            previous = row.Number;
            if (row.Cells is not null)
            {
                Assert.Equal(file.Header!.Count, row.Cells.Count);
            }
        }
    }

    // A workbook whose directory announces more than the limits is refused before it is read.
    private static void CheckLimits(ImportFuzzInput input, IRecordImportFile file, long allocated)
    {
        if (input.Kind == ImportFuzzInputs.LyingBomb)
        {
            Assert.True(allocated < 64L * 1024 * 1024, $"The reader allocated {allocated} bytes for a part that announces a small size.");
            return;
        }

        if (!AnnouncesMoreThanTheLimits(input.File))
        {
            return;
        }

        Assert.Equal(TooLarge, file.HeaderError);
        Assert.Null(file.Header);
        Assert.True(allocated < 16L * 1024 * 1024, $"The refusal allocated {allocated} bytes.");
    }

    private static bool AnnouncesMoreThanTheLimits(byte[] file)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            return archive.Entries.Count > MaxEntries || archive.Entries.Sum(entry => entry.Length) > MaxUnpackedBytes;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    // The pure part of the dry run: the columns of the header, the body of each row and the typed value of each cell.
    private static void CheckColumns(IReadOnlyList<string> header, List<RecordImportFileRow> rows)
    {
        var (columns, errors) = RecordImportColumns.Map(header, _entity);
        Assert.True((columns is null) != (errors is null));
        if (columns is null)
        {
            return;
        }

        foreach (var cells in rows.Where(row => row.Cells is not null).Select(row => row.Cells!))
        {
            using var body = JsonDocument.Parse(columns.Body(cells));
            Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("values").ValueKind);
            for (var index = 0; index < cells.Count; index++)
            {
                columns.Value(_application, _entity, cells, index);
            }
        }
    }
}
