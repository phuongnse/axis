using Axis.DataModeling.Application.Commands.ReorderFields;
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

public class ReorderFieldsHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private ReorderFieldsHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task ReorderFields_WhenValidOrderProvided_ReordersCustomFieldsAndSaves()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        FieldDefinition f1 = model.AddField("alpha", "Alpha", FieldType.Text, false, new TextFieldConfig());
        FieldDefinition f2 = model.AddField("beta", "Beta", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        // Reverse order: beta first, alpha second
        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(model.Id, TenantId, [f2.Id, f1.Id]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<FieldDefinition> customFields = model.Fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        customFields[0].Id.Should().Be(f2.Id);
        customFields[1].Id.Should().Be(f1.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReorderFields_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId).Returns((DataModel?)null);

        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(Guid.NewGuid(), TenantId, [Guid.NewGuid()]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ReorderFields_WhenModelBelongsToAnotherTenant_ReturnsNotFound()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        FieldDefinition f1 = model.AddField("alpha", "Alpha", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Guid otherTenantId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(model.Id, otherTenantId, [f1.Id]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ReorderFields_WhenIdsMismatch_ReturnsBusinessRuleFailure()
    {
        DataModel model = DataModel.Create("My Model", null, null, null, TenantId, UserId);
        model.AddField("alpha", "Alpha", FieldType.Text, false, new TextFieldConfig());
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result result = await CreateHandler().Handle(
            new ReorderFieldsCommand(model.Id, TenantId, [Guid.NewGuid()]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
