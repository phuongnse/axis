using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class TeamAccountMembership : AggregateRoot<Guid>
{
    private readonly List<TeamAccountMembershipRole> _roles = [];

    public Guid UserId { get; private set; }
    public Guid TeamAccountId { get; private set; }
    public TeamAccountMembershipStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roles.Select(role => role.RoleId).ToList().AsReadOnly();
    public IReadOnlyList<TeamAccountMembershipRole> Roles => _roles.AsReadOnly();

    private TeamAccountMembership(
        Guid id,
        Guid userId,
        Guid teamAccountId,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        TeamAccountId = teamAccountId;
        Status = TeamAccountMembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public static TeamAccountMembership Create(Guid userId, Guid teamAccountId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (teamAccountId == Guid.Empty)
            throw new ArgumentException("Team account is required.", nameof(teamAccountId));

        return new TeamAccountMembership(Guid.NewGuid(), userId, teamAccountId, DateTime.UtcNow);
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role is required.", nameof(roleId));

        if (_roles.Any(role => role.RoleId == roleId))
            return;

        _roles.Add(TeamAccountMembershipRole.Create(Id, roleId));
        RaiseDomainEvent(new RoleAssigned(UserId, TeamAccountId, roleId));
    }

    public void RemoveRole(Guid roleId)
    {
        TeamAccountMembershipRole? role = _roles.FirstOrDefault(item => item.RoleId == roleId);
        if (role is null)
            return;

        _roles.Remove(role);
        RaiseDomainEvent(new RoleRemoved(UserId, TeamAccountId, roleId));
    }

    public void Deactivate()
    {
        if (Status == TeamAccountMembershipStatus.Inactive)
            throw new InvalidOperationException("Membership is already inactive.");

        Status = TeamAccountMembershipStatus.Inactive;
        RaiseDomainEvent(new UserDeactivated(UserId, TeamAccountId));
    }

    public void Reactivate()
    {
        if (Status == TeamAccountMembershipStatus.Active)
            throw new InvalidOperationException("Membership is already active.");

        Status = TeamAccountMembershipStatus.Active;
        RaiseDomainEvent(new UserReactivated(UserId, TeamAccountId));
    }
}
