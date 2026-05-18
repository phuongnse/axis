using Axis.DataModeling.Application.Commands.UpdateField;
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

public class UpdateFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private UpdateFieldHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task UpdateField_WhenFieldExists_UpdatesLabelAndSaves()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition field = model.AddField("price", "Price", FieldType.Number, false, new NumberFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(
            new UpdateFieldCommand(model.Id, field.Id, OrgId, "Unit Price", "help", true, new NumberFieldConfig(Min: 0)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        FieldDefinition updated = model.Fields.Single(f => f.Id == field.Id);
        updated.Label.Should().Be("Unit Price");
        updated.IsRequired.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateField_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).Returns((DataModel?)null);

        Result result = await CreateHandler().Handle(
            new UpdateFieldCommand(Guid.NewGuid(), Guid.NewGuid(), OrgId, "L", null, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateField_WhenModelBelongsToAnotherOrg_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition field = model.AddField("price", "Price", FieldType.Number, false, new NumberFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new UpdateFieldCommand(model.Id, field.Id, otherOrgId, "New Label", null, false, new NumberFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _modelRepo.Received(1).GetByIdAsync(model.Id, otherOrgId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateField_WhenFieldIsSystem_ReturnsBusinessRuleFailure()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, OrgId, UserId);
        FieldDefinition systemField = model.Fields.First(f => f.IsSystem);
        _modelRepo.GetByIdAsync(model.Id, OrgId).Returns(model);

        Result result = await CreateHandler().Handle(
            new UpdateFieldCommand(model.Id, systemField.Id, OrgId, "Bad", null, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("system field");
    }
}
