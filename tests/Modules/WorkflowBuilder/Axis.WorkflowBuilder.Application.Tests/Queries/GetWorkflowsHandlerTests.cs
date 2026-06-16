using Axis.Shared.Application;
using Axis.WorkflowBuilder.Application.Queries.GetWorkflows;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests.Queries;

public class GetWorkflowsHandlerTests
{
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    private GetWorkflowsHandler CreateHandler() => new(_workflowRepo);

    [Fact]
    public async Task GetWorkflows_WithExistingWorkflows_ReturnsPagedDtos()
    {
        List<WorkflowDefinition> workflows =
        [
            WorkflowDefinition.Create("Invoice Approval", null, TenantId, UserId),
            WorkflowDefinition.Create("Onboarding", "New hire flow", TenantId, UserId),
        ];
        _workflowRepo.GetPagedAsync(TenantId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((workflows, 2));

        PagedResult<WorkflowSummaryDto> result = await CreateHandler()
            .Handle(new GetWorkflowsQuery(TenantId, 1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(w => w.Name == "Invoice Approval" && w.Status == WorkflowStatus.Draft);
    }

    [Fact]
    public async Task GetWorkflows_EmptyTenant_ReturnsEmptyPage()
    {
        _workflowRepo.GetPagedAsync(TenantId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<WorkflowDefinition>(), 0));

        PagedResult<WorkflowSummaryDto> result = await CreateHandler()
            .Handle(new GetWorkflowsQuery(TenantId, 1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetWorkflows_PageSizeExceedsCap_ClampsTo100()
    {
        _workflowRepo.GetPagedAsync(TenantId, 1, 100, Arg.Any<CancellationToken>())
            .Returns((new List<WorkflowDefinition>(), 0));

        await CreateHandler().Handle(new GetWorkflowsQuery(TenantId, 1, 999), CancellationToken.None);

        await _workflowRepo.Received(1)
            .GetPagedAsync(TenantId, 1, 100, Arg.Any<CancellationToken>());
    }
}
