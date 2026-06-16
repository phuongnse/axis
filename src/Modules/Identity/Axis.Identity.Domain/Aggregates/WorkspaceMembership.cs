using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class WorkspaceMembership : AggregateRoot<Guid>
{
    private readonly List<WorkspaceMembershipRole> _roles = [];

    public Guid UserId { get; private set; }
    public Guid workspaceId { get; private set; }
    public WorkspaceMembershipStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roles.Select(role => role.RoleId).ToList().AsReadOnly();
    public IReadOnlyList<WorkspaceMembershipRole> Roles => _roles.AsReadOnly();

    private WorkspaceMembership(
        Guid id,
        Guid userId,
        Guid workspaceId,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        this.workspaceId = workspaceId;
        Status = WorkspaceMembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public static WorkspaceMembership Create(Guid userId, Guid workspaceId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace is required.", nameof(workspaceId));

        return new WorkspaceMembership(Guid.NewGuid(), userId, workspaceId, DateTime.UtcNow);
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        if (_roles.Any(role => role.RoleId == roleId))
            return;

        _roles.Add(WorkspaceMembershipRole.Create(Id, roleId));
        RaiseDomainEvent(new RoleAssigned(UserId, workspaceId, roleId));
    }

    public void RemoveRole(Guid roleId)
    {
        WorkspaceMembershipRole? role = _roles.FirstOrDefault(item => item.RoleId == roleId);
        if (role is null)
            return;

        _roles.Remove(role);
        RaiseDomainEvent(new RoleRemoved(UserId, workspaceId, roleId));
    }

    public void Deactivate()
    {
        if (Status == WorkspaceMembershipStatus.Inactive)
            throw new InvalidOperationException("Membership is already inactive.");

        Status = WorkspaceMembershipStatus.Inactive;
        RaiseDomainEvent(new UserDeactivated(UserId, workspaceId));
    }

    public void Reactivate()
    {
        if (Status == WorkspaceMembershipStatus.Active)
            throw new InvalidOperationException("Membership is already active.");

        Status = WorkspaceMembershipStatus.Active;
        RaiseDomainEvent(new UserReactivated(UserId, workspaceId));
    }
}
