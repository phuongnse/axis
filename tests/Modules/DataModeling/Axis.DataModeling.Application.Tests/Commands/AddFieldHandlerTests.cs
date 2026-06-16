using Axis.DataModeling.Application.Commands.AddField;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.DataModeling.Application.Tests.Commands;

public class AddFieldHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private AddFieldHandler CreateHandler() => new(_modelRepo, _uow);

    private static DataModel MakeModel() => DataModel.Create("Invoice", null, null, null, TenantId, UserId);

    [Fact]
    public async Task AddField_WhenModelExists_AddsTextField()
    {
        DataModel model = MakeModel();
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldCommand(model.Id, TenantId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        model.Fields.Should().Contain(f => f.Name == "amount");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddField_WhenModelNotFound_ReturnsNotFound()
    {
        _modelRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldCommand(Guid.NewGuid(), TenantId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AddField_WhenModelBelongsToAnotherTenant_ReturnsNotFound()
    {
        DataModel model = MakeModel();
        _modelRepo.GetByIdAsync(model.Id, TenantId).Returns(model);

        Guid otherTenantId = Guid.NewGuid();
        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldCommand(model.Id, otherTenantId, "amount", "Amount", FieldType.Text, false, new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
