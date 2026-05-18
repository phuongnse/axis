using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Queries.GetExecutionsByWorkflow;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowEngine.Application.Tests.Queries;

public class GetExecutionsByWorkflowHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private GetExecutionsByWorkflowHandler CreateHandler() => new(_execRepo);

    private static ExecutionSummaryResponse BuildSummary(Guid id) => new(
        id, WorkflowId, "Completed", "Manual", null, null, null,
        DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);

    [Fact]
    public async Task GetExecutionsByWorkflow_WhenExecutionsExist_ReturnsPagedResult()
    {
        List<ExecutionSummaryResponse> summaries = [BuildSummary(Guid.NewGuid()), BuildSummary(Guid.NewGuid())];
        _execRepo.GetPagedByWorkflowAsync(WorkflowId, OrgId, 1, 25, null, Arg.Any<CancellationToken>())
            .Returns((summaries, 2));

        PagedResult<ExecutionSummaryResponse> result = await CreateHandler().Handle(
            new GetExecutionsByWorkflowQuery(WorkflowId, OrgId), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task GetExecutionsByWorkflow_WithStatusFilter_PassesFilterToRepository()
    {
        _execRepo.GetPagedByWorkflowAsync(WorkflowId, OrgId, 1, 25, ExecutionStatus.Failed, Arg.Any<CancellationToken>())
            .Returns((new List<ExecutionSummaryResponse>(), 0));

        await CreateHandler().Handle(
            new GetExecutionsByWorkflowQuery(WorkflowId, OrgId, Status: ExecutionStatus.Failed),
            CancellationToken.None);

        await _execRepo.Received(1).GetPagedByWorkflowAsync(
            WorkflowId, OrgId, 1, 25, ExecutionStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExecutionsByWorkflow_WhenPageSizeExceedsMax_ClampsToHundred()
    {
        _execRepo.GetPagedByWorkflowAsync(WorkflowId, OrgId, 1, 100, null, Arg.Any<CancellationToken>())
            .Returns((new List<ExecutionSummaryResponse>(), 0));

        await CreateHandler().Handle(
            new GetExecutionsByWorkflowQuery(WorkflowId, OrgId, PageSize: 9999),
            CancellationToken.None);

        await _execRepo.Received(1).GetPagedByWorkflowAsync(
            WorkflowId, OrgId, 1, 100, null, Arg.Any<CancellationToken>());
    }
}
