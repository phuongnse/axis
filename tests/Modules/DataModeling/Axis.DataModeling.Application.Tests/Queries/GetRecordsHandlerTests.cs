using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetRecordsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetRecordsHandler CreateHandler() => new(_modelRepo, _recordRepo);

    [Fact]
    public async Task Happy_path_returns_paged_result()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        List<DataRecord> records =
        [
            DataRecord.Create(ModelId, OrgId, new Dictionary<string, object?> { ["x"] = 1 }, UserId),
        ];
        _modelRepo.GetByIdAsync(ModelId, OrgId).Returns(model);
        _recordRepo.GetPagedAsync(ModelId, OrgId, 1, 25, null)
            .Returns((records.AsReadOnly() as IReadOnlyList<DataRecord>, 1));

        var result = await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, OrgId), CancellationToken.None);

        result.Records.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Model_not_found_throws()
    {
        _modelRepo.GetByIdAsync(ModelId, OrgId).Returns((DataModel?)null);

        var act = async () => await CreateHandler().Handle(
            new GetRecordsQuery(ModelId, OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
