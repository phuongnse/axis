using Axis.DataModeling.Application.Commands.CreateRecord;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class CreateRecordHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IDataRecordRepository _recordRepo = Substitute.For<IDataRecordRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateRecordHandler CreateHandler() => new(_modelRepo, _recordRepo, _uow);

    [Fact]
    public async Task CreateRecord_WhenModelExists_CreatesRecordAndReturnsId()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, OrgId, UserId);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Dictionary<string, object?> data = new() { ["amount"] = 100 };

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(model.Id, OrgId, data, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _recordRepo.Received(1).AddAsync(Arg.Any<DataRecord>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRecord_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new CreateRecordCommand(Guid.NewGuid(), OrgId, new Dictionary<string, object?>(), UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
