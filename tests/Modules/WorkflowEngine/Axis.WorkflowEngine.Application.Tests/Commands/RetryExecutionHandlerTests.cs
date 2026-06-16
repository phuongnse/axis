using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.RetryExecution;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Commands;

public class RetryExecutionHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private RetryExecutionHandler CreateHandler() => new(_execRepo, _uow);

    private static WorkflowExecution MakeFailedExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        exec.Fail("network timeout");
        return exec;
    }

    [Fact]
    public async Task RetryExecution_WhenExecutionHasFailed_CreatesRetryExecutionAndReturnsId()
    {
        WorkflowExecution failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, WorkspaceId).Returns(failed);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(failed.Id, WorkspaceId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<WorkflowExecution>(r =>
                r.RetryOfExecutionId == failed.Id &&
                r.Status == ExecutionStatus.Pending),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryExecution_WhenExecutionNotFound_ReturnsNotFound()
    {
        _execRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(Guid.NewGuid(), WorkspaceId, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RetryExecution_WhenExecutionIsNotFailed_ReturnsBusinessRuleFailure()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        _execRepo.GetByIdAsync(exec.Id, WorkspaceId).Returns(exec);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(exec.Id, WorkspaceId, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("failed");
    }

    [Fact]
    public async Task RetryExecution_WhenExecutionBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        WorkflowExecution failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, WorkspaceId).Returns(failed);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(failed.Id, otherWorkspaceId, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
