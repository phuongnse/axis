using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Queries.GetWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class GetWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly GetWorkflowHandler _handler;

    public GetWorkflowHandlerTests() => _handler = new GetWorkflowHandler(_repo);

    [Fact]
    public async Task Handle_WhenWorkflowExists_ReturnsDetailDto()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", "desc", OrgId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("Review", StepType.Form, new Dictionary<string, object?> { ["form_id"] = "abc" });
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        WorkflowDetailDto? dto = await _handler.Handle(new GetWorkflowQuery(wf.Id, OrgId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(wf.Id);
        dto.Name.Should().Be("Invoice Approval");
        dto.Description.Should().Be("desc");
        dto.Status.Should().Be(WorkflowStatus.Draft);
        dto.Steps.Should().HaveCount(3); // Start + Review + End
        dto.Triggers.Should().HaveCount(1);
        dto.Steps.Should().Contain(s => s.Name == "Review" && s.Type == StepType.Form);
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNull()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        WorkflowDetailDto? dto = await _handler.Handle(
            new GetWorkflowQuery(Guid.NewGuid(), OrgId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNull()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, "user");

        Guid otherOrgId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        WorkflowDetailDto? dto = await _handler.Handle(
            new GetWorkflowQuery(wf.Id, otherOrgId), CancellationToken.None);

        dto.Should().BeNull();
        await _repo.Received(1).GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>());
    }
}
