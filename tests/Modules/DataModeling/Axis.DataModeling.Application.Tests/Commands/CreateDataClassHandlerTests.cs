using Axis.DataModeling.Application.Commands.CreateDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class CreateDataClassHandlerTests
{
    private readonly IDataClassRepository _dataClassRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateDataClassHandler CreateHandler() => new(_dataClassRepo, _uow);

    [Fact]
    public async Task Happy_path_creates_data_class_and_returns_id()
    {
        _dataClassRepo.NameExistsAsync("Address", OrgId).Returns(false);

        var result = await CreateHandler().Handle(
            new CreateDataClassCommand("Address", "Postal address", OrgId, UserId),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _dataClassRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.DataClass>(dc =>
                dc.Name == "Address" && dc.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_name_throws_validation_exception()
    {
        _dataClassRepo.NameExistsAsync("Address", OrgId).Returns(true);

        var act = async () => await CreateHandler().Handle(
            new CreateDataClassCommand("Address", null, OrgId, UserId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*already exists*");
    }
}
