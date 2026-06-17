using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class CreateFormHandlerTests
{
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateFormHandler CreateHandler() => new(_formRepo, _uow);

    [Fact]
    public async Task CreateForm_WhenNameIsUnique_CreatesFormAndReturnsId()
    {
        _formRepo.NameExistsAsync("Employee Intake", WorkspaceId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", "New hire form", WorkspaceId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _formRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.FormDefinition>(f =>
                f.Name == "Employee Intake" && f.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateForm_WhenNameIsDuplicate_ReturnsConflict()
    {
        _formRepo.NameExistsAsync("Employee Intake", WorkspaceId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", null, WorkspaceId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateForm_WhenSaveThrowsUniqueConstraint_ReturnsConflict()
    {
        _formRepo.NameExistsAsync("Concurrent Form", WorkspaceId).Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintException("unique violation"));

        Result<Guid> result = await CreateHandler().Handle(
            new CreateFormCommand("Concurrent Form", null, WorkspaceId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }
}
