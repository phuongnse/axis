using Axis.Audit.Contracts;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.AcceptWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class AcceptWorkspaceInvitationHandlerTests
{
    [Fact]
    public async Task Accept_WhenRecipientHasNoMembership_EstablishesOnlyBaselineAndInvitedRoles()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Administrator);

        Result<WorkspaceInvitationAcceptanceDto> result = await fixture.Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.OrganizationRole.Should().Be("Member");
        result.Value.WorkspaceRole.Should().Be("Administrator");
        fixture.Invitation.Status.Should().Be(WorkspaceInvitationStatus.Accepted);
        fixture.RecipientOrganizationMembership.Should().NotBeNull();
        fixture.RecipientOrganizationMembership!.Role.Should().Be(OrganizationMembershipRole.Member);
        fixture.RecipientWorkspaceMembership.Should().NotBeNull();
        fixture.RecipientWorkspaceMembership!.Role.Should().Be(WorkspaceMembershipRole.Administrator);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.Action == "workspace.invitation.accepted" && audit.Outcome == "succeeded");
        fixture.Audits.Single().Metadata.Should().NotContainKey("email");
        fixture.Audits.Single().Metadata.Should().NotContainKey("handoff");
    }

    [Fact]
    public async Task Accept_PreservesActiveOrganizationAuthority_AndRestoresRemovedWorkspaceRole()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Member);
        fixture.WithActiveOrganizationMembership(OrganizationMembershipRole.Administrator);
        fixture.WithRemovedWorkspaceMembership(WorkspaceMembershipRole.Administrator);

        Result<WorkspaceInvitationAcceptanceDto> result = await fixture.Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.OrganizationRole.Should().Be("Administrator");
        result.Value.WorkspaceRole.Should().Be("Member");
        fixture.RecipientOrganizationMembership!.Role.Should().Be(OrganizationMembershipRole.Administrator);
        fixture.RecipientWorkspaceMembership!.Status.Should().Be(MembershipStatus.Active);
        fixture.RecipientWorkspaceMembership.Role.Should().Be(WorkspaceMembershipRole.Member);
    }

    [Fact]
    public async Task Accept_WhenMembershipIsSuspended_DeniesWithoutConsumingInvitation()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Member);
        fixture.WithSuspendedOrganizationMembership();

        Result<WorkspaceInvitationAcceptanceDto> result = await fixture.Handle();

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationMembershipSuspended);
        fixture.Invitation.Status.Should().Be(WorkspaceInvitationStatus.Pending);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.Action == "workspace.invitation.accept_rejected");
    }

    [Fact]
    public async Task Accept_WhenActiveWorkspaceRoleDiffers_DeniesWithoutConsumingInvitation()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Administrator);
        fixture.WithActiveWorkspaceMembership(WorkspaceMembershipRole.Member);

        Result<WorkspaceInvitationAcceptanceDto> result = await fixture.Handle();

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationConflict);
        fixture.Invitation.Status.Should().Be(WorkspaceInvitationStatus.Pending);
        fixture.RecipientWorkspaceMembership!.Role.Should().Be(WorkspaceMembershipRole.Member);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.Action == "workspace.invitation.accept_rejected");
    }

    [Fact]
    public async Task Accept_WhenAuthenticatedEmailDiffers_DeniesWithoutDisclosingOrMutatingMembership()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Member, recipientEmail: "other@example.com");

        Result<WorkspaceInvitationAcceptanceDto> result = await fixture.Handle();

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAccountMismatch);
        fixture.Invitation.Status.Should().Be(WorkspaceInvitationStatus.Pending);
        fixture.RecipientOrganizationMembership.Should().BeNull();
        fixture.RecipientWorkspaceMembership.Should().BeNull();
    }

    [Fact]
    public async Task Accept_WhenReplayed_ReturnsCanonicalFailure()
    {
        Fixture fixture = new(WorkspaceMembershipRole.Member);

        Result<WorkspaceInvitationAcceptanceDto> first = await fixture.Handle();
        Result<WorkspaceInvitationAcceptanceDto> replay = await fixture.Handle();

        first.IsSuccess.Should().BeTrue();
        replay.IsFailure.Should().BeTrue();
        replay.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAccessInvalid);
        await fixture.OrganizationMemberships.Received(1).AddAsync(
            Arg.Any<OrganizationMembership>(),
            Arg.Any<CancellationToken>());
        await fixture.WorkspaceMemberships.Received(1).AddAsync(
            Arg.Any<WorkspaceMembership>(),
            Arg.Any<CancellationToken>());
        fixture.Audits.Should().Contain(audit => audit.Outcome == "used");
    }

    private sealed class Fixture
    {
        private readonly AcceptWorkspaceInvitationHandler handler;

        public Fixture(
            WorkspaceMembershipRole requestedRole,
            string recipientEmail = "recipient@example.com")
        {
            Inviter = User.Create("Ada Admin", Email.Create("admin@example.com").Value);
            Inviter.VerifyEmail();
            Recipient = User.Create("Riley Recipient", Email.Create(recipientEmail).Value);
            Recipient.VerifyEmail();
            Organization = Organization.Create("Acme");
            Workspace = Workspace.CreateOrganization(
                "Acme Operations",
                WorkspaceSlug.Create("acme-operations").Value,
                Organization.Id);
            DateTime now = DateTime.UtcNow;
            Invitation = WorkspaceInvitation.Create(
                Organization.Id,
                Workspace.Id,
                Inviter.Id,
                "recipient@example.com",
                requestedRole,
                now,
                now.AddDays(7),
                "token-hash",
                "delivery-envelope",
                "delivery-correlation");
            Invitation.Exchange(
                "token-hash",
                HandoffHash,
                now.AddHours(2),
                now,
                Invitation.Revision).Should().Be(InvitationExchangeOutcome.Exchanged);

            Users.GetByIdPlatformWideAsync(Recipient.Id, Arg.Any<CancellationToken>()).Returns(Recipient);
            Users.GetByIdPlatformWideAsync(Inviter.Id, Arg.Any<CancellationToken>()).Returns(Inviter);
            Organizations.GetByIdAsync(Organization.Id, Arg.Any<CancellationToken>()).Returns(Organization);
            Workspaces.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
            OrganizationMemberships.GetActiveAsync(
                    Organization.Id,
                    Inviter.Id,
                    Arg.Any<CancellationToken>())
                .Returns(OrganizationMembership.Create(
                    Organization.Id,
                    Inviter.Id,
                    OrganizationMembershipRole.Administrator));
            WorkspaceMemberships.GetActiveAsync(
                    Workspace.Id,
                    Inviter.Id,
                    Arg.Any<CancellationToken>())
                .Returns(WorkspaceMembership.CreateOrganizationMember(
                    Workspace.Id,
                    Inviter.Id,
                    WorkspaceMembershipRole.Administrator));
            OrganizationMemberships.GetAsync(
                    Organization.Id,
                    Recipient.Id,
                    Arg.Any<CancellationToken>())
                .Returns(_ => RecipientOrganizationMembership);
            WorkspaceMemberships.GetAsync(
                    Workspace.Id,
                    Recipient.Id,
                    Arg.Any<CancellationToken>())
                .Returns(_ => RecipientWorkspaceMembership);
            OrganizationMemberships.GetActiveAsync(
                    Organization.Id,
                    Recipient.Id,
                    Arg.Any<CancellationToken>())
                .Returns(_ => RecipientOrganizationMembership?.Status == MembershipStatus.Active
                    ? RecipientOrganizationMembership
                    : null);
            WorkspaceMemberships.GetActiveAsync(
                    Workspace.Id,
                    Recipient.Id,
                    Arg.Any<CancellationToken>())
                .Returns(_ => RecipientWorkspaceMembership?.Status == MembershipStatus.Active
                    ? RecipientWorkspaceMembership
                    : null);
            OrganizationMemberships.AddAsync(
                    Arg.Any<OrganizationMembership>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    RecipientOrganizationMembership = call.Arg<OrganizationMembership>();
                    return Task.CompletedTask;
                });
            WorkspaceMemberships.AddAsync(
                    Arg.Any<WorkspaceMembership>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    RecipientWorkspaceMembership = call.Arg<WorkspaceMembership>();
                    return Task.CompletedTask;
                });
            Invitations.GetByHandoffHashAsync(HandoffHash, Arg.Any<CancellationToken>())
                .Returns(Invitation);
            Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Audits.Add(call.Arg<AuditEventV1>());
                    return Task.CompletedTask;
                });
            Audit.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                AuditEventV1? audit = Audits.SingleOrDefault(candidate => candidate.EventId == call.Arg<Guid>());
                return audit is null
                    ? null
                    : new IdentityAuditOutboxEntry(audit, IdentityAuditOutboxState.Pending);
            });

            handler = new AcceptWorkspaceInvitationHandler(
                Users,
                Organizations,
                OrganizationMemberships,
                Workspaces,
                WorkspaceMemberships,
                Invitations,
                Audit,
                TimeProvider.System,
                UnitOfWork);
        }

        public const string HandoffHash = "handoff-hash";
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IOrganizationRepository Organizations { get; } = Substitute.For<IOrganizationRepository>();
        public IOrganizationMembershipRepository OrganizationMemberships { get; } =
            Substitute.For<IOrganizationMembershipRepository>();
        public IWorkspaceRepository Workspaces { get; } = Substitute.For<IWorkspaceRepository>();
        public IWorkspaceMembershipRepository WorkspaceMemberships { get; } =
            Substitute.For<IWorkspaceMembershipRepository>();
        public IWorkspaceInvitationRepository Invitations { get; } =
            Substitute.For<IWorkspaceInvitationRepository>();
        public IIdentityAuditOutbox Audit { get; } = Substitute.For<IIdentityAuditOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public List<AuditEventV1> Audits { get; } = [];
        public User Inviter { get; }
        public User Recipient { get; }
        public Organization Organization { get; }
        public Workspace Workspace { get; }
        public WorkspaceInvitation Invitation { get; }
        public OrganizationMembership? RecipientOrganizationMembership { get; private set; }
        public WorkspaceMembership? RecipientWorkspaceMembership { get; private set; }

        public void WithActiveOrganizationMembership(OrganizationMembershipRole role) =>
            RecipientOrganizationMembership = OrganizationMembership.Create(
                Organization.Id,
                Recipient.Id,
                role);

        public void WithRemovedWorkspaceMembership(WorkspaceMembershipRole role)
        {
            RecipientWorkspaceMembership = WorkspaceMembership.CreateOrganizationMember(
                Workspace.Id,
                Recipient.Id,
                role);
            RecipientWorkspaceMembership.Remove(RecipientWorkspaceMembership.Revision);
        }

        public void WithActiveWorkspaceMembership(WorkspaceMembershipRole role) =>
            RecipientWorkspaceMembership = WorkspaceMembership.CreateOrganizationMember(
                Workspace.Id,
                Recipient.Id,
                role);

        public void WithSuspendedOrganizationMembership()
        {
            RecipientOrganizationMembership = OrganizationMembership.Create(
                Organization.Id,
                Recipient.Id,
                OrganizationMembershipRole.Member);
            RecipientOrganizationMembership.Suspend(RecipientOrganizationMembership.Revision);
        }

        public Task<Result<WorkspaceInvitationAcceptanceDto>> Handle() =>
            handler.Handle(
                new AcceptWorkspaceInvitationCommand(
                    HandoffHash,
                    Recipient.Id,
                    "acceptance-correlation"),
                CancellationToken.None);
    }
}
