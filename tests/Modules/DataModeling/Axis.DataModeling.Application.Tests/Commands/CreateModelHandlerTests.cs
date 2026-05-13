using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.DataModeling.Application.Tests.Commands;

public class CreateModelHandlerTests
{
    private readonly IDataModelRepository _modelRepo = Substitute.For<IDataModelRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateModelHandler CreateHandler() => new(_modelRepo, _uow);

    [Fact]
    public async Task Happy_path_creates_model_and_returns_id()
    {
        _modelRepo.NameExistsAsync("Invoice", OrgId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateModelCommand("Invoice", "Invoicing model", null, null, OrgId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _modelRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.DataModel>(m =>
                m.Name == "Invoice" && m.OrganizationId == OrgId && m.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_name_returns_conflict()
    {
        _modelRepo.NameExistsAsync("Invoice", OrgId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateModelCommand("Invoice", null, null, null, OrgId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
