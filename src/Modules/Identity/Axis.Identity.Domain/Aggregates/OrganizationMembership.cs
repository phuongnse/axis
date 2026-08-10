using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class OrganizationMembership : AggregateRoot<Guid>
{
    private OrganizationMembership(Guid organizationId, Guid userId, OrganizationMembershipRole role)
        : base(Guid.NewGuid())
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Organization and user are required.");

        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        Revision = 1;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationMembershipRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public int Revision { get; private set; }
    public static OrganizationMembership Create(
        Guid organizationId,
        Guid userId,
        OrganizationMembershipRole role) =>
        new(organizationId, userId, role);

    public void Suspend(int expectedRevision)
    {
        EnsureActive(expectedRevision);
        Status = MembershipStatus.Suspended;
        Revision++;
    }

    public void Reactivate(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Suspended)
            throw new InvalidOperationException("Only a suspended Organization membership can be reactivated.");

        Status = MembershipStatus.Active;
        Revision++;
    }

    public void RestoreBaselineFromInvitation(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Removed)
            throw new InvalidOperationException("Only a removed Organization membership can be restored by invitation.");

        Role = OrganizationMembershipRole.Member;
        Status = MembershipStatus.Active;
        Revision++;
    }

    public void Remove(int expectedRevision)
    {
        EnsureActive(expectedRevision);
        Status = MembershipStatus.Removed;
        Revision++;
    }

    private void EnsureActive(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException("Only an active Organization membership can change state.");
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (Revision != expectedRevision)
            throw new InvalidOperationException("Organization membership revision is stale.");
    }
}
