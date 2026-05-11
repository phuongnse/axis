using Axis.DataModeling.Application.Commands.UpdateField;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateFieldHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task Happy_path_updates_field_label_and_saves()
    {
        var model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        var field = model.AddField("price", "Price", FieldType.Number, false, new NumberFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        await CreateHandler().Handle(
            new UpdateFieldCommand(model.Id, field.Id, OrgId, "Unit Price", "help", true, new NumberFieldConfig(Min: 0)),
            CancellationToken.None);

        var updated = model.Fields.Single(f => f.Id == field.Id);
        updated.Label.Should().Be("Unit Price");
        updated.IsRequired.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Model_not_found_throws()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).Returns((DataModel?)null);

        var act = async () => await CreateHandler().Handle(
            new UpdateFieldCommand(Guid.NewGuid(), Guid.NewGuid(), OrgId, "L", null, false, new TextFieldConfig()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task System_field_update_throws()
    {
        var model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        var systemField = model.Fields.First(f => f.IsSystem);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        var act = async () => await CreateHandler().Handle(
            new UpdateFieldCommand(model.Id, systemField.Id, OrgId, "Bad", null, false, new TextFieldConfig()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*system field*");
    }
}
