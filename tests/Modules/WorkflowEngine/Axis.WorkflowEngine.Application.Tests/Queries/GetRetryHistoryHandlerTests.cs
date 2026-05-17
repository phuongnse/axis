using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Queries.GetRetryHistory;
using Axis.WorkflowEngine.Application.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowEngine.Application.Tests.Queries;

public class GetRetryHistoryHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OriginalExecId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private GetRetryHistoryHandler CreateHandler() => new(_execRepo);

    private static ExecutionSummaryResponse BuildSummary(Guid retryOfId) => new(
        Guid.NewGuid(), WorkflowId, "Failed", "Manual", null, retryOfId, "error",
        DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task GetRetryHistory_WhenRetriesExist_ReturnsChronologicalList()
    {
        List<ExecutionSummaryResponse> retries =
        [
            BuildSummary(OriginalExecId),
            BuildSummary(OriginalExecId),
        ];
        _execRepo.GetRetriesAsync(OriginalExecId, OrgId).Returns(retries);

        IReadOnlyList<ExecutionSummaryResponse> result = await CreateHandler().Handle(
            new GetRetryHistoryQuery(OriginalExecId, OrgId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.RetryOfExecutionId.Should().Be(OriginalExecId));
    }

    [Fact]
    public async Task GetRetryHistory_WhenNoRetries_ReturnsEmptyList()
    {
        _execRepo.GetRetriesAsync(OriginalExecId, OrgId).Returns(new List<ExecutionSummaryResponse>());

        IReadOnlyList<ExecutionSummaryResponse> result = await CreateHandler().Handle(
            new GetRetryHistoryQuery(OriginalExecId, OrgId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
