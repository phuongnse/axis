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
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteDataClassHandler CreateHandler() => new(_dcRepo, _uow);

    [Fact]
    public async Task DeleteDataClass_WhenNotReferenced_DeletesAndSaves()
    {
        DataClass dc = DataClass.Create("Address", null, OrgId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, OrgId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(false);

        Result result = await CreateHandler().Handle(new DeleteDataClassCommand(dc.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dc.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDataClass_WhenReferencedByModel_ReturnsConflict()
    {
        DataClass dc = DataClass.Create("Address", null, OrgId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, OrgId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(true);

        Result result = await CreateHandler().Handle(
            new DeleteDataClassCommand(dc.Id, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("referenced");
    }
}
