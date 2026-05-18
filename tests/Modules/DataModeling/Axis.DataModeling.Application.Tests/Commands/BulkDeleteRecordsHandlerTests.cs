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

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private BulkDeleteRecordsHandler CreateHandler() => new(_modelRepo, _recordRepo);

    [Fact]
    public async Task BulkDelete_WhenNoIdsProvided_ReturnsBusinessRuleError()
    {
        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand([], ModelId, OrgId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _recordRepo.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task BulkDelete_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(ModelId, OrgId).ReturnsNull();

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand([Guid.NewGuid()], ModelId, OrgId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _recordRepo.DidNotReceive().BulkDeleteAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task BulkDelete_WhenAllRecordsExist_ReturnsCorrectDeletedCount()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, OrgId, UserId);
        IReadOnlyList<Guid> ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        _modelRepo.GetByIdAsync(ModelId, OrgId).Returns(model);
        _recordRepo.BulkDeleteAsync(ids, ModelId, OrgId).Returns(3);

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand(ids, ModelId, OrgId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Be(3);
        result.Value.NotFound.Should().Be(0);
    }

    [Fact]
    public async Task BulkDelete_WhenSomeRecordsNotFound_ReturnsPartialDeletedCount()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, OrgId, UserId);
        IReadOnlyList<Guid> ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        _modelRepo.GetByIdAsync(ModelId, OrgId).Returns(model);
        _recordRepo.BulkDeleteAsync(ids, ModelId, OrgId).Returns(2); // one not found or already deleted

        Result<BulkDeleteResult> result = await CreateHandler().Handle(
            new BulkDeleteRecordsCommand(ids, ModelId, OrgId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Be(2);
        result.Value.NotFound.Should().Be(1);
    }
}
