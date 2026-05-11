using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using FluentAssertions;
using FluentValidation;
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
    public async Task Happy_path_creates_form_and_returns_id()
    {
        _formRepo.NameExistsAsync("Employee Intake", OrgId).Returns(false);

        var result = await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", "New hire form", OrgId, UserId),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _formRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.FormDefinition>(f =>
                f.Name == "Employee Intake" && f.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_name_throws_validation_exception()
    {
        _formRepo.NameExistsAsync("Employee Intake", OrgId).Returns(true);

        var act = async () => await CreateHandler().Handle(
            new CreateFormCommand("Employee Intake", null, OrgId, UserId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*already exists*");
    }
}
