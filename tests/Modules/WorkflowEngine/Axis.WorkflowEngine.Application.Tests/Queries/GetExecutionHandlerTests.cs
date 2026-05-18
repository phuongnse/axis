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

    private static readonly Guid OrgId = Guid.NewGuid();
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
        _execRepo.GetWithStepsAsync(ExecId, OrgId).Returns(expected);

        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(ExecId, OrgId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ExecId);
        result.WorkflowDefinitionId.Should().Be(WorkflowId);
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExecution_WhenExecutionNotFound_ReturnsNull()
    {
        _execRepo.GetWithStepsAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetExecution_WhenExecutionBelongsToAnotherOrg_ReturnsNull()
    {
        ExecutionResponse expected = BuildResponse();
        _execRepo.GetWithStepsAsync(ExecId, OrgId).Returns(expected);

        Guid otherOrgId = Guid.NewGuid();
        ExecutionResponse? result = await CreateHandler().Handle(
            new GetExecutionQuery(ExecId, otherOrgId), CancellationToken.None);

        result.Should().BeNull();
    }
}
