using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class CreateFormHandlerTests
{
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateFormHandler CreateHandler() => new(_formRepo, _uow);

    [Fact]
    public async Task CreateForm_WhenNameIsUnique_CreatesFormAndReturnsId()
    {
        _formRepo.NameExistsAsync("Employee Intake", OrgId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", "New hire form", OrgId, UserId),
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
        _formRepo.NameExistsAsync("Employee Intake", OrgId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", null, OrgId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
