using Axis.Shared.Domain.Primitives;

namespace Axis.Solutions.Domain;

public enum ProvisioningStatus
{
    Installing = 0,
    Installed = 1,
    Failed = 2,
}

public enum ComplianceStatus
{
    Compliant = 0,
    Noncompliant = 1,
}

public sealed class SolutionInstallation
{
    private SolutionInstallation()
    {
    }

    private SolutionInstallation(Guid workspaceId, string solutionKey, Guid solutionVersionId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        WorkspaceId = workspaceId;
        SolutionKey = solutionKey;
        SolutionVersionId = solutionVersionId;
        ProvisioningStatus = ProvisioningStatus.Installing;
        ComplianceStatus = ComplianceStatus.Compliant;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string SolutionKey { get; private set; } = string.Empty;
    public Guid SolutionVersionId { get; private set; }
    public ProvisioningStatus ProvisioningStatus { get; private set; }
    public ComplianceStatus ComplianceStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Revision { get; private set; }
    private ActorKind CreatedByKind { get; set; }
    private Guid? CreatedBySubjectId { get; set; }
    private string CreatedByDisplayName { get; set; } = string.Empty;
    private ActorKind UpdatedByKind { get; set; }
    private Guid? UpdatedBySubjectId { get; set; }
    private string UpdatedByDisplayName { get; set; } = string.Empty;
    public ActorSnapshot CreatedBy => Snapshot(CreatedByKind, CreatedBySubjectId, CreatedByDisplayName);
    public ActorSnapshot UpdatedBy => Snapshot(UpdatedByKind, UpdatedBySubjectId, UpdatedByDisplayName);

    public void InitializeMetadata(ActorSnapshot actor)
    {
        if (!actor.IsValid || CreatedByDisplayName.Length > 0)
            throw new InvalidOperationException("Solution-installation creation provenance is invalid.");
        CreatedByKind = actor.Kind;
        CreatedBySubjectId = actor.SubjectId;
        CreatedByDisplayName = actor.DisplayName;
        UpdatedByKind = actor.Kind;
        UpdatedBySubjectId = actor.SubjectId;
        UpdatedByDisplayName = actor.DisplayName;
    }

    public void RecordModification(ActorSnapshot actor)
    {
        if (!actor.IsValid)
            throw new InvalidOperationException("Solution-installation modification provenance is invalid.");
        UpdatedByKind = actor.Kind;
        UpdatedBySubjectId = actor.SubjectId;
        UpdatedByDisplayName = actor.DisplayName;
    }

    public static SolutionInstallation Create(
        Guid workspaceId,
        string solutionKey,
        Guid solutionVersionId,
        DateTimeOffset now)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(solutionKey)
            || solutionKey.Length > 63 || solutionVersionId == Guid.Empty || now == default)
            throw new ArgumentException("Installation data is incomplete.");
        return new SolutionInstallation(workspaceId, solutionKey, solutionVersionId, now);
    }

    public void MarkInstalled(DateTimeOffset now)
    {
        if (ProvisioningStatus != ProvisioningStatus.Installing)
            throw new InvalidOperationException("Only an installing solution can become installed.");
        ProvisioningStatus = ProvisioningStatus.Installed;
        Advance(now);
    }

    public void MarkFailed(DateTimeOffset now)
    {
        if (ProvisioningStatus == ProvisioningStatus.Installed)
            throw new InvalidOperationException("An installed solution cannot regress to failed.");
        ProvisioningStatus = ProvisioningStatus.Failed;
        Advance(now);
    }

    public void MarkNoncompliant(DateTimeOffset now)
    {
        if (ComplianceStatus == ComplianceStatus.Noncompliant)
            return;
        ComplianceStatus = ComplianceStatus.Noncompliant;
        Advance(now);
    }

    private void Advance(DateTimeOffset now)
    {
        if (now < UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now));
        UpdatedAt = now;
        Revision++;
    }

    private static ActorSnapshot Snapshot(
        ActorKind kind,
        Guid? subjectId,
        string displayName)
    {
        ActorSnapshot actor = new(kind, subjectId, displayName);
        return actor.IsValid
            ? actor
            : throw new InvalidOperationException("Solution installation provenance is incomplete.");
    }
}
