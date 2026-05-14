using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateRecordHandlerTests
{
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateRecordHandler CreateHandler() => new(_recordRepo, _uow);

    [Fact]
    public async Task UpdateRecord_WhenRecordExists_UpdatesDataAndSaves()
    {
        DataRecord record = DataRecord.Create(ModelId, OrgId, new Dictionary<string, object?> { ["name"] = "Old" }, UserId);
        _recordRepo.GetByIdAsync(record.Id, ModelId, OrgId).Returns(record);

        Dictionary<string, object?> newData = new() { ["name"] = "New" };
        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(record.Id, ModelId, OrgId, newData),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        record.Data["name"].Should().Be("New");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRecord_WhenRecordNotFound_ReturnsNotFound()
    {
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), ModelId, OrgId).Returns((DataRecord?)null);

        Result result = await CreateHandler().Handle(
            new UpdateRecordCommand(Guid.NewGuid(), ModelId, OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
