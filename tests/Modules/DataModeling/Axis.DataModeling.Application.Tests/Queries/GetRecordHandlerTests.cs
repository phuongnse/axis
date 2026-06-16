using Axis.DataModeling.Application.Queries.GetRecord;
using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Queries;

public class GetRecordHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();

    private GetRecordHandler CreateHandler() => new(_recordRepo);

    [Fact]
    public async Task Handle_WhenRecordExists_ReturnsRecordDto()
    {
        DataRecord record = DataRecord.Create(
            ModelId,
            TenantId,
            new Dictionary<string, object?> { ["status"] = "active" },
            "user-123");
        _recordRepo.GetByIdAsync(record.Id, ModelId, TenantId, Arg.Any<CancellationToken>()).Returns(record);

        Result<RecordDto> result = await CreateHandler().Handle(
            new GetRecordQuery(record.Id, ModelId, TenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(record.Id);
        result.Value.Data.Should().ContainKey("status");
    }

    [Fact]
    public async Task Handle_WhenRecordDoesNotExist_ReturnsNotFound()
    {
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), ModelId, TenantId, Arg.Any<CancellationToken>())
            .Returns((DataRecord?)null);

        Result<RecordDto> result = await CreateHandler().Handle(
            new GetRecordQuery(Guid.NewGuid(), ModelId, TenantId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
