using Axis.DataModeling.Application.Commands.UpdateDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class UpdateDataClassHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateDataClassHandler CreateHandler() => new(_dataClassRepo, _uow);

    [Fact]
    public async Task Handle_WhenNameIsUnique_UpdatesDataClass()
    {
        DataClass dataClass = DataClass.Create("Address", null, TenantId, "user-123");
        _dataClassRepo.GetByIdAsync(dataClass.Id, TenantId, Arg.Any<CancellationToken>()).Returns(dataClass);
        _dataClassRepo.NameExistsAsync("Billing Address", TenantId, dataClass.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        Result result = await CreateHandler().Handle(
            new UpdateDataClassCommand(dataClass.Id, TenantId, "Billing Address", "Postal destination"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dataClass.Name.Should().Be("Billing Address");
        dataClass.Description.Should().Be("Postal destination");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ReturnsConflict()
    {
        DataClass dataClass = DataClass.Create("Address", null, TenantId, "user-123");
        _dataClassRepo.GetByIdAsync(dataClass.Id, TenantId, Arg.Any<CancellationToken>()).Returns(dataClass);
        _dataClassRepo.NameExistsAsync("Billing Address", TenantId, dataClass.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        Result result = await CreateHandler().Handle(
            new UpdateDataClassCommand(dataClass.Id, TenantId, "Billing Address", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
