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
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    private ActorKind CreatedByKind { get; set; }
    private Guid? CreatedBySubjectId { get; set; }
    private string CreatedByDisplayName { get; set; } = string.Empty;
    private ActorKind UpdatedByKind { get; set; }
    private Guid? UpdatedBySubjectId { get; set; }
    private string UpdatedByDisplayName { get; set; } = string.Empty;
    public ActorSnapshot CreatedBy => Snapshot(CreatedByKind, CreatedBySubjectId, CreatedByDisplayName);
    public ActorSnapshot UpdatedBy => Snapshot(UpdatedByKind, UpdatedBySubjectId, UpdatedByDisplayName);
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

    public void InitializeMetadata(ActorSnapshot actor, DateTime now)
    {
        if (!actor.IsValid || now == default || CreatedByDisplayName.Length > 0) throw new InvalidOperationException("Membership creation provenance is invalid.");
        CreatedAt = now; UpdatedAt = now; StampCreated(actor);
    }

    public void RecordModification(ActorSnapshot actor, DateTime now)
    {
        if (!actor.IsValid || now == default || (UpdatedAt != default && now < UpdatedAt)) throw new InvalidOperationException("Membership modification provenance is invalid.");
        UpdatedAt = now; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName;
    }

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

    private void StampCreated(ActorSnapshot actor) { CreatedByKind = actor.Kind; CreatedBySubjectId = actor.SubjectId; CreatedByDisplayName = actor.DisplayName; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName; }
    private static ActorSnapshot Snapshot(ActorKind kind, Guid? subjectId, string displayName)
    {
        ActorSnapshot actor = new(kind, subjectId, displayName);
        return actor.IsValid ? actor : throw new InvalidOperationException("Workspace membership provenance is incomplete.");
    }
}
