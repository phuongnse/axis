using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests.Commands;

public class CreateWorkflowHandlerTests
{
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateWorkflowHandler CreateHandler() => new(_workflowRepo, _uow);

    [Fact]
    public async Task CreateWorkflow_WhenNameIsUnique_CreatesDraftWorkflowAndReturnsId()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", OrgId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", "Approves invoices", OrgId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _workflowRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.WorkflowDefinition>(w =>
                w.Name == "Invoice Approval" &&
                w.Status == WorkflowStatus.Draft &&
                w.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateWorkflow_WhenNameIsDuplicate_ReturnsConflict()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", OrgId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", null, OrgId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
