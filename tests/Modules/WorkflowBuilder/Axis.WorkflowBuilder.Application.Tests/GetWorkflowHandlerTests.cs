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
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowReferenceRepository _referenceRepo = Substitute.For<IWorkflowReferenceRepository>();
    private readonly GetWorkflowHandler _handler;

    public GetWorkflowHandlerTests() => _handler = new GetWorkflowHandler(_repo, _referenceRepo);

    [Fact]
    public async Task Handle_WhenWorkflowExists_ReturnsDetailDto()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", "desc", TeamAccountId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("Review", StepType.Form, new Dictionary<string, object?> { ["form_id"] = "abc" });
        _repo.GetByIdAsync(wf.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(wf);
        _referenceRepo.GetBrokenStepIdsAsync(wf.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
        _referenceRepo.GetBrokenModelIdsAsync(wf.Id, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());

        WorkflowDetailDto? dto = await _handler.Handle(new GetWorkflowQuery(wf.Id, TeamAccountId), CancellationToken.None);

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
        _repo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        WorkflowDetailDto? dto = await _handler.Handle(
            new GetWorkflowQuery(Guid.NewGuid(), TeamAccountId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherTeamAccount_ReturnsNull()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, TeamAccountId, "user");

        Guid otherTeamAccountId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        WorkflowDetailDto? dto = await _handler.Handle(
            new GetWorkflowQuery(wf.Id, otherTeamAccountId), CancellationToken.None);

        dto.Should().BeNull();
        await _repo.Received(1).GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>());
    }
}
