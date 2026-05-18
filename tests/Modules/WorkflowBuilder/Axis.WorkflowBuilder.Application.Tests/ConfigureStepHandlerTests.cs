using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.ConfigureStep;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class ConfigureStepHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ConfigureStepHandler _handler;

    public ConfigureStepHandlerTests() => _handler = new ConfigureStepHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenStepExists_UpdatesConfigAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        WorkflowStep step = wf.AddStep("Form Step", StepType.Form, null);
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);
        Dictionary<string, object?> config = new() { ["form_id"] = Guid.NewGuid() };

        Result result = await _handler.Handle(
            new ConfigureStepCommand(wf.Id, OrgId, step.Id, "Updated Form", config), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Steps.Single(s => s.Id == step.Id).Name.Should().Be("Updated Form");
        wf.Steps.Single(s => s.Id == step.Id).Config.Should().BeEquivalentTo(config);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new ConfigureStepCommand(Guid.NewGuid(), OrgId, Guid.NewGuid(), "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        WorkflowStep step = wf.AddStep("Form Step", StepType.Form, null);

        Guid otherOrgId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new ConfigureStepCommand(wf.Id, otherOrgId, step.Id, "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStepNotFound_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new ConfigureStepCommand(wf.Id, OrgId, Guid.NewGuid(), "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
