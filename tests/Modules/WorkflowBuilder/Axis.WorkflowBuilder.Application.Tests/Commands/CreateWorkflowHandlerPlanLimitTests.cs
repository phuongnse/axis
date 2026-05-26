using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests.Commands;

public class CreateWorkflowHandlerPlanLimitTests
{
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task CreateWorkflow_WhenWorkflowLimitExceeded_ReturnsPlanLimitFailureWithoutSaving()
    {
        PlanLimitFailureDetails details = new(
            "workflows",
            3,
            3,
            "/pricing",
            "Your plan allows 3 workflows.");
        _planLimitService
            .EnsureWithinLimitAsync(OrgId, PlanLimitResourceType.Workflows, 1, Arg.Any<CancellationToken>())
            .Returns(Result.PlanLimitFailure(details));

        Result<Guid> result = await new CreateWorkflowHandler(_planLimitService, _workflowRepo, _uow).Handle(
            new CreateWorkflowCommand("New Flow", null, OrgId, "user"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.PlanLimit);
        result.PlanLimitDetails.Should().BeEquivalentTo(details);
        await _workflowRepo.DidNotReceive().AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
        await _planLimitService.DidNotReceive().RecordUsageDeltaAsync(
            Arg.Any<Guid>(), Arg.Any<PlanLimitResourceType>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
