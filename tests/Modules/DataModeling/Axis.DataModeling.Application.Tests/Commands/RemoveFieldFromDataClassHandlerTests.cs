using Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;
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

public class RemoveFieldFromDataClassHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RemoveFieldFromDataClassHandler CreateHandler() => new(_dataClassRepo, _uow);

    [Fact]
    public async Task Handle_WhenFieldExists_RemovesFieldAndSaves()
    {
        DataClass dataClass = DataClass.Create("Address", null, WorkspaceId, "user-123");
        FieldDefinition field = dataClass.AddField(
            "street",
            "Street",
            FieldType.Text,
            true,
            new TextFieldConfig());
        _dataClassRepo.GetByIdAsync(dataClass.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(dataClass);

        Result result = await CreateHandler().Handle(
            new RemoveFieldFromDataClassCommand(dataClass.Id, field.Id, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dataClass.Fields.Should().NotContain(f => f.Id == field.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenDataClassDoesNotExist_ReturnsNotFound()
    {
        _dataClassRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns((DataClass?)null);

        Result result = await CreateHandler().Handle(
            new RemoveFieldFromDataClassCommand(Guid.NewGuid(), Guid.NewGuid(), WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
