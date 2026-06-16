using Axis.DataModeling.Application.Queries.ExportRecordsCsv;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Queries;

public class ExportRecordsCsvHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private ExportRecordsCsvHandler CreateHandler() => new(_modelRepo, _recordRepo);

    [Fact]
    public async Task Export_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).ReturnsNull();

        Result<CsvExportDto> result = await CreateHandler().Handle(
            new ExportRecordsCsvQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Export_WhenModelHasRecords_ReturnsCsvWithHeaderAndRows()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        model.AddField("company", "Company", FieldType.Text, required: false, new TextFieldConfig());
        model.AddField("amount", "Amount", FieldType.Number, required: false, new NumberFieldConfig());

        DataRecord record = DataRecord.Create(ModelId, TeamAccountId,
            new Dictionary<string, object?> { ["company"] = "Acme Corp", ["amount"] = 1500 }, UserId);

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetAllForExportAsync(ModelId, TeamAccountId, null, null, null, null)
            .Returns(AsyncEnumerableOf(record));

        Result<CsvExportDto> result = await CreateHandler().Handle(
            new ExportRecordsCsvQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Contain("\"Company\"");
        result.Value.Content.Should().Contain("\"Amount\"");
        result.Value.Content.Should().Contain("\"Acme Corp\"");
        result.Value.Content.Should().Contain("\"1500\"");
        result.Value.FileName.Should().StartWith("invoice-records-");
        result.Value.FileName.Should().EndWith(".csv");
    }

    [Fact]
    public async Task Export_WhenValueContainsDoubleQuotes_EscapesCorrectly()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        model.AddField("notes", "Notes", FieldType.Text, required: false, new TextFieldConfig());

        DataRecord record = DataRecord.Create(ModelId, TeamAccountId,
            new Dictionary<string, object?> { ["notes"] = "He said \"hello\"" }, UserId);

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetAllForExportAsync(ModelId, TeamAccountId, null, null, null, null)
            .Returns(AsyncEnumerableOf(record));

        Result<CsvExportDto> result = await CreateHandler().Handle(
            new ExportRecordsCsvQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // RFC 4180: double-quotes inside a quoted field are doubled
        result.Value.Content.Should().Contain("\"He said \"\"hello\"\"\"");
    }

    [Theory]
    [InlineData("=SUM(A1:A10)")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1+1)")]
    public async Task Export_WhenValueStartsWithFormulaChar_PrefixesSingleQuote(string formulaValue)
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        model.AddField("notes", "Notes", FieldType.Text, required: false, new TextFieldConfig());

        DataRecord record = DataRecord.Create(ModelId, TeamAccountId,
            new Dictionary<string, object?> { ["notes"] = formulaValue }, UserId);

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetAllForExportAsync(ModelId, TeamAccountId, null, null, null, null)
            .Returns(AsyncEnumerableOf(record));

        Result<CsvExportDto> result = await CreateHandler().Handle(
            new ExportRecordsCsvQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Value is prefixed with ' to neutralise spreadsheet formula execution
        result.Value.Content.Should().Contain($"\"'{formulaValue}\"");
    }

    [Fact]
    public async Task Export_WhenNoRecords_ReturnsCsvWithHeaderOnly()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        model.AddField("company", "Company", FieldType.Text, required: false, new TextFieldConfig());

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetAllForExportAsync(ModelId, TeamAccountId, null, null, null, null)
            .Returns(AsyncEnumerableOf<DataRecord>());

        Result<CsvExportDto> result = await CreateHandler().Handle(
            new ExportRecordsCsvQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Contain("\"Company\"");
        // Only header line — count the line breaks
        int lineCount = result.Value.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        lineCount.Should().Be(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<T> AsyncEnumerableOf<T>(params T[] items)
    {
        foreach (T item in items)
            yield return item;
        await Task.CompletedTask;
    }
}
