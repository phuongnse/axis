using Axis.DataModeling.Application.Commands.DeleteDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class DeleteDataClassHandlerTests
{
    private readonly IDataClassRepository _dcRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteDataClassHandler CreateHandler() => new(_dcRepo, _uow);

    [Fact]
    public async Task DeleteDataClass_WhenNotReferenced_DeletesAndSaves()
    {
        DataClass dc = DataClass.Create("Address", null, WorkspaceId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, WorkspaceId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(false);

        Result result = await CreateHandler().Handle(new DeleteDataClassCommand(dc.Id, WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dc.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDataClass_WhenNotFound_ReturnsNotFound()
    {
        _dcRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId).Returns((DataClass?)null);

        Result result = await CreateHandler().Handle(
            new DeleteDataClassCommand(Guid.NewGuid(), WorkspaceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteDataClass_WhenBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        DataClass dc = DataClass.Create("Address", null, WorkspaceId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, WorkspaceId).Returns(dc);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeleteDataClassCommand(dc.Id, otherWorkspaceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteDataClass_WhenReferencedByModel_ReturnsConflict()
    {
        DataClass dc = DataClass.Create("Address", null, WorkspaceId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, WorkspaceId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(true);

        Result result = await CreateHandler().Handle(
            new DeleteDataClassCommand(dc.Id, WorkspaceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("referenced");
    }
}
