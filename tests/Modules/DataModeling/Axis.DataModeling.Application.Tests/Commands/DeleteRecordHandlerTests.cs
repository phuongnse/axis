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
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteRecordHandler CreateHandler() => new(_recordRepo, _uow);

    [Fact]
    public async Task DeleteRecord_WhenRecordExists_SoftDeletesAndSaves()
    {
        DataRecord record = DataRecord.Create(ModelId, TeamAccountId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        _recordRepo.GetByIdAsync(record.Id, ModelId, TeamAccountId).Returns(record);

        Result result = await CreateHandler().Handle(
            new DeleteRecordCommand(record.Id, ModelId, TeamAccountId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        record.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRecord_WhenRecordNotFound_ReturnsNotFound()
    {
        _recordRepo.GetByIdAsync(Arg.Any<Guid>(), ModelId, TeamAccountId).Returns((DataRecord?)null);

        Result result = await CreateHandler().Handle(
            new DeleteRecordCommand(Guid.NewGuid(), ModelId, TeamAccountId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteRecord_WhenRecordBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        DataRecord record = DataRecord.Create(ModelId, TeamAccountId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        _recordRepo.GetByIdAsync(record.Id, ModelId, TeamAccountId).Returns(record);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeleteRecordCommand(record.Id, ModelId, otherTeamAccountId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
