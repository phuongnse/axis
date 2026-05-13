using Axis.FormBuilder.Application.Commands.DeleteForm;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class DeleteFormHandlerTests
{
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteFormHandler CreateHandler() => new(_formRepo, _uow);

    [Fact]
    public async Task Happy_path_soft_deletes_form()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formRepo.IsReferencedByWorkflowAsync(form.Id).Returns(false);

        Result result = await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Form_referenced_by_active_workflow_returns_business_rule_failure()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formRepo.IsReferencedByWorkflowAsync(form.Id).Returns(true);

        Result result = await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("workflow");
    }

    [Fact]
    public async Task Form_not_found_returns_not_found()
    {
        _formRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeleteFormCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }
}
