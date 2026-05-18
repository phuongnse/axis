using Axis.DataModeling.Application.Commands.RemoveField;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
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
    public async Task RemoveField_WhenFieldExists_RemovesFieldAndSaves()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition field = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());
        int fieldCountBefore = model.Fields.Count;
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(new RemoveFieldCommand(model.Id, field.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Fields.Should().HaveCount(fieldCountBefore - 1);
        model.Fields.Should().NotContain(f => f.Id == field.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveField_WhenFieldIsSystem_ReturnsBusinessRuleFailure()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition sysField = model.Fields.First(f => f.IsSystem);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(
            new RemoveFieldCommand(model.Id, sysField.Id, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("system field");
    }
}
