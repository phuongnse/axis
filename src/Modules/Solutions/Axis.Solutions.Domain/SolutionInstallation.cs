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
}
