namespace Axis.Authorization.Domain;

public enum AuthorizationSubjectKind { Human = 0, Service = 1 }
public readonly record struct AuthorizationSubjectReference(AuthorizationSubjectKind Kind, Guid Id)
{
    public bool IsValid => Id != Guid.Empty && Enum.IsDefined(Kind);
}

public sealed class ProductRoleAssignment
{
    private ProductRoleAssignment(Guid id, Guid workspaceId, AuthorizationSubjectReference subject, Guid policyVersionId, string roleKey, DateTime createdAt)
    {
        Id = id; WorkspaceId = workspaceId; Subject = subject; PolicyVersionId = policyVersionId; RoleKey = roleKey; IsActive = true; Revision = 1; CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid WorkspaceId { get; }
    public AuthorizationSubjectReference Subject { get; }
    public Guid PolicyVersionId { get; }
    public string RoleKey { get; }
    public bool IsActive { get; private set; }
    public int Revision { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? RevokedAt { get; private set; }

    public static ProductRoleAssignment? Create(Guid workspaceId, AuthorizationSubjectReference subject, Guid policyVersionId, string roleKey, DateTime now) =>
        workspaceId == Guid.Empty || !subject.IsValid || policyVersionId == Guid.Empty || string.IsNullOrWhiteSpace(roleKey)
            ? null : new(Guid.NewGuid(), workspaceId, subject, policyVersionId, roleKey.Trim(), now);

    public bool Revoke(int expectedRevision, DateTime now)
    {
        if (!IsActive || expectedRevision != Revision) return false;
        IsActive = false; Revision++; RevokedAt = now; return true;
    }
}
