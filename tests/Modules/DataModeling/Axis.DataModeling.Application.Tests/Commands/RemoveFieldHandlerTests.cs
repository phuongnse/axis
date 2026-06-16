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
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private RemoveFieldHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task RemoveField_WhenFieldExists_RemovesFieldAndSaves()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        FieldDefinition field = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());
        int fieldCountBefore = model.Fields.Count;
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result result = await CreateHandler().Handle(new RemoveFieldCommand(model.Id, field.Id, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Fields.Should().HaveCount(fieldCountBefore - 1);
        model.Fields.Should().NotContain(f => f.Id == field.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveField_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId).Returns((DataModel?)null);

        Result result = await CreateHandler().Handle(
            new RemoveFieldCommand(Guid.NewGuid(), Guid.NewGuid(), TenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RemoveField_WhenModelBelongsToAnotherTenant_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        FieldDefinition field = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Guid otherTenantId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new RemoveFieldCommand(model.Id, field.Id, otherTenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RemoveField_WhenFieldIsSystem_ReturnsBusinessRuleFailure()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        FieldDefinition sysField = model.Fields.First(f => f.IsSystem);
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result result = await CreateHandler().Handle(
            new RemoveFieldCommand(model.Id, sysField.Id, TenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("system field");
    }
}
