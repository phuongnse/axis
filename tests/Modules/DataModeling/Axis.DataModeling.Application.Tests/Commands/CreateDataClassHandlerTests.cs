using Axis.DataModeling.Application.Commands.CreateDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class CreateDataClassHandlerTests
{
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateDataClassHandler CreateHandler() => new(_dataClassRepo, _uow);

    [Fact]
    public async Task CreateDataClass_WhenNameIsUnique_CreatesDataClassAndReturnsId()
    {
        _dataClassRepo.NameExistsAsync("Address", TenantId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateDataClassCommand("Address", "Postal address", TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _dataClassRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.DataClass>(dc =>
                dc.Name == "Address" && dc.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDataClass_WhenNameIsDuplicate_ReturnsConflict()
    {
        _dataClassRepo.NameExistsAsync("Address", TenantId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateDataClassCommand("Address", null, TenantId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
