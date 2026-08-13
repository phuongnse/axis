using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class WorkspaceMembership : AggregateRoot<Guid>
{
    private WorkspaceMembership(
        Guid workspaceId,
        Guid userId,
        WorkspaceMembershipRole role,
        bool isProductBuilder)
        : base(Guid.NewGuid())
    {
        if (workspaceId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Workspace and user are required.");

        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        IsProductBuilder = isProductBuilder;
        Revision = 1;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceMembershipRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public bool IsProductBuilder { get; private set; }
    public int Revision { get; private set; }
    public bool HasLifecycleAdministratorAuthority =>
        Status == MembershipStatus.Active
        && Role is WorkspaceMembershipRole.Owner or WorkspaceMembershipRole.Administrator;

    public static WorkspaceMembership CreatePersonalOwner(Guid workspaceId, Guid userId) =>
        new(workspaceId, userId, WorkspaceMembershipRole.Owner, isProductBuilder: true);

    public static WorkspaceMembership CreateOrganizationMember(Guid workspaceId, Guid userId, WorkspaceMembershipRole role)
    {
        if (role is WorkspaceMembershipRole.Owner)
            throw new ArgumentOutOfRangeException(
                nameof(role),
                "Organization Workspace memberships cannot be owners.");

        return new(workspaceId, userId, role, isProductBuilder: false);
    }

    public static WorkspaceMembership CreateOrganizationCreator(Guid workspaceId, Guid userId) =>
        new(workspaceId, userId, WorkspaceMembershipRole.Administrator, isProductBuilder: true);

    public void SetProductBuilder(bool enabled, int expectedRevision)
    {
        EnsureOrganizationMembership();
        EnsureActive(expectedRevision);
        if (IsProductBuilder == enabled)
            return;

        IsProductBuilder = enabled;
        Revision++;
    }

    public void Suspend(int expectedRevision)
    {
        EnsureOrganizationMembership();
        EnsureActive(expectedRevision);
        Status = MembershipStatus.Suspended;
        Revision++;
    }

    public void Reactivate(int expectedRevision)
    {
        EnsureOrganizationMembership();
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Suspended)
            throw new InvalidOperationException("Only a suspended Workspace membership can be reactivated.");

        Status = MembershipStatus.Active;
        Revision++;
    }

    public void RestoreFromInvitation(WorkspaceMembershipRole role, int expectedRevision)
    {
        EnsureOrganizationMembership();
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Removed)
            throw new InvalidOperationException("Only a removed Workspace membership can be restored by invitation.");
        if (role is not (WorkspaceMembershipRole.Administrator or WorkspaceMembershipRole.Member))
            throw new ArgumentOutOfRangeException(nameof(role));

        Role = role;
        Status = MembershipStatus.Active;
        Revision++;
    }

    public void Remove(int expectedRevision)
    {
        EnsureOrganizationMembership();
        EnsureActive(expectedRevision);
        IsProductBuilder = false;
        Status = MembershipStatus.Removed;
        Revision++;
    }

    private void EnsureOrganizationMembership()
    {
        if (Role == WorkspaceMembershipRole.Owner)
            throw new InvalidOperationException("A personal Workspace owner membership cannot be suspended or removed.");
    }

    private void EnsureActive(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException("Only an active Workspace membership can change state.");
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (Revision != expectedRevision)
            throw new InvalidOperationException("Workspace membership revision is stale.");
    }
}
