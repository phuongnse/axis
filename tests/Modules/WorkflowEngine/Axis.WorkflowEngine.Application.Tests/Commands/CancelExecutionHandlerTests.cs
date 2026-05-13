using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.CancelExecution;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Commands;

public class CancelExecutionHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private CancelExecutionHandler CreateHandler() => new(_execRepo, _uow);

    private static WorkflowExecution MakeRunningExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        return exec;
    }

    [Fact]
    public async Task Happy_path_cancels_a_running_execution()
    {
        WorkflowExecution exec = MakeRunningExecution();
        _execRepo.GetByIdAsync(exec.Id, OrgId).Returns(exec);

        Result result = await CreateHandler().Handle(new CancelExecutionCommand(exec.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        exec.Status.Should().Be(ExecutionStatus.Cancelled);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execution_not_found_returns_not_found()
    {
        _execRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new CancelExecutionCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Cancelling_a_completed_execution_returns_business_rule_failure()
    {
        WorkflowExecution exec = MakeRunningExecution();
        exec.Complete();
        _execRepo.GetByIdAsync(exec.Id, OrgId).Returns(exec);

        Result result = await CreateHandler().Handle(
            new CancelExecutionCommand(exec.Id, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cancel");
    }
}
