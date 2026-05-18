using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.UpdateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class UpdateWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UpdateWorkflowHandler _handler;

    public UpdateWorkflowHandlerTests() => _handler = new UpdateWorkflowHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenNameIsUnique_UpdatesWorkflowAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);
        _repo.NameExistsAsync("New Name", OrgId, wf.Id, Arg.Any<CancellationToken>()).Returns(false);

        Result result = await _handler.Handle(
            new UpdateWorkflowCommand(wf.Id, OrgId, "New Name", "Updated desc"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Name.Should().Be("New Name");
        wf.Description.Should().Be("Updated desc");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new UpdateWorkflowCommand(Guid.NewGuid(), OrgId, "Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Old Name", null, OrgId, "user");

        Guid otherOrgId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new UpdateWorkflowCommand(wf.Id, otherOrgId, "New Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNameConflictsWithAnotherWorkflow_ReturnsConflict()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Old Name", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);
        _repo.NameExistsAsync("Taken Name", OrgId, wf.Id, Arg.Any<CancellationToken>()).Returns(true);

        Result result = await _handler.Handle(
            new UpdateWorkflowCommand(wf.Id, OrgId, "Taken Name", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
