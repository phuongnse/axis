using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Workspace : AggregateRoot<Guid>
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    private Workspace(Guid id, string name, WorkspaceSlug slug, WorkspaceType type, Guid? organizationId)
        : base(id)
    {
        Name = NormalizeName(name);
        Slug = slug;
        Type = type;
        OrganizationId = organizationId;
        Status = WorkspaceStatus.Active;
        CreatedAt = DateTime.UtcNow;
        Revision = 1;
    }

    public string Name { get; private set; }
    public WorkspaceSlug Slug { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public int Revision { get; private set; }

    public static Workspace CreatePersonal(string name, WorkspaceSlug slug)
    {
        Workspace workspace = new(Guid.NewGuid(), name, slug, WorkspaceType.Personal, null);
        workspace.Status = WorkspaceStatus.PendingVerification;
        return workspace;
    }

    public static Workspace CreateOrganization(string name, WorkspaceSlug slug, Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));

        return new Workspace(Guid.NewGuid(), name, slug, WorkspaceType.Organization, organizationId);
    }

    public bool AllowsSignIn() => Status is WorkspaceStatus.Active;

    public void RecordLegalAcceptance(string termsVersion, string privacyVersion)
    {
        if (string.IsNullOrWhiteSpace(termsVersion))
            throw new ArgumentException("Terms version is required.", nameof(termsVersion));
        if (string.IsNullOrWhiteSpace(privacyVersion))
            throw new ArgumentException("Privacy version is required.", nameof(privacyVersion));

        AcceptedTermsVersion = termsVersion.Trim();
        AcceptedPrivacyVersion = privacyVersion.Trim();
        LegalAcceptedAt = DateTime.UtcNow;
    }

    public void ActivateAfterOwnerVerification()
    {
        if (Status == WorkspaceStatus.Active)
            return;

        if (Status != WorkspaceStatus.PendingVerification)
            throw new InvalidOperationException("Only pending Workspaces can be activated.");

        Status = WorkspaceStatus.Active;
        Revision++;
    }

    public void SetStatus(WorkspaceStatus status, int expectedRevision)
    {
        if (expectedRevision != Revision)
            throw new InvalidOperationException("Workspace revision is stale.");

        Status = status;
        Revision++;
    }

    private static string NormalizeName(string name)
    {
        string normalized = name?.Trim().Normalize() ?? string.Empty;
        if (normalized.Length is < MinNameLength or > MaxNameLength)
            throw new ArgumentException($"Workspace name must be between {MinNameLength} and {MaxNameLength} characters.", nameof(name));
        return normalized;
    }
}
