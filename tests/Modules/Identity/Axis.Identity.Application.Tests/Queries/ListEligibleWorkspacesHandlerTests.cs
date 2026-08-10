using Axis.Identity.Application.Queries.ListEligibleWorkspaces;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListEligibleWorkspacesHandlerTests
{
    [Fact]
    public async Task Handle_WhenProjectionContainsCurrentWorkspace_MapsCurrentWithoutAddingAuthority()
    {
        Guid userId = Guid.NewGuid();
        Guid currentWorkspaceId = Guid.NewGuid();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        memberships.ListEligibleWorkspacesAsync(userId, Arg.Any<CancellationToken>()).Returns([
            new EligibleWorkspaceProjection(
                currentWorkspaceId,
                "Personal",
                WorkspaceSlug.Create("personal").Value,
                WorkspaceType.Personal,
                null),
            new EligibleWorkspaceProjection(
                Guid.NewGuid(),
                "Acme",
                WorkspaceSlug.Create("acme").Value,
                WorkspaceType.Organization,
                Guid.NewGuid()),
        ]);

        IReadOnlyList<EligibleWorkspaceDto> result = await new ListEligibleWorkspacesHandler(memberships)
            .Handle(
                new ListEligibleWorkspacesQuery(userId, currentWorkspaceId),
                TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(workspace => workspace.IsCurrent && workspace.WorkspaceId == currentWorkspaceId);
    }
}
