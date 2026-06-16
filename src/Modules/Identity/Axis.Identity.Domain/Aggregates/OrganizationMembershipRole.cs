using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class OrganizationMembershipRole : Entity<(Guid MembershipId, Guid RoleId)>
{
    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAt { get; private set; }

    private OrganizationMembershipRole(Guid membershipId, Guid roleId, DateTime assignedAt)
        : base((membershipId, roleId))
    {
        MembershipId = membershipId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    public static OrganizationMembershipRole Create(Guid membershipId, Guid roleId)
    {
        if (membershipId == Guid.Empty)
            throw new ArgumentException("Membership is required.", nameof(membershipId));
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        return new OrganizationMembershipRole(membershipId, roleId, DateTime.UtcNow);
    }
}
