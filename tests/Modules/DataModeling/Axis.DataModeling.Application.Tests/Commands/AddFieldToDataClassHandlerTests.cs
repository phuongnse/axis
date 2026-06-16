using Axis.DataModeling.Application.Commands.AddFieldToDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class AddFieldToDataClassHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private AddFieldToDataClassHandler CreateHandler() => new(_dataClassRepo, _uow);

    [Fact]
    public async Task Handle_WhenDataClassExists_AddsFieldAndReturnsId()
    {
        DataClass dataClass = DataClass.Create("Address", null, TenantId, "user-123");
        _dataClassRepo.GetByIdAsync(dataClass.Id, TenantId, Arg.Any<CancellationToken>()).Returns(dataClass);

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldToDataClassCommand(
                dataClass.Id,
                TenantId,
                "street",
                "Street",
                FieldType.Text,
                true,
                new TextFieldConfig()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        dataClass.Fields.Should().Contain(f => f.Id == result.Value && f.Name == "street" && f.IsRequired);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenDataClassDoesNotExist_ReturnsNotFound()
    {
        _dataClassRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId, Arg.Any<CancellationToken>())
            .Returns((DataClass?)null);

        Result<Guid> result = await CreateHandler().Handle(
            new AddFieldToDataClassCommand(
                Guid.NewGuid(),
                TenantId,
                "street",
                "Street",
                FieldType.Text,
                true,
                new TextFieldConfig()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
