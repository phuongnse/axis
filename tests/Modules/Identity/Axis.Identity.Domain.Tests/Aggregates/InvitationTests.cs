using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class InvitationTests
{
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static Email ValidEmail => Email.Create("invited@example.com").Value;

    [Fact]
    public void Invitation_WhenCreated_ProducesValidInvitation()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.Email.Should().Be(ValidEmail);
        invitation.TeamAccountId.Should().Be(TeamAccountId);
        invitation.RoleId.Should().Be(RoleId);
        invitation.InvitedByUserId.Should().Be(InvitedBy);
        invitation.Status.Should().Be(InvitationStatus.Pending);
        invitation.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(48), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Invitation_WhenCreated_GeneratesNonEmptyToken()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Invitation_WhenTwoCreated_HaveDifferentTokens()
    {
        Invitation a = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);
        Invitation b = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        a.Token.Should().NotBe(b.Token);
    }

    [Fact]
    public void Invitation_WhenCreated_RaisesInvitationCreatedEvent()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InvitationCreated>();
    }

    [Fact]
    public void Invitation_WhenAccepted_ChangesStatusToAccepted()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);
        invitation.ClearDomainEvents();

        invitation.Accept();

        invitation.Status.Should().Be(InvitationStatus.Accepted);
        invitation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InvitationAccepted>();
    }

    [Fact]
    public void Invitation_WhenExpired_AcceptThrows()
    {
        Invitation invitation = Invitation.CreateExpired(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        Action act = () => invitation.Accept();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public void Invitation_WhenAlreadyAccepted_AcceptThrows()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);
        invitation.Accept();

        Action act = () => invitation.Accept();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public void Invitation_WhenCancelled_ChangesStatusToCancelled()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.Cancel();

        invitation.Status.Should().Be(InvitationStatus.Cancelled);
    }

    [Fact]
    public void Invitation_WhenAlreadyAccepted_CancelThrows()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);
        invitation.Accept();

        Action act = () => invitation.Cancel();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot cancel*");
    }

    [Fact]
    public void IsExpired_WhenPastExpiry_ReturnsTrue()
    {
        Invitation invitation = Invitation.CreateExpired(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WhenFreshInvitation_ReturnsFalse()
    {
        Invitation invitation = Invitation.Create(ValidEmail, TeamAccountId, RoleId, InvitedBy);

        invitation.IsExpired.Should().BeFalse();
    }
}
