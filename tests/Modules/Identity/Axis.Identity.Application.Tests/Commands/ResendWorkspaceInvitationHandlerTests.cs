using Axis.Identity.Application.Commands.ResendWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ResendWorkspaceInvitationHandlerTests
{
    [Fact]
    public async Task Handle_WhenInvitationIsMissing_ReturnsNotFoundWithoutMutation()
    {
        IWorkspaceInvitationRepository invitations = Substitute.For<IWorkspaceInvitationRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        ResendWorkspaceInvitationHandler handler = new(
            Substitute.For<IUserRepository>(),
            Substitute.For<IOrganizationRepository>(),
            Substitute.For<IOrganizationMembershipRepository>(),
            Substitute.For<IWorkspaceRepository>(),
            Substitute.For<IWorkspaceMembershipRepository>(),
            invitations,
            Substitute.For<IWorkspaceInvitationRateLimiter>(),
            Substitute.For<IInvitationDeliveryEnvelopeProtector>(),
            Substitute.For<IIdentityAuditOutbox>(),
            new WorkspaceInvitationPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(2), 20, 100),
            TimeProvider.System,
            unitOfWork);

        Result<WorkspaceInvitationLifecycleDto> result = await handler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "correlation"),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
