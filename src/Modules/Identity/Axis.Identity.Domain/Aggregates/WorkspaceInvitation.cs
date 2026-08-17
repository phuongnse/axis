using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class WorkspaceInvitation : AggregateRoot<Guid>
{
    private readonly List<InvitationTokenGeneration> tokenGenerations = [];
    private readonly List<InvitationHandoff> handoffs = [];

    private WorkspaceInvitation() : base(Guid.Empty)
    {
    }

    private WorkspaceInvitation(
        Guid id,
        Guid organizationId,
        Guid workspaceId,
        Guid inviterUserId,
        string normalizedEmail,
        WorkspaceMembershipRole requestedRole,
        DateTime createdAt,
        DateTime expiresAt,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation)
        : base(id)
    {
        if (id == Guid.Empty || organizationId == Guid.Empty || workspaceId == Guid.Empty || inviterUserId == Guid.Empty)
            throw new ArgumentException("Invitation, Organization, Workspace, and inviter are required.");
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Recipient email is required.", nameof(normalizedEmail));
        if (requestedRole is not (WorkspaceMembershipRole.Administrator or WorkspaceMembershipRole.Member))
            throw new ArgumentOutOfRangeException(
                nameof(requestedRole),
                "An Organization Workspace invitation cannot grant owner authority.");
        if (expiresAt <= createdAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));

        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        InviterUserId = inviterUserId;
        NormalizedEmail = normalizedEmail.Trim();
        RequestedRole = requestedRole;
        Status = WorkspaceInvitationStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ExpiresAt = expiresAt;
        Revision = 1;
        tokenGenerations.Add(InvitationTokenGeneration.Create(
            1,
            tokenHash,
            deliveryEnvelope,
            deliveryCorrelation,
            expiresAt));
    }

    public Guid OrganizationId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid InviterUserId { get; private set; }
    public string? NormalizedEmail { get; private set; }
    public WorkspaceMembershipRole RequestedRole { get; private set; }
    public WorkspaceInvitationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? TerminalMaterialPurgedAt { get; private set; }
    public int Revision { get; private set; }
    private ActorKind? CreatedByKind { get; set; }
    private Guid? CreatedBySubjectId { get; set; }
    private string? CreatedByDisplayName { get; set; }
    private ActorKind? UpdatedByKind { get; set; }
    private Guid? UpdatedBySubjectId { get; set; }
    private string? UpdatedByDisplayName { get; set; }
    public ActorSnapshot? CreatedBy => Snapshot(CreatedByKind, CreatedBySubjectId, CreatedByDisplayName);
    public ActorSnapshot? UpdatedBy => Snapshot(UpdatedByKind, UpdatedBySubjectId, UpdatedByDisplayName);
    public IReadOnlyList<InvitationTokenGeneration> TokenGenerations => tokenGenerations;
    public IReadOnlyList<InvitationHandoff> Handoffs => handoffs;

    public InvitationTokenGeneration CurrentToken => tokenGenerations[^1];

    public void InitializeMetadata(ActorSnapshot actor)
    {
        if (!actor.IsValid || CreatedBy is not null) throw new InvalidOperationException("Invitation creation provenance is invalid.");
        StampCreated(actor);
    }

    public void RecordModification(ActorSnapshot actor, DateTime now)
    {
        if (!actor.IsValid || (UpdatedAt.HasValue && now < UpdatedAt.Value)) throw new InvalidOperationException("Invitation modification provenance is invalid.");
        UpdatedAt = now; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName;
    }

    public static WorkspaceInvitation Create(
        Guid organizationId,
        Guid workspaceId,
        Guid inviterUserId,
        string normalizedEmail,
        WorkspaceMembershipRole requestedRole,
        DateTime createdAt,
        DateTime expiresAt,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation) =>
        new(
            Guid.NewGuid(),
            organizationId,
            workspaceId,
            inviterUserId,
            normalizedEmail,
            requestedRole,
            createdAt,
            expiresAt,
            tokenHash,
            deliveryEnvelope,
            deliveryCorrelation);

    public static WorkspaceInvitation Create(
        Guid invitationId,
        Guid organizationId,
        Guid workspaceId,
        Guid inviterUserId,
        string normalizedEmail,
        WorkspaceMembershipRole requestedRole,
        DateTime createdAt,
        DateTime expiresAt,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation) =>
        new(
            invitationId,
            organizationId,
            workspaceId,
            inviterUserId,
            normalizedEmail,
            requestedRole,
            createdAt,
            expiresAt,
            tokenHash,
            deliveryEnvelope,
            deliveryCorrelation);

    public bool IsEquivalent(string normalizedEmail, WorkspaceMembershipRole role) =>
        Status == WorkspaceInvitationStatus.Pending
        && StringComparer.Ordinal.Equals(NormalizedEmail, normalizedEmail)
        && RequestedRole == role;

    public void Resend(
        int expectedRevision,
        DateTime now,
        DateTime expiresAt,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation)
    {
        EnsurePending(expectedRevision, now);
        CurrentToken.Supersede();
        SupersedeActiveHandoffs();
        int generation = CurrentToken.Generation + 1;
        tokenGenerations.Add(InvitationTokenGeneration.Create(
            generation,
            tokenHash,
            deliveryEnvelope,
            deliveryCorrelation,
            expiresAt));
        ExpiresAt = expiresAt;
        Revision++;
    }

    public void Revoke(int expectedRevision, DateTime now)
    {
        EnsurePending(expectedRevision, now);
        CurrentToken.Revoke();
        RevokeActiveHandoffs();
        Status = WorkspaceInvitationStatus.Revoked;
        RevokedAt = now;
        Revision++;
    }

    public InvitationExchangeOutcome Exchange(
        string tokenHash,
        string handoffHash,
        DateTime handoffExpiresAt,
        DateTime now,
        int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        InvitationTokenGeneration? token = tokenGenerations.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.TokenHash, tokenHash));
        if (token is null)
            return InvitationExchangeOutcome.Unknown;

        InvitationExchangeOutcome terminal = ClassifyToken(token, now);
        if (terminal != InvitationExchangeOutcome.Exchanged)
            return terminal;

        token.Exchange();
        handoffs.Add(InvitationHandoff.Create(token.Generation, handoffHash, handoffExpiresAt));
        Revision++;
        return InvitationExchangeOutcome.Exchanged;
    }

    public InvitationAcceptanceOutcome Accept(
        string handoffHash,
        DateTime now,
        int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        InvitationHandoff? handoff = FindHandoff(handoffHash);
        InvitationAcceptanceOutcome terminal = ClassifyHandoff(handoffHash, now);
        if (terminal != InvitationAcceptanceOutcome.Accepted)
            return terminal;

        handoff!.Accept();
        CurrentToken.Accept();
        Status = WorkspaceInvitationStatus.Accepted;
        AcceptedAt = now;
        Revision++;
        return InvitationAcceptanceOutcome.Accepted;
    }

    public InvitationAcceptanceOutcome ClassifyHandoff(string handoffHash, DateTime now)
    {
        InvitationHandoff? handoff = FindHandoff(handoffHash);
        if (handoff is null)
            return InvitationAcceptanceOutcome.Unknown;
        if (handoff.ExpiresAt <= now)
            return InvitationAcceptanceOutcome.Expired;

        InvitationAcceptanceOutcome handoffOutcome = handoff.Status switch
        {
            InvitationHandoffStatus.Active => InvitationAcceptanceOutcome.Accepted,
            InvitationHandoffStatus.Accepted => InvitationAcceptanceOutcome.Used,
            InvitationHandoffStatus.Revoked => InvitationAcceptanceOutcome.Revoked,
            InvitationHandoffStatus.Superseded => InvitationAcceptanceOutcome.Superseded,
            InvitationHandoffStatus.Expired => InvitationAcceptanceOutcome.Expired,
            _ => InvitationAcceptanceOutcome.Unknown,
        };
        if (handoffOutcome != InvitationAcceptanceOutcome.Accepted)
            return handoffOutcome;

        return Status switch
        {
            WorkspaceInvitationStatus.Pending => InvitationAcceptanceOutcome.Accepted,
            WorkspaceInvitationStatus.Accepted => InvitationAcceptanceOutcome.Used,
            WorkspaceInvitationStatus.Revoked => InvitationAcceptanceOutcome.Revoked,
            WorkspaceInvitationStatus.Expired => InvitationAcceptanceOutcome.Expired,
            _ => InvitationAcceptanceOutcome.Unknown,
        };
    }

    public bool IsTargetEmail(string normalizedEmail) =>
        NormalizedEmail is not null
        && StringComparer.Ordinal.Equals(NormalizedEmail, normalizedEmail);

    public void MarkDeliveryAttempt(int expectedRevision, DateTime nextAttemptAt, string? errorCode)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        CurrentToken.MarkDeliveryAttempt(nextAttemptAt, errorCode);
        Revision++;
    }

    public void MarkDelivered(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        CurrentToken.MarkDelivered();
        Revision++;
    }

    public void RecordDeliveryFailure(
        int expectedRevision,
        DateTime nextAttemptAt,
        string errorCode)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        CurrentToken.RecordDeliveryFailure(nextAttemptAt, errorCode);
        Revision++;
    }

    public void MarkTerminalDeliveryFailure(int expectedRevision, string errorCode)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        CurrentToken.MarkTerminalDeliveryFailure(errorCode);
        Revision++;
    }

    public void Expire(int expectedRevision, DateTime now)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        if (ExpiresAt > now)
            throw new InvalidOperationException("The invitation has not expired.");

        CurrentToken.Expire();
        foreach (InvitationHandoff handoff in handoffs.Where(candidate =>
                     candidate.Status == InvitationHandoffStatus.Active))
        {
            handoff.Expire();
        }

        Status = WorkspaceInvitationStatus.Expired;
        Revision++;
    }

    public void PurgeTerminalMaterial(int expectedRevision, DateTime now)
    {
        EnsureRevision(expectedRevision);
        if (Status == WorkspaceInvitationStatus.Pending)
            throw new InvalidOperationException("Pending invitation material cannot be purged.");

        NormalizedEmail = null;
        TerminalMaterialPurgedAt = now;
        Revision++;
    }

    private InvitationExchangeOutcome ClassifyToken(InvitationTokenGeneration token, DateTime now)
    {
        if (token.ExpiresAt <= now)
            return InvitationExchangeOutcome.Expired;
        if (Status == WorkspaceInvitationStatus.Revoked)
            return InvitationExchangeOutcome.Revoked;
        if (Status is WorkspaceInvitationStatus.Accepted or WorkspaceInvitationStatus.Expired)
            return InvitationExchangeOutcome.Used;

        return token.Status switch
        {
            InvitationTokenStatus.Active => InvitationExchangeOutcome.Exchanged,
            InvitationTokenStatus.Exchanged or InvitationTokenStatus.Accepted => InvitationExchangeOutcome.Used,
            InvitationTokenStatus.Revoked => InvitationExchangeOutcome.Revoked,
            InvitationTokenStatus.Superseded => InvitationExchangeOutcome.Superseded,
            InvitationTokenStatus.Expired => InvitationExchangeOutcome.Expired,
            _ => InvitationExchangeOutcome.Unknown,
        };
    }

    private void EnsurePending(int expectedRevision, DateTime now)
    {
        EnsureRevision(expectedRevision);
        EnsurePendingStatus();
        if (ExpiresAt <= now)
            throw new InvalidOperationException("The invitation has expired.");
    }

    private void EnsurePendingStatus()
    {
        if (Status != WorkspaceInvitationStatus.Pending)
            throw new InvalidOperationException("Only a pending invitation can change.");
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (Revision != expectedRevision)
            throw new InvalidOperationException("Workspace invitation revision is stale.");
    }

    private void SupersedeActiveHandoffs()
    {
        foreach (InvitationHandoff handoff in handoffs.Where(candidate =>
                     candidate.Status == InvitationHandoffStatus.Active))
        {
            handoff.Supersede();
        }
    }

    private void RevokeActiveHandoffs()
    {
        foreach (InvitationHandoff handoff in handoffs.Where(candidate =>
                     candidate.Status == InvitationHandoffStatus.Active))
        {
            handoff.Revoke();
        }
    }

    private InvitationHandoff? FindHandoff(string handoffHash) =>
        handoffs.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.HandoffHash, handoffHash));

    private void StampCreated(ActorSnapshot actor) { CreatedByKind = actor.Kind; CreatedBySubjectId = actor.SubjectId; CreatedByDisplayName = actor.DisplayName; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName; }
    private static ActorSnapshot? Snapshot(ActorKind? kind, Guid? subjectId, string? displayName) => kind is ActorKind actorKind && !string.IsNullOrWhiteSpace(displayName) ? ActorSnapshot.Create(actorKind, subjectId, displayName) : null;
}
