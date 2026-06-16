using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Queries.GetAllExecutions;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowEngine.Application.Tests.Queries;

public class GetAllExecutionsHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private GetAllExecutionsHandler CreateHandler() => new(_execRepo);

    private static ExecutionSummaryResponse BuildSummary() => new(
        Guid.NewGuid(), WorkflowId, "Completed", "Manual", null, null, null,
        DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);

    [Fact]
    public async Task GetAllExecutions_WhenExecutionsExist_ReturnsPagedResult()
    {
        List<ExecutionSummaryResponse> summaries = [BuildSummary(), BuildSummary(), BuildSummary()];
        _execRepo.GetPagedAsync(TenantId, 1, 25, null, Arg.Any<CancellationToken>())
            .Returns((summaries, 3));

        PagedResult<ExecutionSummaryResponse> result = await CreateHandler().Handle(
            new GetAllExecutionsQuery(TenantId), CancellationToken.None);

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetAllExecutions_WithStatusFilter_PassesFilterToRepository()
    {
        _execRepo.GetPagedAsync(TenantId, 1, 25, ExecutionStatus.Running, Arg.Any<CancellationToken>())
            .Returns((new List<ExecutionSummaryResponse>(), 0));

        await CreateHandler().Handle(
            new GetAllExecutionsQuery(TenantId, Status: ExecutionStatus.Running),
            CancellationToken.None);

        await _execRepo.Received(1).GetPagedAsync(
            TenantId, 1, 25, ExecutionStatus.Running, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllExecutions_WhenPageIsZeroOrNegative_ClampsToOne()
    {
        _execRepo.GetPagedAsync(TenantId, 1, 25, null, Arg.Any<CancellationToken>())
            .Returns((new List<ExecutionSummaryResponse>(), 0));

        await CreateHandler().Handle(
            new GetAllExecutionsQuery(TenantId, Page: -5), CancellationToken.None);

        await _execRepo.Received(1).GetPagedAsync(TenantId, 1, 25, null, Arg.Any<CancellationToken>());
    }
}
