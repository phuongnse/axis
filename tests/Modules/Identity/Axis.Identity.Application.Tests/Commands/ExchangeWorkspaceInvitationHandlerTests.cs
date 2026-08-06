using Axis.Audit.Contracts;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.ExchangeWorkspaceInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ExchangeWorkspaceInvitationHandlerTests
{
    [Fact]
    public async Task Exchange_WhenOpaqueTokenIsUnknown_PersistsRedactedPlatformAudit()
    {
        Fixture fixture = new();

        Result<WorkspaceInvitationExchangeDto> result = await fixture.Handle();

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAccessInvalid);
        AuditEventV1 audit = fixture.Audits.Should().ContainSingle().Subject;
        audit.ActorKind.Should().Be(AuditActorKindV1.Anonymous);
        audit.ActorId.Should().BeNull();
        audit.SubjectId.Should().BeNull();
        audit.WorkspaceId.Should().BeNull();
        audit.Action.Should().Be("workspace.invitation.exchange_rejected");
        audit.TargetType.Should().Be("WorkspaceInvitationAccessAttempt");
        audit.TargetId.Should().NotBeEmpty();
        audit.Outcome.Should().Be("invalid");
        audit.Metadata.Should().BeNull();
        audit.ToString().Should().NotContain(Fixture.RawToken);
        audit.ToString().Should().NotContain(Fixture.RequestPartition);
    }

    [Fact]
    public async Task Exchange_WhenTokenShapeIsInvalid_AuditsWithoutInvitationLookup()
    {
        Fixture fixture = new();

        Result<WorkspaceInvitationExchangeDto> result = await fixture.Handle("not-an-opaque-token");

        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAccessInvalid);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.WorkspaceId == null && audit.Outcome == "invalid");
        await fixture.Invitations.DidNotReceiveWithAnyArgs()
            .GetByTokenHashAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exchange_WhenRateLimited_AuditsBeforeInvitationLookup()
    {
        Fixture fixture = new();
        fixture.RateLimiter.AcquireExchangeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                ErrorCodes.RateLimited,
                "Rate limited.",
                IdentityProblemCodes.InvitationRateLimited));

        Result<WorkspaceInvitationExchangeDto> result = await fixture.Handle();

        result.ErrorCode.Should().Be(ErrorCodes.RateLimited);
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationRateLimited);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.WorkspaceId == null && audit.Outcome == "rate_limited");
        await fixture.Invitations.DidNotReceiveWithAnyArgs()
            .GetByTokenHashAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exchange_WhenPlatformAuditCannotBeReadBack_FailsClosed()
    {
        Fixture fixture = new() { ReturnAuditReadBack = false };

        Result<WorkspaceInvitationExchangeDto> result = await fixture.Handle();

        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAuditUnavailable);
    }

    [Fact]
    public async Task Exchange_WhenKnownInvitationIsTerminal_KeepsAuditWorkspaceScoped()
    {
        Fixture fixture = new();
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "recipient@example.com",
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7),
            OpaqueTokenGenerator.Hash(Fixture.RawToken),
            "delivery-envelope",
            "delivery-correlation");
        invitation.Revoke(invitation.Revision, now);
        fixture.Invitations.GetByTokenHashAsync(
                OpaqueTokenGenerator.Hash(Fixture.RawToken),
                Arg.Any<CancellationToken>())
            .Returns(invitation);

        Result<WorkspaceInvitationExchangeDto> result = await fixture.Handle();

        result.ProblemCode.Should().Be(IdentityProblemCodes.InvitationAccessInvalid);
        fixture.Audits.Should().ContainSingle(audit =>
            audit.WorkspaceId == invitation.WorkspaceId
            && audit.TargetId == invitation.Id
            && audit.Outcome == "revoked");
    }

    private sealed class Fixture
    {
        private readonly ExchangeWorkspaceInvitationHandler handler;

        public Fixture()
        {
            RateLimiter.AcquireExchangeAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success());
            Audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Audits.Add(call.Arg<AuditEventV1>());
                    return Task.CompletedTask;
                });
            Audit.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                AuditEventV1? audit = Audits.SingleOrDefault(candidate => candidate.EventId == call.Arg<Guid>());
                return ReturnAuditReadBack && audit is not null
                    ? new IdentityAuditOutboxEntry(audit, IdentityAuditOutboxState.Pending)
                    : null;
            });

            handler = new ExchangeWorkspaceInvitationHandler(
                Invitations,
                RateLimiter,
                Audit,
                new WorkspaceInvitationPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(2), 20, 100),
                TimeProvider.System,
                UnitOfWork);
        }

        public const string RawToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string RequestPartition = "request-partition";
        public IWorkspaceInvitationRepository Invitations { get; } =
            Substitute.For<IWorkspaceInvitationRepository>();
        public IWorkspaceInvitationRateLimiter RateLimiter { get; } =
            Substitute.For<IWorkspaceInvitationRateLimiter>();
        public IIdentityAuditOutbox Audit { get; } = Substitute.For<IIdentityAuditOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public List<AuditEventV1> Audits { get; } = [];
        public bool ReturnAuditReadBack { get; init; } = true;

        public Task<Result<WorkspaceInvitationExchangeDto>> Handle(string rawToken = RawToken) => handler.Handle(
            new ExchangeWorkspaceInvitationCommand(
                rawToken,
                RequestPartition,
                "exchange-correlation"),
            CancellationToken.None);
    }
}
