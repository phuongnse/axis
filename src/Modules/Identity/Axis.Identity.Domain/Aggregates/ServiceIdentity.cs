using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class ServiceIdentity : AggregateRoot<Guid>
{
    private readonly List<ServiceIdentityKey> _keys = [];
    private readonly List<ServiceIdentityKeyTombstone> _tombstones = [];
    private ServiceIdentity() : base(Guid.Empty) { }
    private ServiceIdentity(Guid id, Guid workspaceId, string clientId, DateTime now) : base(id)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Length > 100)
            throw new ArgumentException("Workspace and a client identifier of at most 100 characters are required.");
        WorkspaceId = workspaceId; ClientId = clientId; Status = ServiceIdentityStatus.Active;
        WorkspaceGrantStatus = ServiceWorkspaceGrantStatus.Active; CreatedAt = now; UpdatedAt = now; Revision = 1;
    }
    public Guid WorkspaceId { get; private set; }
    public string ClientId { get; private set; } = null!;
    public ServiceIdentityStatus Status { get; private set; }
    public ServiceWorkspaceGrantStatus WorkspaceGrantStatus { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    private ActorKind? CreatedByKind { get; set; }
    private Guid? CreatedBySubjectId { get; set; }
    private string? CreatedByDisplayName { get; set; }
    private ActorKind? UpdatedByKind { get; set; }
    private Guid? UpdatedBySubjectId { get; set; }
    private string? UpdatedByDisplayName { get; set; }
    public ActorSnapshot? CreatedBy => Snapshot(CreatedByKind, CreatedBySubjectId, CreatedByDisplayName);
    public ActorSnapshot? UpdatedBy => Snapshot(UpdatedByKind, UpdatedBySubjectId, UpdatedByDisplayName);
    public int Revision { get; private set; }
    public IReadOnlyList<ServiceIdentityKey> Keys => _keys;
    public IReadOnlyList<ServiceIdentityKeyTombstone> Tombstones => _tombstones;
    public static ServiceIdentity Create(Guid workspaceId, string clientId, DateTime now) => new(Guid.NewGuid(), workspaceId, clientId.Trim(), now);
    public void InitializeMetadata(ActorSnapshot actor)
    {
        if (!actor.IsValid || CreatedBy is not null) throw new InvalidOperationException("Service identity creation provenance is invalid.");
        StampCreated(actor);
    }
    public void RecordModification(ActorSnapshot actor, DateTime now)
    {
        if (!actor.IsValid || (UpdatedAt.HasValue && now < UpdatedAt.Value)) throw new InvalidOperationException("Service identity modification provenance is invalid.");
        UpdatedAt = now; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName;
    }
    public ServiceIdentityKey AddKey(string kid, string thumbprint, string x, string y, int expectedRevision, DateTime now)
    {
        EnsureActive(expectedRevision);
        if (string.IsNullOrWhiteSpace(kid) || string.IsNullOrWhiteSpace(thumbprint)) throw new ArgumentException("Key identity is required.");
        if (_keys.Any(x => x.Kid == kid) || _tombstones.Any(x => x.Kid == kid || x.Thumbprint == thumbprint)) throw new InvalidOperationException("The key identifier or key material cannot be reused.");
        if (_keys.Any(x => x.Thumbprint == thumbprint)) throw new InvalidOperationException("The key material already exists.");
        ServiceIdentityKey key = new ServiceIdentityKey(Guid.NewGuid(), kid, thumbprint, x, y, now);
        _keys.Add(key); Revision++;
        return key;
    }
    public bool RevokeKey(Guid keyId, int expectedRevision, DateTime now)
    {
        ServiceIdentityKey key = _keys.SingleOrDefault(x => x.Id == keyId) ?? throw new InvalidOperationException("Key not found.");
        if (key.Status == ServiceIdentityKeyStatus.Revoked) return false;
        EnsureRevision(expectedRevision);
        key.Revoke(now); _tombstones.Add(new ServiceIdentityKeyTombstone(Guid.NewGuid(), key.Kid, key.Thumbprint, now)); Revision++;
        return true;
    }
    public bool Revoke(int expectedRevision, DateTime now)
    {
        if (Status == ServiceIdentityStatus.Revoked) return false;
        EnsureRevision(expectedRevision);
        foreach (ServiceIdentityKey? key in _keys.Where(x => x.Status == ServiceIdentityKeyStatus.Active)) { key.Revoke(now); _tombstones.Add(new ServiceIdentityKeyTombstone(Guid.NewGuid(), key.Kid, key.Thumbprint, now)); }
        Status = ServiceIdentityStatus.Revoked; WorkspaceGrantStatus = ServiceWorkspaceGrantStatus.Revoked; RevokedAt = now; Revision++;
        return true;
    }
    public bool HasActiveAuthority(Guid keyId) => Status == ServiceIdentityStatus.Active && WorkspaceGrantStatus == ServiceWorkspaceGrantStatus.Active && _keys.Any(x => x.Id == keyId && x.Status == ServiceIdentityKeyStatus.Active);
    private void EnsureActive(int revision) { EnsureRevision(revision); if (Status != ServiceIdentityStatus.Active || WorkspaceGrantStatus != ServiceWorkspaceGrantStatus.Active) throw new InvalidOperationException("Service identity is revoked."); }
    private void EnsureRevision(int revision) { if (Revision != revision) throw new InvalidOperationException("Service identity revision is stale."); }
    private void StampCreated(ActorSnapshot actor) { CreatedByKind = actor.Kind; CreatedBySubjectId = actor.SubjectId; CreatedByDisplayName = actor.DisplayName; UpdatedByKind = actor.Kind; UpdatedBySubjectId = actor.SubjectId; UpdatedByDisplayName = actor.DisplayName; }
    private static ActorSnapshot? Snapshot(ActorKind? kind, Guid? subjectId, string? displayName) => kind is ActorKind actorKind && !string.IsNullOrWhiteSpace(displayName) ? ActorSnapshot.Create(actorKind, subjectId, displayName) : null;
}
