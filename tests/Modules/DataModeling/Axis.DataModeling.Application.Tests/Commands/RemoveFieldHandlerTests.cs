using Axis.DataModeling.Application.Commands.RemoveField;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class RemoveFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private RemoveFieldHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task Happy_path_removes_field_and_saves()
    {
        var model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        var field = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());
        var fieldCountBefore = model.Fields.Count;
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        await CreateHandler().Handle(new RemoveFieldCommand(model.Id, field.Id, OrgId), CancellationToken.None);

        model.Fields.Should().HaveCount(fieldCountBefore - 1);
        model.Fields.Should().NotContain(f => f.Id == field.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task System_field_removal_throws()
    {
        var model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        var sysField = model.Fields.First(f => f.IsSystem);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        var act = async () => await CreateHandler().Handle(
            new RemoveFieldCommand(model.Id, sysField.Id, OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*system field*");
    }
}
