using Axis.DataModeling.Application.Commands.DeleteModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class DeleteModelHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IModelDeletionGuard _deletionGuard = Substitute.For<IModelDeletionGuard>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    public DeleteModelHandlerTests()
    {
        _deletionGuard.ValidateCanDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private DeleteModelHandler CreateHandler() => new(_modelRepo, _deletionGuard, _uow);

    [Fact]
    public async Task DeleteModel_WhenModelExists_SoftDeletesModel()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);

        Result result = await CreateHandler().Handle(new DeleteModelCommand(model.Id, TeamAccountId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteModel_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeleteModelCommand(Guid.NewGuid(), TeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteModel_WhenModelBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeleteModelCommand(model.Id, otherTeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteModel_WhenGuardBlocksDeletion_ReturnsConflictWithoutDeleting()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TeamAccountId, UserId);
        _modelRepo.GetByIdAsync(model.Id, TeamAccountId).Returns(model);
        _deletionGuard.ValidateCanDeleteAsync(model.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorCodes.Conflict, "This model is used by 2 form(s). Remove those references before deleting."));

        Result result = await CreateHandler().Handle(new DeleteModelCommand(model.Id, TeamAccountId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        model.DeletedAt.Should().BeNull();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
