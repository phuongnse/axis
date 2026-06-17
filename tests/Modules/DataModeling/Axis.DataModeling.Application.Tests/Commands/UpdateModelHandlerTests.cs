using Axis.DataModeling.Application.Commands.UpdateModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateModelHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateModelHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel BuildModel() =>
        DataModel.Create("Invoice", null, null, null, WorkspaceId, UserId);

    [Fact]
    public async Task UpdateModel_WhenRequestIsValid_UpdatesModelAndSaves()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, WorkspaceId).Returns(model);
        _modelRepo.NameExistsAsync("Updated Invoice", WorkspaceId, model.Id).Returns(false);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, WorkspaceId, "Updated Invoice", "desc", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Name.Should().Be("Updated Invoice");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateModel_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId).Returns((DataModel?)null);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(Guid.NewGuid(), WorkspaceId, "X", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateModel_WhenModelBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, WorkspaceId).Returns(model);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, otherWorkspaceId, "Updated Invoice", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _modelRepo.Received(1).GetByIdAsync(model.Id, otherWorkspaceId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateModel_WhenNameIsDuplicate_ReturnsConflict()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, WorkspaceId).Returns(model);
        _modelRepo.NameExistsAsync("Other", WorkspaceId, model.Id).Returns(true);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, WorkspaceId, "Other", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
