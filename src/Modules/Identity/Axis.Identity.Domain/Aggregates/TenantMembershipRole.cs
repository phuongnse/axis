using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class TenantMembershipRole : Entity<(Guid MembershipId, Guid RoleId)>
{
    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAt { get; private set; }

    private TenantMembershipRole(Guid membershipId, Guid roleId, DateTime assignedAt)
        : base((membershipId, roleId))
    {
        MembershipId = membershipId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    public static TenantMembershipRole Create(Guid membershipId, Guid roleId)
    {
        if (membershipId == Guid.Empty)
            throw new ArgumentException("Membership is required.", nameof(membershipId));
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        return new TenantMembershipRole(membershipId, roleId, DateTime.UtcNow);
    }
}
