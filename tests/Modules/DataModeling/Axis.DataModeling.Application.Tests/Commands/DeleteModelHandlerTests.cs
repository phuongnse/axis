using Axis.DataModeling.Application.Commands.DeleteModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
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

        await CreateHandler().Handle(new DeleteModelCommand(model.Id, OrgId), CancellationToken.None);

        model.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_throws_validation_exception()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new DeleteModelCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
