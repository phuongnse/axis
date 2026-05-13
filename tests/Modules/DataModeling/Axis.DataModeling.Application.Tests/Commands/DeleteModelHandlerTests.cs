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
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteModelHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task Happy_path_soft_deletes_model()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, OrgId, UserId);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(new DeleteModelCommand(model.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_returns_not_found()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeleteModelCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
