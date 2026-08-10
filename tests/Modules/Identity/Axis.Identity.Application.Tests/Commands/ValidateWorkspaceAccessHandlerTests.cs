using Axis.Identity.Application.Commands.ValidateWorkspaceAccess;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ValidateWorkspaceAccessHandlerTests
{
    [Fact]
    public async Task Handle_WhenActiveWorkspaceMembershipExists_AllowsOnlyThatWorkspace()
    {
        Guid userId = Guid.NewGuid();
        Workspace workspace = Workspace.CreateOrganization(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
            Guid.NewGuid());
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationMember(
            workspace.Id,
            userId,
            WorkspaceMembershipRole.Member);
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        workspaces.GetByIdAsync(workspace.Id, Arg.Any<CancellationToken>()).Returns(workspace);
        memberships.GetActiveAsync(workspace.Id, userId, Arg.Any<CancellationToken>()).Returns(membership);

        Result<WorkspaceAccessDto> result = await new ValidateWorkspaceAccessHandler(workspaces, memberships).Handle(
            new ValidateWorkspaceAccessCommand(userId, workspace.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceId.Should().Be(workspace.Id);
    }
}
