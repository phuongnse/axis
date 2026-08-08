using Axis.Identity.Application.Queries.ListWorkspaceInvitations;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListWorkspaceInvitationsHandlerTests
{
    [Fact]
    public async Task Handle_WhenPageIsInvalid_RejectsBeforeRepositoryReads()
    {
        IWorkspaceRepository workspaces = Substitute.For<IWorkspaceRepository>();
        ListWorkspaceInvitationsHandler handler = new(
            workspaces,
            Substitute.For<IOrganizationMembershipRepository>(),
            Substitute.For<IWorkspaceMembershipRepository>(),
            Substitute.For<IWorkspaceInvitationRepository>(),
            new WorkspaceInvitationPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(2), 20, 100));

        Result<WorkspaceInvitationPageDto> result = await handler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), 0, 20),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        await workspaces.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
