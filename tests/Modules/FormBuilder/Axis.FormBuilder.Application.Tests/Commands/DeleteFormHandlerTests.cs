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
    private readonly IFormDeletionGuard _formDeletionGuard = Substitute.For<IFormDeletionGuard>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private DeleteFormHandler CreateHandler() => new(_formRepo, _formDeletionGuard, _uow);

    [Fact]
    public async Task DeleteForm_WhenFormNotReferenced_SoftDeletesForm()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formDeletionGuard.ValidateCanDeleteAsync(form.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteForm_WhenReferencedByActiveWorkflow_ReturnsBusinessRuleFailure()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);
        _formDeletionGuard.ValidateCanDeleteAsync(form.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorCodes.BusinessRule,
                "This form is referenced by one or more active workflow steps. Remove those references before deleting."));

        Result result = await CreateHandler().Handle(new DeleteFormCommand(form.Id, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("workflow");
    }

    [Fact]
    public async Task DeleteForm_WhenFormNotFound_ReturnsNotFound()
    {
        _formRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeleteFormCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteForm_WhenFormBelongsToAnotherOrg_ReturnsNotFound()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        _formRepo.GetByIdAsync(form.Id, OrgId).Returns(form);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeleteFormCommand(form.Id, otherOrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _formRepo.Received(1).GetByIdAsync(form.Id, otherOrgId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
