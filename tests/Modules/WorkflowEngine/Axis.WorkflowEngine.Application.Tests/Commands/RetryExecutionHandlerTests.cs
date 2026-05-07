using Axis.WorkflowEngine.Application.Commands.RetryExecution;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using FluentValidation;
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
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        exec.Fail("network timeout");
        return exec;
    }

    [Fact]
    public async Task Happy_path_creates_retry_execution_and_returns_id()
    {
        var failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, OrgId).Returns(failed);

        var result = await CreateHandler().Handle(
            new RetryExecutionCommand(failed.Id, OrgId, UserId),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<WorkflowExecution>(r =>
                r.RetryOfExecutionId == failed.Id &&
                r.Status == ExecutionStatus.Pending),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execution_not_found_throws_validation_exception()
    {
        _execRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new RetryExecutionCommand(Guid.NewGuid(), OrgId, UserId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task Retrying_non_failed_execution_throws_validation_exception()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        _execRepo.GetByIdAsync(exec.Id, OrgId).Returns(exec);

        var act = async () => await CreateHandler().Handle(
            new RetryExecutionCommand(exec.Id, OrgId, UserId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*failed*");
    }
}
