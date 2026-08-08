using Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class RevokeWorkspaceInvitationHandlerTests
{
    [Fact]
    public async Task Handle_WhenInvitationIsMissing_ReturnsNotFoundWithoutMutation()
    {
        IWorkspaceInvitationRepository invitations = Substitute.For<IWorkspaceInvitationRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        RevokeWorkspaceInvitationHandler handler = new(
            Substitute.For<IOrganizationMembershipRepository>(),
            Substitute.For<IWorkspaceMembershipRepository>(),
            invitations,
            Substitute.For<IIdentityAuditOutbox>(),
            TimeProvider.System,
            unitOfWork);

        Result<WorkspaceInvitationLifecycleDto> result = await handler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "correlation"),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
