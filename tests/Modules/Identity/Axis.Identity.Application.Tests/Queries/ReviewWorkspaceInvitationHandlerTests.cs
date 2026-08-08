using Axis.Identity.Application.Queries.ReviewWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ReviewWorkspaceInvitationHandlerTests
{
    [Fact]
    public async Task Handle_WhenHandoffOrUserIsMissing_RejectsBeforeRepositoryReads()
    {
        IWorkspaceInvitationRepository invitations = Substitute.For<IWorkspaceInvitationRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        ReviewWorkspaceInvitationHandler handler = new(
            Substitute.For<IUserRepository>(),
            Substitute.For<IOrganizationRepository>(),
            Substitute.For<IWorkspaceRepository>(),
            invitations,
            Substitute.For<IIdentityAuditOutbox>(),
            TimeProvider.System,
            unitOfWork);

        Result<WorkspaceInvitationReviewDto> result = await handler.Handle(
            new(string.Empty, Guid.Empty, "correlation"),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        await invitations.DidNotReceive().GetByHandoffHashAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
