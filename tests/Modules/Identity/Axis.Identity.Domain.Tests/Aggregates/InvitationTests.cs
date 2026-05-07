using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class InvitationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static Email ValidEmail => Email.Create("invited@example.com").Value;

    [Fact]
    public void Create_produces_valid_invitation()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.Email.Should().Be(ValidEmail);
        invitation.OrganizationId.Should().Be(OrgId);
        invitation.RoleId.Should().Be(RoleId);
        invitation.InvitedByUserId.Should().Be(InvitedBy);
        invitation.Status.Should().Be(InvitationStatus.Pending);
        invitation.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(48), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_generates_non_empty_token()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Two_invitations_have_different_tokens()
    {
        var a = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);
        var b = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        a.Token.Should().NotBe(b.Token);
    }

    [Fact]
    public void Create_raises_InvitationCreated_event()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InvitationCreated>();
    }

    [Fact]
    public void Accept_changes_status_to_accepted()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);
        invitation.ClearDomainEvents();

        invitation.Accept();

        invitation.Status.Should().Be(InvitationStatus.Accepted);
        invitation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InvitationAccepted>();
    }

    [Fact]
    public void Accept_expired_invitation_throws()
    {
        var invitation = Invitation.CreateExpired(ValidEmail, OrgId, RoleId, InvitedBy);

        var act = () => invitation.Accept();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public void Accept_already_accepted_invitation_throws()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);
        invitation.Accept();

        var act = () => invitation.Accept();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public void Cancel_changes_status_to_cancelled()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.Cancel();

        invitation.Status.Should().Be(InvitationStatus.Cancelled);
    }

    [Fact]
    public void Cancel_accepted_invitation_throws()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);
        invitation.Accept();

        var act = () => invitation.Cancel();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot cancel*");
    }

    [Fact]
    public void IsExpired_returns_true_when_past_expiry()
    {
        var invitation = Invitation.CreateExpired(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_returns_false_for_fresh_invitation()
    {
        var invitation = Invitation.Create(ValidEmail, OrgId, RoleId, InvitedBy);

        invitation.IsExpired.Should().BeFalse();
    }
}
