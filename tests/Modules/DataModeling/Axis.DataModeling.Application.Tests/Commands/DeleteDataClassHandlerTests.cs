using Axis.DataModeling.Application.Commands.DeleteDataClass;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class DeleteDataClassHandlerTests
{
    private readonly IDataClassRepository _dcRepo = Substitute.For<IDataClassRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteDataClassHandler CreateHandler() => new(_dcRepo, _uow);

    [Fact]
    public async Task Happy_path_deletes_and_saves()
    {
        DataClass dc = DataClass.Create("Address", null, OrgId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, OrgId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(false);

        await CreateHandler().Handle(new DeleteDataClassCommand(dc.Id, OrgId), CancellationToken.None);

        dc.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Referenced_data_class_throws()
    {
        DataClass dc = DataClass.Create("Address", null, OrgId, UserId);
        _dcRepo.GetByIdAsync(dc.Id, OrgId).Returns(dc);
        _dcRepo.IsReferencedByAnyModelAsync(dc.Id).Returns(true);

        var act = async () => await CreateHandler().Handle(
            new DeleteDataClassCommand(dc.Id, OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*referenced*");
    }
}
