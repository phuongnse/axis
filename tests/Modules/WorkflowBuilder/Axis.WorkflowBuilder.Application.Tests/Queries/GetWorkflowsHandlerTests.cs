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

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetWorkflowsHandler CreateHandler() => new(_workflowRepo);

    [Fact]
    public async Task Returns_all_workflows_for_org()
    {
        var workflows = new List<WorkflowDefinition>
        {
            WorkflowDefinition.Create("Invoice Approval", null, OrgId),
            WorkflowDefinition.Create("Onboarding", "New hire flow", OrgId),
        };
        _workflowRepo.GetAllAsync(OrgId).Returns(workflows);

        var result = await CreateHandler().Handle(new GetWorkflowsQuery(OrgId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Name == "Invoice Approval" && w.Status == WorkflowStatus.Draft);
    }

    [Fact]
    public async Task Empty_org_returns_empty_list()
    {
        _workflowRepo.GetAllAsync(OrgId).Returns(new List<WorkflowDefinition>());

        var result = await CreateHandler().Handle(new GetWorkflowsQuery(OrgId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
