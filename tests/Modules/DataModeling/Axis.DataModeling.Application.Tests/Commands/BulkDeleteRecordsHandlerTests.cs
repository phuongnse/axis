using Axis.DataModeling.Application.Commands.BulkDeleteRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class BulkDeleteRecordsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private BulkDeleteRecordsHandler CreateHandler() => new(_modelRepo, _recordRepo);

    [Fact]
    public async Task BulkDelete_WhenNoIdsProvided_ReturnsBusinessRuleError()
    {
        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand([], ModelId, WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _recordRepo.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task BulkDelete_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(ModelId, WorkspaceId).ReturnsNull();

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand([Guid.NewGuid()], ModelId, WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _recordRepo.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task BulkDelete_WhenAllRecordsExist_ReturnsCorrectDeletedCount()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, WorkspaceId, UserId);
        IReadOnlyList<Guid> ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        _modelRepo.GetByIdAsync(ModelId, WorkspaceId).Returns(model);
        _recordRepo.BulkDeleteAsync(
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(3);

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand(ids, ModelId, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Be(3);
        result.Value.NotFound.Should().Be(0);
    }

    [Fact]
    public async Task BulkDelete_WhenDuplicateIdsProvided_DeduplicatesBeforeRepositoryCall()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, WorkspaceId, UserId);
        Guid id = Guid.NewGuid();
        IReadOnlyList<Guid> idsWithDuplicates = [id, id, id]; // same ID 3 times

        _modelRepo.GetByIdAsync(ModelId, WorkspaceId).Returns(model);
        _recordRepo.BulkDeleteAsync(
            Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 1),
            Arg.Is(ModelId), Arg.Is(WorkspaceId), Arg.Any<CancellationToken>()).Returns(1);

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand(idsWithDuplicates, ModelId, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Be(1);
        result.Value.NotFound.Should().Be(0); // not inflated by duplicates
        await _recordRepo.Received(1).BulkDeleteAsync(
            Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 1),
            Arg.Is(ModelId), Arg.Is(WorkspaceId));
    }

    [Fact]
    public async Task BulkDelete_WhenModelBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, WorkspaceId, UserId);
        _modelRepo.GetByIdAsync(ModelId, WorkspaceId).Returns(model);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand([Guid.NewGuid()], ModelId, otherWorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _recordRepo.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task BulkDelete_WhenSomeRecordsNotFound_ReturnsPartialDeletedCount()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, WorkspaceId, UserId);
        IReadOnlyList<Guid> ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        _modelRepo.GetByIdAsync(ModelId, WorkspaceId).Returns(model);
        _recordRepo.BulkDeleteAsync(
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Is(ModelId), Arg.Is(WorkspaceId), Arg.Any<CancellationToken>()).Returns(2); // one not found or already deleted

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand(ids, ModelId, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Be(2);
        result.Value.NotFound.Should().Be(1);
    }
}
