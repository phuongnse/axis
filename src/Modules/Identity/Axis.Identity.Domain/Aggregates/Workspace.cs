using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Workspace : AggregateRoot<Guid>
{
    public const int DeletionGracePeriodDays = 30;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    public string Name { get; private set; }
    public WorkspaceSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }

    private Workspace(
        Guid id,
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid? ownerUserId,
        WorkspaceType type,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        OwnerUserId = ownerUserId;
        Type = type;
        Status = WorkspaceStatus.Active;
        CreatedAt = createdAt;
    }

    public static Workspace Create(string name, WorkspaceSlug slug, Email ownerEmail)
    {
        return CreatePersonal(name, slug, ownerEmail, Guid.NewGuid());
    }

    public static Workspace CreatePersonal(
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("Owner user is required.", nameof(ownerUserId));

        Workspace workspace = CreateWorkspace(
            name,
            slug,
            ownerEmail,
            ownerUserId,
            WorkspaceType.Personal);
        workspace.Status = WorkspaceStatus.PendingVerification;
        return workspace;
    }

    private static Workspace CreateWorkspace(
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid? ownerUserId,
        WorkspaceType type)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workspace name is required.", nameof(name));

        Workspace workspace = new Workspace(
            Guid.NewGuid(),
            name.Trim(),
            slug,
            ownerEmail,
            ownerUserId,
            type,
            DateTime.UtcNow);
        return workspace;
    }

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
    }

    public bool AllowsSignIn() =>
        Status is WorkspaceStatus.Active;
}
