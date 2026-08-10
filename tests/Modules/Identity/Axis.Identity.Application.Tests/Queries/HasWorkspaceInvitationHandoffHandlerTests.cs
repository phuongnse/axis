using Axis.Identity.Application.Queries.HasWorkspaceInvitationHandoff;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class HasWorkspaceInvitationHandoffHandlerTests
{
    [Fact]
    public async Task Handle_WhenActiveHandoffExists_ReturnsTrue()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "recipient@example.com",
            WorkspaceMembershipRole.Member, now, now.AddDays(7), "token", "envelope", "delivery");
        invitation.Exchange("token", "handoff", now.AddHours(1), now, invitation.Revision);
        IWorkspaceInvitationRepository invitations = Substitute.For<IWorkspaceInvitationRepository>();
        invitations.GetByHandoffHashAsync("handoff", Arg.Any<CancellationToken>()).Returns(invitation);
        HasWorkspaceInvitationHandoffHandler handler = new(invitations, TimeProvider.System);

        bool result = await handler.Handle(new("handoff"), CancellationToken.None);

        result.Should().BeTrue();
    }
}
