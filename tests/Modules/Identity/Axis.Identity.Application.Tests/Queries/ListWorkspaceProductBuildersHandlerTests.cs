using Axis.Identity.Application.Queries.ListWorkspaceProductBuilders;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListWorkspaceProductBuildersHandlerTests
{
    [Fact]
    public async Task ListProductBuilders_WhenActorIsAdministrator_ReturnsIndependentRoleAndAuthority()
    {
        Guid actorId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Organization organization = Organization.Create("Acme");
        Workspace workspace = Workspace.CreateOrganization(
            "Acme",
            WorkspaceSlug.Create($"acme-{Guid.NewGuid():N}").Value,
            organization.Id);
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        workspaces.GetByIdAsync(workspace.Id, Arg.Any<CancellationToken>()).Returns(workspace);
        memberships.GetActiveHumanAsync(workspace.Id, actorId, Arg.Any<CancellationToken>())
            .Returns(WorkspaceMembership.CreateOrganizationMember(
                workspace.Id,
                actorId,
                WorkspaceMembershipRole.Administrator));
        memberships.ListActiveForWorkspaceAsync(workspace.Id, Arg.Any<CancellationToken>())
            .Returns([
                new(actorId, "Administrator", "admin@example.com", WorkspaceMembershipRole.Administrator, false, 1),
                new(targetId, "Builder", "builder@example.com", WorkspaceMembershipRole.Member, true, 4),
            ]);

        Result<IReadOnlyList<WorkspaceProductBuilderDto>> result =
            await new ListWorkspaceProductBuildersHandler(workspaces, memberships).Handle(
                new ListWorkspaceProductBuildersQuery(actorId, workspace.Id),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainEquivalentOf(new WorkspaceProductBuilderDto(
            actorId,
            "Administrator",
            "admin@example.com",
            "Administrator",
            false,
            1,
            false,
            new ResourceMetadataDto(1, null, null, null, null)));
        result.Value.Should().ContainEquivalentOf(new WorkspaceProductBuilderDto(
            targetId,
            "Builder",
            "builder@example.com",
            "Member",
            true,
            4,
            true,
            new ResourceMetadataDto(4, null, null, null, null)));
    }
}
