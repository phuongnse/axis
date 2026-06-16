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
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateModelHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel BuildModel() =>
        DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);

    [Fact]
    public async Task UpdateModel_WhenRequestIsValid_UpdatesModelAndSaves()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _modelRepo.NameExistsAsync("Updated Invoice", TeamAccountId, model.Id).Returns(false);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, TeamAccountId, "Updated Invoice", "desc", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Name.Should().Be("Updated Invoice");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateModel_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).Returns((DataModel?)null);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(Guid.NewGuid(), TeamAccountId, "X", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateModel_WhenModelBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, otherTeamAccountId, "Updated Invoice", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _modelRepo.Received(1).GetByIdAsync(model.Id, otherTeamAccountId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateModel_WhenNameIsDuplicate_ReturnsConflict()
    {
        DataModel model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _modelRepo.NameExistsAsync("Other", TeamAccountId, model.Id).Returns(true);

        Result result = await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, TeamAccountId, "Other", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
