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

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private RetryExecutionHandler CreateHandler() => new(_execRepo, _uow);

    private static WorkflowExecution MakeFailedExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        exec.Fail("network timeout");
        return exec;
    }

    [Fact]
    public async Task Happy_path_creates_retry_execution_and_returns_id()
    {
        WorkflowExecution failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, OrgId).Returns(failed);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(failed.Id, OrgId, UserId),
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
    public async Task Execution_not_found_returns_not_found()
    {
        _execRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(Guid.NewGuid(), OrgId, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Retrying_non_failed_execution_returns_business_rule_failure()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        _execRepo.GetByIdAsync(exec.Id, OrgId).Returns(exec);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionCommand(exec.Id, OrgId, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("failed");
    }
}
