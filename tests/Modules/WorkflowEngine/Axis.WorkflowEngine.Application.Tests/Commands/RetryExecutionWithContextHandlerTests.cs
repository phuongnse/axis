using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.RetryExecutionWithContext;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Commands;

public class RetryExecutionWithContextHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private RetryExecutionWithContextHandler CreateHandler() => new(_execRepo, _uow);

    private static WorkflowExecution MakeFailedExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(
            WorkflowId, TeamAccountId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        exec.Fail("original error");
        return exec;
    }

    private static IReadOnlyDictionary<string, object?> ModifiedContext =>
        new Dictionary<string, object?> { ["key"] = "fixed-value" };

    [Fact]
    public async Task RetryWithContext_WhenExecutionHasFailed_CreatesRetryWithModifiedContextAndReturnsId()
    {
        WorkflowExecution failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, TeamAccountId).Returns(failed);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionWithContextCommand(failed.Id, TeamAccountId, UserId, ModifiedContext),
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
    public async Task RetryWithContext_WhenExecutionNotFound_ReturnsNotFound()
    {
        _execRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionWithContextCommand(Guid.NewGuid(), TeamAccountId, UserId, ModifiedContext),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RetryWithContext_WhenExecutionIsNotFailed_ReturnsBusinessRuleFailure()
    {
        WorkflowExecution exec = WorkflowExecution.Create(
            WorkflowId, TeamAccountId, TriggerType.Manual, null, new Dictionary<string, object?>());
        exec.Start();
        _execRepo.GetByIdAsync(exec.Id, TeamAccountId).Returns(exec);

        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionWithContextCommand(exec.Id, TeamAccountId, UserId, ModifiedContext),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("failed");
    }

    [Fact]
    public async Task RetryWithContext_WhenExecutionBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        WorkflowExecution failed = MakeFailedExecution();
        _execRepo.GetByIdAsync(failed.Id, TeamAccountId).Returns(failed);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result<Guid> result = await CreateHandler().Handle(
            new RetryExecutionWithContextCommand(failed.Id, otherTeamAccountId, UserId, ModifiedContext),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
