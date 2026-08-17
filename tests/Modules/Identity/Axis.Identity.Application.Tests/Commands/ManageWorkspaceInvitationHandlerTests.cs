using Axis.Audit.Contracts;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.ResendWorkspaceInvitation;
using Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ManageWorkspaceInvitationHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manage_WhenAuthorityIsMissing_PersistsRedactedDenialBeforeReturningForbidden(
        bool resend)
    {
        Fixture fixture = new(authorize: false);

        Result<WorkspaceInvitationLifecycleDto> result = resend
            ? await fixture.Resend()
            : await fixture.Revoke();

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationForbidden);
        AuditEventV1 audit = fixture.Audits.Should().ContainSingle().Subject;
        audit.Action.Should().Be(resend
            ? "workspace.invitation.resend_denied"
            : "workspace.invitation.revoke_denied");
        audit.Outcome.Should().Be("authority_denied");
        audit.WorkspaceId.Should().Be(fixture.Invitation.WorkspaceId);
        audit.Metadata.Should().NotContainKey("email");
        audit.Metadata.Should().NotContainKey("token");
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenRateLimited_PersistsRedactedOutcomeBeforeReturningRateLimit()
    {
        Fixture fixture = new();
        fixture.RateLimiter.AcquireResendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                ErrorCodes.RateLimited,
                "Rate limited.",
                IdentityProblemCodes.InvitationRateLimited));

        Result<WorkspaceInvitationLifecycleDto> result = await fixture.Resend();

        result.ErrorCode.Should().Be(ErrorCodes.RateLimited);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.Action == "workspace.invitation.resend_rejected"
            && audit.Outcome == "rate_limited");
        fixture.Invitation.CurrentToken.Generation.Should().Be(1);
    }

    [Fact]
    public async Task Manage_WhenRequiredDenialAuditFails_FailsClosed()
    {
        Fixture fixture = new(authorize: false);
        fixture.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("audit unavailable"));

        Result<WorkspaceInvitationLifecycleDto> result = await fixture.Revoke();

        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAuditUnavailable);
    }

    private sealed class Fixture
    {
        public Fixture(bool authorize = true)
        {
            DateTime now = DateTime.UtcNow;
            Invitation = WorkspaceInvitation.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ActorId,
                "recipient@example.com",
                WorkspaceMembershipRole.Member,
                now,
                now.AddDays(7),
                "token-hash",
                "delivery-envelope",
                "delivery-correlation");
            Invitations.GetByIdAsync(
                    Invitation.WorkspaceId,
                    Invitation.Id,
                    Arg.Any<CancellationToken>())
                .Returns(Invitation);
            if (authorize)
            {
                OrganizationMemberships.GetActiveAsync(
                        Invitation.OrganizationId,
                        ActorId,
                        Arg.Any<CancellationToken>())
                    .Returns(OrganizationMembership.Create(
                        Invitation.OrganizationId,
                        ActorId,
                        OrganizationMembershipRole.Administrator));
                WorkspaceMemberships.GetActiveAsync(
                        Invitation.WorkspaceId,
                        ActorId,
                        Arg.Any<CancellationToken>())
                    .Returns(WorkspaceMembership.CreateOrganizationMember(
                        Invitation.WorkspaceId,
                        ActorId,
                        WorkspaceMembershipRole.Administrator));
            }
            RateLimiter.AcquireResendAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success());
            Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Audits.Add(call.Arg<AuditEventV1>());
                    return Task.CompletedTask;
                });
        }

        private static readonly Guid ActorId = Guid.NewGuid();
        public IOrganizationMembershipRepository OrganizationMemberships { get; } =
            Substitute.For<IOrganizationMembershipRepository>();
        public IWorkspaceMembershipRepository WorkspaceMemberships { get; } =
            Substitute.For<IWorkspaceMembershipRepository>();
        public IWorkspaceInvitationRepository Invitations { get; } =
            Substitute.For<IWorkspaceInvitationRepository>();
        public IWorkspaceInvitationRateLimiter RateLimiter { get; } =
            Substitute.For<IWorkspaceInvitationRateLimiter>();
        public IIdentityAuditOutbox Audit { get; } = Substitute.For<IIdentityAuditOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public WorkspaceInvitation Invitation { get; }
        public List<AuditEventV1> Audits { get; } = [];

        public Task<Result<WorkspaceInvitationLifecycleDto>> Revoke() =>
            new RevokeWorkspaceInvitationHandler(
                OrganizationMemberships,
                WorkspaceMemberships,
                Invitations,
                Audit,
                TimeProvider.System,
                UnitOfWork).Handle(
                new RevokeWorkspaceInvitationCommand(
                    ActorId,
                    Invitation.WorkspaceId,
                    Invitation.Id,
                    Invitation.Revision,
                    "revoke-correlation",
                    "Workspace Admin"),
                CancellationToken.None);

        public Task<Result<WorkspaceInvitationLifecycleDto>> Resend() =>
            new ResendWorkspaceInvitationHandler(
                Substitute.For<IUserRepository>(),
                Substitute.For<IOrganizationRepository>(),
                OrganizationMemberships,
                Substitute.For<IWorkspaceRepository>(),
                WorkspaceMemberships,
                Invitations,
                RateLimiter,
                Substitute.For<IInvitationDeliveryEnvelopeProtector>(),
                Audit,
                new WorkspaceInvitationPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(2), 20, 100),
                TimeProvider.System,
                UnitOfWork).Handle(
                new ResendWorkspaceInvitationCommand(
                    ActorId,
                    Invitation.WorkspaceId,
                    Invitation.Id,
                    Invitation.Revision,
                    "resend-correlation",
                    "Workspace Admin"),
                CancellationToken.None);
    }
}
