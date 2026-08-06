using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public sealed class WorkspaceInvitationTests
{
    [Fact]
    public void Create_WhenOwnerRoleIsRequested_RejectsOrganizationElevation()
    {
        DateTime now = DateTime.UtcNow;

        Action act = () => Create(now, WorkspaceMembershipRole.Owner);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Exchange_WhenTokenIsReplayed_ReturnsUsedWithoutSecondHandoff()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = Create(now);

        InvitationExchangeOutcome first = invitation.Exchange(
            "token-1",
            "handoff-1",
            now.AddHours(1),
            now,
            invitation.Revision);
        InvitationExchangeOutcome replay = invitation.Exchange(
            "token-1",
            "handoff-2",
            now.AddHours(1),
            now,
            invitation.Revision);

        first.Should().Be(InvitationExchangeOutcome.Exchanged);
        replay.Should().Be(InvitationExchangeOutcome.Used);
        invitation.Handoffs.Should().ContainSingle();
    }

    [Fact]
    public void Resend_WhenHandoffExists_SupersedesEveryPriorAcceptancePath()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = Create(now);
        invitation.Exchange(
            "token-1",
            "handoff-1",
            now.AddHours(1),
            now,
            invitation.Revision);

        invitation.Resend(
            invitation.Revision,
            now.AddMinutes(1),
            now.AddDays(8),
            "token-2",
            "envelope-2",
            "delivery-2");

        invitation.TokenGenerations.Should().HaveCount(2);
        invitation.TokenGenerations[0].Status.Should().Be(InvitationTokenStatus.Superseded);
        invitation.Handoffs[0].Status.Should().Be(InvitationHandoffStatus.Superseded);
        invitation.CurrentToken.Status.Should().Be(InvitationTokenStatus.Active);
        invitation.Accept("handoff-1", now.AddMinutes(2), invitation.Revision)
            .Should().Be(InvitationAcceptanceOutcome.Superseded);
        invitation.Exchange(
                "token-1",
                "handoff-replay",
                now.AddHours(1),
                now.AddMinutes(2),
                invitation.Revision)
            .Should().Be(InvitationExchangeOutcome.Superseded);
    }

    [Fact]
    public void Accept_WhenHandoffIsCurrent_ProducesOneTerminalOutcome()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = Create(now);
        invitation.Exchange(
            "token-1",
            "handoff-1",
            now.AddHours(1),
            now,
            invitation.Revision);

        InvitationAcceptanceOutcome first = invitation.Accept(
            "handoff-1",
            now.AddMinutes(1),
            invitation.Revision);
        InvitationAcceptanceOutcome replay = invitation.Accept(
            "handoff-1",
            now.AddMinutes(2),
            invitation.Revision);

        first.Should().Be(InvitationAcceptanceOutcome.Accepted);
        replay.Should().Be(InvitationAcceptanceOutcome.Used);
        invitation.Status.Should().Be(WorkspaceInvitationStatus.Accepted);
        invitation.CurrentToken.Status.Should().Be(InvitationTokenStatus.Accepted);
    }

    [Fact]
    public void Revoke_WhenAcceptanceFollows_ReturnsRevokedWithoutMutation()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = Create(now);
        invitation.Exchange(
            "token-1",
            "handoff-1",
            now.AddHours(1),
            now,
            invitation.Revision);

        invitation.Revoke(invitation.Revision, now.AddMinutes(1));

        invitation.Accept("handoff-1", now.AddMinutes(2), invitation.Revision)
            .Should().Be(InvitationAcceptanceOutcome.Revoked);
        invitation.Status.Should().Be(WorkspaceInvitationStatus.Revoked);
    }

    [Fact]
    public void Expire_BeforeExpiry_RejectsPrematureTerminalState()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation invitation = Create(now);

        Action act = () => invitation.Expire(invitation.Revision, now.AddDays(6));

        act.Should().Throw<InvalidOperationException>();
        invitation.Status.Should().Be(WorkspaceInvitationStatus.Pending);
    }

    private static WorkspaceInvitation Create(
        DateTime now,
        WorkspaceMembershipRole role = WorkspaceMembershipRole.Member) =>
        WorkspaceInvitation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "recipient@example.com",
            role,
            now,
            now.AddDays(7),
            "token-1",
            "envelope-1",
            "delivery-1");
}
