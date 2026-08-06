using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.InviteWorkspaceMember;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class InviteWorkspaceMemberHandlerTests
{
    [Fact]
    public async Task InviteWorkspaceMember_WhenAuthorized_PersistsCanonicalInvitationDeliveryAndAudit()
    {
        Fixture fixture = new();

        Result<InviteWorkspaceMemberDto> result = await fixture.Handle(
            " Recipient@Example.com ",
            "Member");

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("Created");
        result.Value.Invitation.Should().NotBeNull();
        fixture.CreatedInvitation.Should().NotBeNull();
        fixture.CreatedInvitation!.NormalizedEmail.Should().Be("recipient@example.com");
        fixture.CreatedInvitation.CurrentToken.DeliveryEnvelope.Should().Be("protected-envelope");
        fixture.DeliveryMessage.Should().NotBeNull();
        fixture.DeliveryMessage!.RecipientEmail.Should().Be("recipient@example.com");
        fixture.DeliveryMessage.RawToken.Should().NotBeNullOrWhiteSpace();
        fixture.DeliveryMessage.WorkspaceName.Should().Be(fixture.Workspace.Name);
        fixture.CreatedAudit.Should().NotBeNull();
        fixture.CreatedAudit!.Metadata.Should().NotContainKey("email");
        fixture.CreatedAudit.Metadata.Should().NotContainKey("token");
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-email", "Member", "email")]
    [InlineData("recipient@example.com", "Owner", "requestedRole")]
    [InlineData("recipient@example.com", "ProductAdministrator", "requestedRole")]
    public async Task InviteWorkspaceMember_WhenInputIsInvalid_FailsBeforeMutation(
        string email,
        string role,
        string field)
    {
        Fixture fixture = new();

        Result<InviteWorkspaceMemberDto> result = await fixture.Handle(email, role);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey(field);
        await fixture.Invitations.DidNotReceive().AddAsync(
            Arg.Any<WorkspaceInvitation>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteWorkspaceMember_WhenAuthorityIsMissing_PersistsRedactedAttemptBeforeDenial()
    {
        Fixture fixture = new(authorize: false);

        Result<InviteWorkspaceMemberDto> result = await fixture.Handle(
            "recipient@example.com",
            "Administrator");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        fixture.CreatedAudit.Should().NotBeNull();
        fixture.CreatedAudit!.Action.Should().Be("workspace.invitation.create_denied");
        fixture.CreatedAudit.Metadata.Should().NotContainKey("email");
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteWorkspaceMember_WhenEquivalentPendingExists_ReturnsCanonicalWithoutNewToken()
    {
        Fixture fixture = new();
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation canonical = WorkspaceInvitation.Create(
            fixture.Organization.Id,
            fixture.Workspace.Id,
            fixture.Inviter.Id,
            "recipient@example.com",
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7),
            "existing-hash",
            "existing-envelope",
            "existing-correlation");
        fixture.Invitations.GetCanonicalPendingAsync(
                fixture.Workspace.Id,
                "recipient@example.com",
                WorkspaceMembershipRole.Member,
                Arg.Any<CancellationToken>())
            .Returns(canonical);

        Result<InviteWorkspaceMemberDto> result = await fixture.Handle(
            "recipient@example.com",
            "Member");

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("CanonicalPending");
        result.Value.Invitation!.InvitationId.Should().Be(canonical.Id);
        await fixture.RateLimiter.DidNotReceive().AcquireCreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteWorkspaceMember_WhenActiveRecipientMembershipExists_ReturnsExistingMember()
    {
        Fixture fixture = new();
        User recipient = User.Create("Recipient", Email.Create("recipient@example.com").Value);
        fixture.Users.FindByEmailGloballyAsync(
                Email.Create("recipient@example.com").Value,
                Arg.Any<CancellationToken>())
            .Returns(recipient);
        fixture.WorkspaceMemberships.GetActiveAsync(
                fixture.Workspace.Id,
                recipient.Id,
                Arg.Any<CancellationToken>())
            .Returns(WorkspaceMembership.CreateOrganizationMember(
                fixture.Workspace.Id,
                recipient.Id,
                WorkspaceMembershipRole.Member));

        Result<InviteWorkspaceMemberDto> result = await fixture.Handle(
            "recipient@example.com",
            "Member");

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("ExistingMember");
        result.Value.Invitation.Should().BeNull();
        await fixture.Invitations.DidNotReceive().AddAsync(
            Arg.Any<WorkspaceInvitation>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture(bool authorize = true)
        {
            Inviter = User.Create("Ada Admin", Email.Create("ada@example.com").Value);
            Inviter.VerifyEmail();
            Organization = Organization.Create("Acme");
            Workspace = Workspace.CreateOrganization(
                "Acme Operations",
                WorkspaceSlug.Create("acme-operations").Value,
                Organization.Id);

            Workspaces.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
            Organizations.GetByIdAsync(Organization.Id, Arg.Any<CancellationToken>()).Returns(Organization);
            Users.GetByIdPlatformWideAsync(Inviter.Id, Arg.Any<CancellationToken>()).Returns(Inviter);
            if (authorize)
            {
                OrganizationMemberships.GetActiveAsync(
                        Organization.Id,
                        Inviter.Id,
                        Arg.Any<CancellationToken>())
                    .Returns(Axis.Identity.Domain.Aggregates.OrganizationMembership.Create(
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
            }

            RateLimiter.AcquireCreateAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success());
            EnvelopeProtector.Protect(Arg.Any<InvitationDeliveryMessage>()).Returns(call =>
            {
                DeliveryMessage = call.Arg<InvitationDeliveryMessage>();
                return "protected-envelope";
            });
            Invitations.AddAsync(Arg.Any<WorkspaceInvitation>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    CreatedInvitation = call.Arg<WorkspaceInvitation>();
                    return Task.CompletedTask;
                });
            Invitations.GetByIdAsync(
                    Workspace.Id,
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => CreatedInvitation?.Id == call.ArgAt<Guid>(1) ? CreatedInvitation : null);
            Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    CreatedAudit = call.Arg<AuditEventV1>();
                    return Task.CompletedTask;
                });
            Audit.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                CreatedAudit?.EventId == call.Arg<Guid>()
                    ? new IdentityAuditOutboxEntry(CreatedAudit, IdentityAuditOutboxState.Pending)
                    : null);
        }

        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IOrganizationRepository Organizations { get; } = Substitute.For<IOrganizationRepository>();
        public IOrganizationMembershipRepository OrganizationMemberships { get; } =
            Substitute.For<IOrganizationMembershipRepository>();
        public IWorkspaceRepository Workspaces { get; } = Substitute.For<IWorkspaceRepository>();
        public IWorkspaceMembershipRepository WorkspaceMemberships { get; } =
            Substitute.For<IWorkspaceMembershipRepository>();
        public IWorkspaceInvitationRepository Invitations { get; } =
            Substitute.For<IWorkspaceInvitationRepository>();
        public IWorkspaceInvitationRateLimiter RateLimiter { get; } =
            Substitute.For<IWorkspaceInvitationRateLimiter>();
        public IInvitationDeliveryEnvelopeProtector EnvelopeProtector { get; } =
            Substitute.For<IInvitationDeliveryEnvelopeProtector>();
        public IIdentityAuditOutbox Audit { get; } = Substitute.For<IIdentityAuditOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public User Inviter { get; }
        public Organization Organization { get; }
        public Workspace Workspace { get; }
        public WorkspaceInvitation? CreatedInvitation { get; private set; }
        public InvitationDeliveryMessage? DeliveryMessage { get; private set; }
        public AuditEventV1? CreatedAudit { get; private set; }

        public Task<Result<InviteWorkspaceMemberDto>> Handle(string email, string role) =>
            new InviteWorkspaceMemberHandler(
                Users,
                Organizations,
                OrganizationMemberships,
                Workspaces,
                WorkspaceMemberships,
                Invitations,
                RateLimiter,
                EnvelopeProtector,
                Audit,
                new WorkspaceInvitationPolicy(
                    TimeSpan.FromDays(7),
                    TimeSpan.FromHours(2),
                    20,
                    100),
                TimeProvider.System,
                UnitOfWork).Handle(
                new InviteWorkspaceMemberCommand(
                    Inviter.Id,
                    Workspace.Id,
                    email,
                    role,
                    "correlation"),
                CancellationToken.None);
    }
}
