using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Application.Commands.UpdateModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateModelHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateModelHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel BuildModel() =>
        DataModel.Create("Invoice", null, null, null, OrgId, UserId);

    [Fact]
    public async Task Happy_path_updates_model_and_saves()
    {
        var model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);
        _modelRepo.NameExistsAsync("Updated Invoice", OrgId, model.Id).Returns(false);

        await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, OrgId, "Updated Invoice", "desc", null, null),
            CancellationToken.None);

        model.Name.Should().Be("Updated Invoice");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_throws()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).Returns((DataModel?)null);

        var act = async () => await CreateHandler().Handle(
            new UpdateModelCommand(Guid.NewGuid(), OrgId, "X", null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task Duplicate_name_throws()
    {
        var model = BuildModel();
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);
        _modelRepo.NameExistsAsync("Other", OrgId, model.Id).Returns(true);

        var act = async () => await CreateHandler().Handle(
            new UpdateModelCommand(model.Id, OrgId, "Other", null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*already exists*");
    }
}
