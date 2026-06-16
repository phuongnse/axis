using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class TenantMembership : AggregateRoot<Guid>
{
    private readonly List<TenantMembershipRole> _roles = [];

    public Guid UserId { get; private set; }
    public Guid tenantId { get; private set; }
    public TenantMembershipStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roles.Select(role => role.RoleId).ToList().AsReadOnly();
    public IReadOnlyList<TenantMembershipRole> Roles => _roles.AsReadOnly();

    private TenantMembership(
        Guid id,
        Guid userId,
        Guid tenantId,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        this.tenantId = tenantId;
        Status = TenantMembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public static TenantMembership Create(Guid userId, Guid tenantId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant is required.", nameof(tenantId));

        return new TenantMembership(Guid.NewGuid(), userId, tenantId, DateTime.UtcNow);
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        if (_roles.Any(role => role.RoleId == roleId))
            return;

        _roles.Add(TenantMembershipRole.Create(Id, roleId));
        RaiseDomainEvent(new RoleAssigned(UserId, tenantId, roleId));
    }

    public void RemoveRole(Guid roleId)
    {
        TenantMembershipRole? role = _roles.FirstOrDefault(item => item.RoleId == roleId);
        if (role is null)
            return;

        _roles.Remove(role);
        RaiseDomainEvent(new RoleRemoved(UserId, tenantId, roleId));
    }

    public void Deactivate()
    {
        if (Status == TenantMembershipStatus.Inactive)
            throw new InvalidOperationException("Membership is already inactive.");

        Status = TenantMembershipStatus.Inactive;
        RaiseDomainEvent(new UserDeactivated(UserId, tenantId));
    }

    public void Reactivate()
    {
        if (Status == TenantMembershipStatus.Active)
            throw new InvalidOperationException("Membership is already active.");

        Status = TenantMembershipStatus.Active;
        RaiseDomainEvent(new UserReactivated(UserId, tenantId));
    }
}
