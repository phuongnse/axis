using Axis.DataModeling.Application.Commands.DeleteRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class DeleteRecordHandlerTests
{
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteRecordHandler CreateHandler() => new(_recordRepo, _uow);

    [Fact]
    public async Task Happy_path_soft_deletes_and_saves()
    {
        DataRecord record = DataRecord.Create(ModelId, OrgId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        _recordRepo.GetByIdAsync(record.Id, ModelId, OrgId).Returns(record);

        Result result = await CreateHandler().Handle(
            new DeleteRecordCommand(record.Id, ModelId, OrgId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        record.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Record_not_found_returns_not_found()
    {
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), ModelId, OrgId).Returns((DataRecord?)null);

        Result result = await CreateHandler().Handle(
            new DeleteRecordCommand(Guid.NewGuid(), ModelId, OrgId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
