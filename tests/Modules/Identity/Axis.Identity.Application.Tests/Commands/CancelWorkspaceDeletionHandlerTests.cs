using Axis.Identity.Application.Commands.CancelWorkspaceDeletion;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CancelWorkspaceDeletionHandlerTests
{
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public async Task CancelWorkspaceDeletion_WhenScheduled_RestoresActive()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        Workspace.ScheduleDeletion(DateTime.UtcNow);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await new CancelWorkspaceDeletionHandler(_workspaceRepo, _uow).Handle(
            new CancelWorkspaceDeletionCommand(WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Workspace.Status.Should().Be(WorkspaceStatus.Active);
    }
}
