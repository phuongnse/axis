using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class OrganizationMembership : AggregateRoot<Guid>
{
    private readonly List<OrganizationMembershipRole> _roles = [];

    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public OrganizationMembershipStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roles.Select(role => role.RoleId).ToList().AsReadOnly();
    public IReadOnlyList<OrganizationMembershipRole> Roles => _roles.AsReadOnly();

    private OrganizationMembership(
        Guid id,
        Guid userId,
        Guid organizationId,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        OrganizationId = organizationId;
        Status = OrganizationMembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public static OrganizationMembership Create(Guid userId, Guid organizationId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));

        return new OrganizationMembership(Guid.NewGuid(), userId, organizationId, DateTime.UtcNow);
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        if (_roles.Any(role => role.RoleId == roleId))
            return;

        _roles.Add(OrganizationMembershipRole.Create(Id, roleId));
        RaiseDomainEvent(new RoleAssigned(UserId, OrganizationId, roleId));
    }

    public void RemoveRole(Guid roleId)
    {
        OrganizationMembershipRole? role = _roles.FirstOrDefault(item => item.RoleId == roleId);
        if (role is null)
            return;

        _roles.Remove(role);
        RaiseDomainEvent(new RoleRemoved(UserId, OrganizationId, roleId));
    }

    public void Deactivate()
    {
        if (Status == OrganizationMembershipStatus.Inactive)
            throw new InvalidOperationException("Membership is already inactive.");

        Status = OrganizationMembershipStatus.Inactive;
        RaiseDomainEvent(new UserDeactivated(UserId, OrganizationId));
    }

    public void Reactivate()
    {
        if (Status == OrganizationMembershipStatus.Active)
            throw new InvalidOperationException("Membership is already active.");

        Status = OrganizationMembershipStatus.Active;
        RaiseDomainEvent(new UserReactivated(UserId, OrganizationId));
    }
}
