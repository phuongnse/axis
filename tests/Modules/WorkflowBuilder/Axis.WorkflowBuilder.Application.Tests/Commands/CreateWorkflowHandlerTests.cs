using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests.Commands;

public class CreateWorkflowHandlerTests
{
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private CreateWorkflowHandler CreateHandler() => new(_workflowRepo, _uow);

    [Fact]
    public async Task Happy_path_creates_draft_workflow_and_returns_id()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", OrgId).Returns(false);

        var result = await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", "Approves invoices", OrgId),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _workflowRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.WorkflowDefinition>(w =>
                w.Name == "Invoice Approval" &&
                w.Status == WorkflowStatus.Draft),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_name_throws_validation_exception()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", OrgId).Returns(true);

        var act = async () => await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", null, OrgId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*already exists*");
    }
}
