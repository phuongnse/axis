using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public sealed class GovernanceAggregateTests
{
    [Fact]
    public void Organization_WhenNameHasWhitespace_NormalizesUnicodeName()
    {
        Organization organization = Organization.Create("  Acme  ");
        organization.Name.Should().Be("Acme");
        organization.Revision.Should().Be(1);
    }

    [Fact]
    public void OrganizationWorkspaceMembership_WhenOwnerRoleIsRequested_RejectsRole()
    {
        Action act = () => WorkspaceMembership.CreateOrganizationMember(Guid.NewGuid(), Guid.NewGuid(), WorkspaceMembershipRole.Owner);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(WorkspaceMembershipRole.Owner, true)]
    [InlineData(WorkspaceMembershipRole.Administrator, true)]
    [InlineData(WorkspaceMembershipRole.Member, false)]
    public void WorkspaceMembership_WhenActive_UsesRoleContextualLifecycleAuthority(
        WorkspaceMembershipRole role,
        bool expected)
    {
        WorkspaceMembership membership = role == WorkspaceMembershipRole.Owner
            ? WorkspaceMembership.CreatePersonalOwner(Guid.NewGuid(), Guid.NewGuid())
            : WorkspaceMembership.CreateOrganizationMember(Guid.NewGuid(), Guid.NewGuid(), role);

        membership.HasLifecycleAdministratorAuthority.Should().Be(expected);
    }

    [Fact]
    public void OrganizationAdministrator_WhenSuspended_LosesLifecycleAuthority()
    {
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceMembershipRole.Administrator);

        membership.Suspend(membership.Revision);

        membership.HasLifecycleAdministratorAuthority.Should().BeFalse();
    }

    [Fact]
    public void WorkspaceMembership_ProductBuilderState_IsExplicitAndIndependentOfRole()
    {
        WorkspaceMembership creator = WorkspaceMembership.CreateOrganizationCreator(
            Guid.NewGuid(),
            Guid.NewGuid());
        WorkspaceMembership administrator = WorkspaceMembership.CreateOrganizationMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceMembershipRole.Administrator);
        WorkspaceMembership member = WorkspaceMembership.CreateOrganizationMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceMembershipRole.Member);

        creator.IsProductBuilder.Should().BeTrue();
        administrator.IsProductBuilder.Should().BeFalse();
        member.SetProductBuilder(true, member.Revision);
        member.IsProductBuilder.Should().BeTrue();
        member.Role.Should().Be(WorkspaceMembershipRole.Member);
    }

    [Fact]
    public void WorkspaceMembership_ProductBuilderLifecycle_PreservesOnSuspensionAndClearsOnRemoval()
    {
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceMembershipRole.Member);
        membership.SetProductBuilder(true, membership.Revision);

        membership.Suspend(membership.Revision);
        membership.IsProductBuilder.Should().BeTrue();
        Action changeWhileSuspended = () => membership.SetProductBuilder(false, membership.Revision);
        changeWhileSuspended.Should().Throw<InvalidOperationException>();
        membership.Reactivate(membership.Revision);
        membership.IsProductBuilder.Should().BeTrue();
        membership.Remove(membership.Revision);
        membership.IsProductBuilder.Should().BeFalse();
        membership.RestoreFromInvitation(WorkspaceMembershipRole.Member, membership.Revision);
        membership.IsProductBuilder.Should().BeFalse();
    }

    [Fact]
    public void WorkspaceMembership_EquivalentProductBuilderState_ReturnsCanonicalRevision()
    {
        WorkspaceMembership membership = WorkspaceMembership.CreateOrganizationMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceMembershipRole.Member);

        membership.SetProductBuilder(false, membership.Revision);

        membership.Revision.Should().Be(1);
    }

    [Fact]
    public void Transition_WhenCompletedThenCompensated_RejectsSecondTerminalState()
    {
        DateTime now = DateTime.UtcNow;
        string sourceDigest = new('a', WorkspaceContextTransition.CorrelationDigestLength);
        string targetDigest = new('b', WorkspaceContextTransition.CorrelationDigestLength);
        WorkspaceContextTransition transition = WorkspaceContextTransition.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sourceDigest, targetDigest, now, now.AddMinutes(5), now.AddHours(2));
        transition.SourceCorrelationDigest.Should().Be(sourceDigest);
        transition.Complete(1, DateTime.UtcNow);
        transition.Status.Should().Be(WorkspaceContextTransitionStatus.Completed);
        Action act = () => transition.Compensate(2, DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Transition_WhenExpired_RejectsCompletion()
    {
        DateTime expiresAt = DateTime.UtcNow.AddMinutes(1);
        DateTime now = DateTime.UtcNow;
        WorkspaceContextTransition transition = WorkspaceContextTransition.Begin(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new string('a', WorkspaceContextTransition.CorrelationDigestLength),
            new string('b', WorkspaceContextTransition.CorrelationDigestLength),
            now, expiresAt, expiresAt.AddHours(1));
        Action act = () => transition.Complete(1, expiresAt.AddTicks(1));
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Transition_WhenCorrelationDigestIsNotLowercaseSha256Hex_RejectsIt(
        string invalidDigest)
    {
        DateTime now = DateTime.UtcNow;

        Action act = () => WorkspaceContextTransition.Begin(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            invalidDigest,
            new string('b', WorkspaceContextTransition.CorrelationDigestLength),
            now,
            now.AddMinutes(5),
            now.AddHours(1));

        act.Should().Throw<ArgumentException>();
    }
}
