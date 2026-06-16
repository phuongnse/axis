using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests.Commands;

public class CreateWorkflowHandlerTests
{
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    private CreateWorkflowHandler CreateHandler()
    {
        _planLimitService
            .EnsureWithinLimitAsync(Arg.Any<Guid>(), Arg.Any<PlanLimitResourceType>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return new(_planLimitService, _workflowRepo, _uow);
    }

    [Fact]
    public async Task CreateWorkflow_WhenNameIsUnique_CreatesDraftWorkflowAndReturnsId()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", TeamAccountId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", "Approves invoices", TeamAccountId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _workflowRepo.Received(1).AddAsync(
            Arg.Is<WorkflowDefinition>(w =>
                w.Name == "Invoice Approval" &&
                w.Status == WorkflowStatus.Draft &&
                w.CreatedBy == UserId),
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).EnsureWithinLimitAsync(
            TeamAccountId,
            PlanLimitResourceType.Workflows,
            1,
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).RecordUsageDeltaAsync(
            TeamAccountId,
            PlanLimitResourceType.Workflows,
            1,
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateWorkflow_WhenNameIsDuplicate_ReturnsConflict()
    {
        _workflowRepo.NameExistsAsync("Invoice Approval", TeamAccountId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new CreateWorkflowCommand("Invoice Approval", null, TeamAccountId, UserId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }
}
