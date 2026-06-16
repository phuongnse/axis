using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Queries.GetExecution;
using Axis.WorkflowEngine.Application.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Queries;

public class GetExecutionHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid ExecId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private GetExecutionHandler CreateHandler() => new(_execRepo);

    private static ExecutionResponse BuildResponse() => new(
        ExecId, WorkflowId, "Running", "Manual", null, null, null,
        new Dictionary<string, object?>(), DateTime.UtcNow, DateTime.UtcNow, null,
        Array.Empty<ExecutionStepResponse>());

    [Fact]
    public async Task GetExecution_WhenExecutionExists_ReturnsExecutionResponse()
    {
        ExecutionResponse expected = BuildResponse();
        _execRepo.GetWithStepsAsync(ExecId, TeamAccountId).Returns(expected);

        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(ExecId, TeamAccountId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ExecId);
        result.WorkflowDefinitionId.Should().Be(WorkflowId);
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExecution_WhenExecutionNotFound_ReturnsNull()
    {
        _execRepo.GetWithStepsAsync(Arg.Any<Guid>(), TeamAccountId).ReturnsNull();

        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(Guid.NewGuid(), TeamAccountId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetExecution_WhenExecutionBelongsToAnotherTeamAccount_ReturnsNull()
    {
        ExecutionResponse expected = BuildResponse();
        _execRepo.GetWithStepsAsync(ExecId, TeamAccountId).Returns(expected);

        Guid otherTeamAccountId = Guid.NewGuid();
        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(ExecId, otherTeamAccountId), CancellationToken.None);

        result.Should().BeNull();
    }
}
