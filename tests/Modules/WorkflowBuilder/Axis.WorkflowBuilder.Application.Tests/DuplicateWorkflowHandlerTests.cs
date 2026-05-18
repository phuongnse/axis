using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class DuplicateWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly DuplicateWorkflowHandler _handler;

    public DuplicateWorkflowHandlerTests() => _handler = new DuplicateWorkflowHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenNameIsAvailable_CreatesDraftCopyAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);
        _repo.NameExistsAsync("Copy of Invoice Approval", OrgId, null, Arg.Any<CancellationToken>()).Returns(false);

        Result<Guid> result = await _handler.Handle(
            new DuplicateWorkflowCommand(wf.Id, OrgId, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(wf.Id);
        await _repo.Received(1).AddAsync(Arg.Is<WorkflowDefinition>(w => w.Name == "Copy of Invoice Approval"), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCopyNameTaken_AppendsSuffixUntilUnique()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);
        _repo.NameExistsAsync("Copy of Invoice Approval", OrgId, null, Arg.Any<CancellationToken>()).Returns(true);
        _repo.NameExistsAsync("Copy of Invoice Approval (2)", OrgId, null, Arg.Any<CancellationToken>()).Returns(false);

        Result<Guid> result = await _handler.Handle(
            new DuplicateWorkflowCommand(wf.Id, OrgId, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<WorkflowDefinition>(w => w.Name == "Copy of Invoice Approval (2)"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result<Guid> result = await _handler.Handle(
            new DuplicateWorkflowCommand(Guid.NewGuid(), OrgId, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
