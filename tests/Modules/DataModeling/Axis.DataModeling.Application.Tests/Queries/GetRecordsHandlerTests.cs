using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetRecordsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetRecordsHandler CreateHandler() => new(_modelRepo, _recordRepo);

    [Fact]
    public async Task GetRecords_WhenModelExists_ReturnsPagedResult()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TeamAccountId, UserId);
        List<DataRecord> records =
        [
            DataRecord.Create(ModelId, TeamAccountId, new Dictionary<string, object?> { ["x"] = 1 }, UserId),
        ];
        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetPagedAsync(ModelId, TeamAccountId, 1, 25, null, null, null, null)
            .Returns((records.AsReadOnly() as IReadOnlyList<DataRecord>, 1));

        Result<RecordsPageDto> result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Records.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetRecords_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns((DataModel?)null);

        Result<RecordsPageDto> result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, TeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetRecords_WhenFiltersProvided_PassesFiltersToRepository()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TeamAccountId, UserId);
        IReadOnlyList<RecordFilter> filters = new List<RecordFilter> { new("status", "eq", "active") }.AsReadOnly();

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetPagedAsync(ModelId, TeamAccountId, 1, 25, null, filters, null, null)
            .Returns((new List<DataRecord>().AsReadOnly() as IReadOnlyList<DataRecord>, 0));

        Result<RecordsPageDto> result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, TeamAccountId, Filters: filters), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _recordRepo.Received(1).GetPagedAsync(ModelId, TeamAccountId, 1, 25, null, filters, null, null);
    }

    [Fact]
    public async Task GetRecords_WhenModelBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TeamAccountId, UserId);
        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result<RecordsPageDto> result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, otherTeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _modelRepo.Received(1).GetByIdAsync(ModelId, otherTeamAccountId);
        await _recordRepo.DidNotReceive().GetPagedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<string?>(), Arg.Any<IReadOnlyList<RecordFilter>?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task GetRecords_WhenSortProvided_PassesSortToRepository()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TeamAccountId, UserId);

        _modelRepo.GetByIdAsync(ModelId, TeamAccountId).Returns(model);
        _recordRepo.GetPagedAsync(ModelId, TeamAccountId, 1, 25, null, null, "name", "asc")
            .Returns((new List<DataRecord>().AsReadOnly() as IReadOnlyList<DataRecord>, 0));

        Result<RecordsPageDto> result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, TeamAccountId, SortBy: "name", SortDir: "asc"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _recordRepo.Received(1).GetPagedAsync(ModelId, TeamAccountId, 1, 25, null, null, "name", "asc");
    }
}
