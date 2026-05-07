using Axis.FormBuilder.Application.Commands.DeleteForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class DeleteFormHandlerTests
{
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private DeleteFormHandler CreateHandler() => new(_formRepo, _uow);

    [Fact]
    public async Task Happy_path_soft_deletes_form()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formRepo.IsReferencedByWorkflowAsync(form.Id).Returns(false);

        await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        form.IsDeleted.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Form_referenced_by_active_workflow_throws_validation_exception()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formRepo.IsReferencedByWorkflowAsync(form.Id).Returns(true);

        var act = async () => await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*workflow*");
    }

    [Fact]
    public async Task Form_not_found_throws_validation_exception()
    {
        _formRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new DeleteFormCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }
}
